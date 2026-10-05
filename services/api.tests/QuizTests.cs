using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Quiz;
using Xunit;
namespace Overlap.Api.Tests;
public class QuizCatalogTests
{
    [Fact]
    public void BankHas120UniqueQuestionsIn12CategoriesAndSelectionPrefersUnplayedQuestions(){
        var questions=QuizCatalog.Categories.SelectMany(x=>x.Questions).ToArray();Assert.Equal(120,questions.Length);Assert.Equal(12,QuizCatalog.Categories.Length);
        Assert.Equal(120,questions.Select(x=>x.Id).Distinct().Count());Assert.All(QuizCatalog.Categories,c=>{Assert.Equal(10,c.Questions.Length);Assert.All(c.Questions,q=>{Assert.Equal(c.Id,q.CategoryId);Assert.Equal(4,q.Choices.Length);Assert.Equal(4,q.Choices.Distinct().Count());});});
        var first=QuizCatalog.Select(["food"],[]);Assert.Equal(5,first.Length);var second=QuizCatalog.Select(["food"],first.Select(x=>x.Id));Assert.Empty(first.Select(x=>x.Id).Intersect(second.Select(x=>x.Id)));
        Assert.Throws<ArgumentException>(()=>QuizCatalog.Select([],[]));Assert.Throws<ArgumentException>(()=>QuizCatalog.Select(["missing"],[]));
    }
    [Fact]
    public void RevealRequiresBothSubmissionsAndSkipDoesNotCountAsWrong(){
        var own=new QuizAnswer{AccountId="own",OwnChoice=0,GuessChoice=1};Assert.Null(QuizPlay.Reveal(own,null));
        var partner=new QuizAnswer{AccountId="partner",OwnChoice=1,GuessChoice=1};var reveal=QuizPlay.Reveal(own,partner)!;Assert.True(reveal.OwnCorrect);Assert.False(reveal.PartnerCorrect);
        partner.Skipped=true;partner.OwnChoice=null;partner.GuessChoice=null;reveal=QuizPlay.Reveal(own,partner)!;Assert.Null(reveal.OwnCorrect);Assert.Null(reveal.PartnerCorrect);
    }
}
public sealed partial class TogetherTests
{
    [Fact]
    public async Task CustomQuizzesAreAuthorPrivateAndInvitationsKeepIndependentSnapshots(){
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        await SignIn(factory,first,"custom-quiz-first");await SignIn(factory,second,"custom-quiz-second");(await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        var questions=Enumerable.Range(0,5).Select(i=>new{prompt="My question "+i,choices=new[]{"One","Two","Three","Four"}}).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"quiz/templates",new{title="Bad",questions=questions.Take(4),revision=(string?)null})).StatusCode);
        var saved=await Post(first,"quiz/templates",new{title="Our habits",questions,revision=(string?)null});saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var own=await first.GetFromJsonAsync<JsonElement>("/api/auth/quiz/templates");Assert.Single(own.EnumerateArray());Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/quiz/templates")).EnumerateArray());
        var revision=own[0].GetProperty("revision").GetString();Assert.Equal(HttpStatusCode.NotFound,(await Post(second,$"quiz/templates/{id}",new{title="Changed",questions,revision})).StatusCode);
        var invited=await Post(first,"quiz/rounds",new{templateId=id});invited.EnsureSuccessStatusCode();var roundId=(await invited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var round=await second.GetFromJsonAsync<JsonElement>($"/api/auth/quiz/rounds/{roundId}");Assert.Equal("Our habits",round.GetProperty("title").GetString());Assert.Equal("My question 0",round.GetProperty("questions")[0].GetProperty("prompt").GetString());
        var changed=questions.Select(q=>new{prompt=q.prompt+" edited",q.choices}).ToArray();(await Post(first,$"quiz/templates/{id}",new{title="New title",questions=changed,revision})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"quiz/templates/{id}",new{title="Stale",questions,revision})).StatusCode);
        Assert.Equal("My question 0",(await second.GetFromJsonAsync<JsonElement>($"/api/auth/quiz/rounds/{roundId}")).GetProperty("questions")[0].GetProperty("prompt").GetString());
        revision=(await first.GetFromJsonAsync<JsonElement>("/api/auth/quiz/templates"))[0].GetProperty("revision").GetString();(await Post(first,$"quiz/templates/{id}/remove",new{revision})).EnsureSuccessStatusCode();
        Assert.Equal("Our habits",(await first.GetFromJsonAsync<JsonElement>($"/api/auth/quiz/rounds/{roundId}")).GetProperty("title").GetString());
        (await Post(first,"quiz/templates",new{title="Another",questions,revision=(string?)null})).EnsureSuccessStatusCode();(await Post(first,"together/leave",new{})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<CustomQuiz>().AsNoTracking().ToArrayAsync());
    }
    [Fact]
    public async Task QuizPersistsPrivateAnswersRevealsBothAndKeepsLiveProgressAndHistory(){
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();using var outsider=factory.Client();
        await SignIn(factory,first,"quiz-first");await SignIn(factory,second,"quiz-second");await SignIn(factory,outsider,"quiz-outsider");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        var created=await Post(first,"quiz/rounds",new{categoryIds=new[]{"food","everyday"}});created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        async Task<JsonElement> Read(HttpClient client)=>await client.GetFromJsonAsync<JsonElement>($"/api/auth/quiz/rounds/{id}");
        async Task<HttpResponseMessage> Action(HttpClient client,string action,string? revision=null)=>await Post(client,$"quiz/rounds/{id}/action",new{revision=revision ?? (await Read(client)).GetProperty("revision").GetString(),action});
        Assert.Single((await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("quizInvitations").EnumerateArray());
        Assert.Empty((await outsider.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("quizInvitations").EnumerateArray());
        var pending=await Read(first);Assert.Equal(5,pending.GetProperty("questions").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync($"/api/auth/quiz/rounds/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Action(first,"accept")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"quiz/rounds/{id}/answer",new{questionIndex=0,ownChoice=0,guessChoice=1,skipped=false})).StatusCode);
        (await Action(second,"accept")).EnsureSuccessStatusCode();
        for(var i=0;i<5;i++){
            var skipped=i==2;
            (await Post(first,$"quiz/rounds/{id}/answer",new{questionIndex=i,ownChoice=skipped ? (int?)null : 0,guessChoice=skipped ? (int?)null : 1,skipped})).EnsureSuccessStatusCode();
            var privateState=await Read(second);var q=privateState.GetProperty("questions")[i];Assert.True(q.GetProperty("partnerSubmitted").GetBoolean());Assert.Equal(JsonValueKind.Null,q.GetProperty("reveal").ValueKind);Assert.Equal(JsonValueKind.Null,q.GetProperty("ownAnswer").ValueKind);
            Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"quiz/rounds/{id}/answer",new{questionIndex=i,ownChoice=2,guessChoice=2,skipped=false})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await Action(first,"next")).StatusCode);
            (await Post(second,$"quiz/rounds/{id}/answer",new{questionIndex=i,ownChoice=1,guessChoice=1,skipped=false})).EnsureSuccessStatusCode();
            var state=await Read(first);var reveal=state.GetProperty("questions")[i].GetProperty("reveal");
            if(skipped){Assert.Equal(JsonValueKind.Null,reveal.GetProperty("ownCorrect").ValueKind);}else{Assert.True(reveal.GetProperty("ownCorrect").GetBoolean());Assert.False(reveal.GetProperty("partnerCorrect").GetBoolean());}
            if(i<4){var revision=state.GetProperty("revision").GetString();(await Action(first,"next",revision)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Action(second,"next",revision)).StatusCode);Assert.Equal(i+1,(await Read(second)).GetProperty("currentIndex").GetInt32());}
        }
        var completed=await Read(second);Assert.Equal("completed",completed.GetProperty("status").GetString());Assert.All(completed.GetProperty("questions").EnumerateArray(),q=>Assert.NotEqual(JsonValueKind.Null,q.GetProperty("reveal").ValueKind));
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();Assert.Equal(10,await db.Set<QuizAnswer>().CountAsync());
        Assert.Single((await second.GetFromJsonAsync<JsonElement>("/api/auth/quiz/rounds")).GetProperty("rounds").EnumerateArray());
        (await Post(first,"together/leave",new{})).EnsureSuccessStatusCode();Assert.Empty(await db.Set<QuizRound>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<QuizAnswer>().AsNoTracking().ToArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/auth/quiz/rounds/{id}")).StatusCode);
    }
    [Fact]
    public async Task QuizCancellationKeepsUnrevealedAnswersPrivateAndRemovalDeletesSharedAnswers(){
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        await SignIn(factory,first,"quiz-cancel-first");await SignIn(factory,second,"quiz-cancel-second");(await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        var created=await Post(first,"quiz/rounds",new{categoryIds=new[]{"personality"}});created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        async Task<JsonElement> Read()=>await second.GetFromJsonAsync<JsonElement>($"/api/auth/quiz/rounds/{id}");
        (await Post(second,$"quiz/rounds/{id}/action",new{revision=(await Read()).GetProperty("revision").GetString(),action="accept"})).EnsureSuccessStatusCode();
        (await Post(first,$"quiz/rounds/{id}/answer",new{questionIndex=0,ownChoice=0,guessChoice=0,skipped=false})).EnsureSuccessStatusCode();
        (await Post(second,$"quiz/rounds/{id}/action",new{revision=(await Read()).GetProperty("revision").GetString(),action="cancel"})).EnsureSuccessStatusCode();
        var closed=await Read();Assert.Equal("cancelled",closed.GetProperty("status").GetString());Assert.Equal(JsonValueKind.Null,closed.GetProperty("questions")[0].GetProperty("reveal").ValueKind);
        (await Post(second,$"quiz/rounds/{id}/remove",new{revision=closed.GetProperty("revision").GetString()})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,(await first.GetAsync($"/api/auth/quiz/rounds/{id}")).StatusCode);
        using var scope=factory.Services.CreateScope();Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<QuizAnswer>().AsNoTracking().ToArrayAsync());
    }
}
