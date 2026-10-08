using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Drawing;
using Overlap.Api.Cooking;
using Overlap.Api.Quiz;
using Overlap.Api.Watch;
using Xunit;

namespace Overlap.Api.Tests;

public sealed partial class TogetherTests
{
    [Fact]
    public async Task RemovingCreatorArchiveRevokesOnlyTheirRetainedAccess()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        var owner=await SignIn(factory,first,"archive-removal");await SignIn(factory,second,"archive-other");
        factory.Services.GetRequiredService<IConfiguration>()["ConnectionHistory:CreatorEmail"]="archive-removal@example.test";
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var pair=await db.PairMembers.Where(x=>x.AccountId==owner).Select(x=>x.PairId).SingleAsync();
        Assert.Equal(HttpStatusCode.NotFound,(await Post(first,$"connection-history/{pair}/remove",new{confirm=true})).StatusCode);
        var drawing=new DrawingRound{PairId=pair,CreatedBy=owner,Prompt="A waffle",Seconds=60};db.Add(drawing);await db.SaveChangesAsync();
        (await Post(first,"together/leave",new{acceptTerms=true})).EnsureSuccessStatusCode();
        var listing=await first.GetFromJsonAsync<JsonElement>("/api/auth/connection-history");
        Assert.Equal("archive-other",listing.GetProperty("connections")[0].GetProperty("partnerName").GetString());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(second,$"connection-history/{pair}/remove",new{confirm=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,$"connection-history/{pair}/remove",new{confirm=false})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await first.PostAsJsonAsync($"/api/auth/connection-history/{pair}/remove",new{confirm=true})).StatusCode);
        (await Post(first,$"connection-history/{pair}/remove",new{confirm=true})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,(await first.GetAsync($"/api/auth/connection-history/{pair}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await first.GetAsync($"/api/auth/connection-history/{pair}/export")).StatusCode);
        (await second.GetAsync($"/api/auth/connection-history/{pair}/export")).EnsureSuccessStatusCode();
        Assert.Single(await db.Set<DrawingRound>().AsNoTracking().ToArrayAsync());
        Assert.Single(await db.Set<AlbumAccess>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync());
    }
    [Fact]
    public async Task DisconnectRetainsRecordsRevokesAccessAndLimitsExportsToThirtyDays()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);
        using var first=factory.Client();using var second=factory.Client();using var stranger=factory.Client();
        var owner=await SignIn(factory,first,"history-owner");var partner=await SignIn(factory,second,"history-partner");await SignIn(factory,stranger,"history-stranger");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var pair=await db.PairMembers.Where(x=>x.AccountId==owner).Select(x=>x.PairId).SingleAsync();
        var drawing=new DrawingRound{PairId=pair,CreatedBy=owner,Prompt="A waffle",Seconds=60,CreatedUtc=DateTime.UtcNow};
        var quiz=new QuizRound{PairId=pair,ProposedBy=owner,QuestionsJson=JsonSerializer.Serialize(new[]{new QuizQuestion("one","custom","A private answer?",["Yes","No"])}),Status="active"};
        var pick=new SharedPick{PairId=pair,ProposedBy=owner,TitleId="movie-1",Content=JsonSerializer.Serialize(PickTitle()),IsShared=true};
        db.AddRange(drawing,quiz,pick);await db.SaveChangesAsync();
        db.Add(new DrawingPhoto{RoundId=drawing.Id,AccountId=owner,Png=AvatarImage.Encode(new byte[128*128*3]),UploadedUtc=DateTime.UtcNow});
        db.Add(new QuizAnswer{RoundId=quiz.Id,AccountId=partner,QuestionIndex=0,OwnChoice=1,GuessChoice=0});await db.SaveChangesAsync();
        var recipe=new SavedRecipe{PairId=pair,AddedBy=owner,SourceId=1,Content="{}"};db.Add(recipe);await db.SaveChangesAsync();
        db.Add(new RecipeExperience{RecipeId=recipe.Id,AccountId=partner,Stars=5,MadeUtc=DateTime.UtcNow});await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"together/leave",new{acceptTerms=false})).StatusCode);
        Assert.Equal(2,await db.PairMembers.CountAsync());
        (await Post(first,"together/leave",new{acceptTerms=true})).EnsureSuccessStatusCode();
        var history=await db.Set<ConnectionHistory>().AsNoTracking().SingleAsync(x=>x.PairId==pair);
        Assert.True(history.Closed);Assert.Equal(TimeSpan.FromDays(30),history.ExportExpiresUtc-history.DisconnectedUtc);
        Assert.Empty(await db.PairMembers.AsNoTracking().ToArrayAsync());Assert.Single(await db.Set<DrawingPhoto>().ToArrayAsync());
        Assert.True((await db.Set<DrawingRound>().AsNoTracking().SingleAsync()).Cancelled);
        Assert.Equal("cancelled",(await db.Set<QuizRound>().AsNoTracking().SingleAsync()).Status);
        Assert.Empty((await first.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks")).GetProperty("picks").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(first,$"watch/picks/{pick.Id}/share",new{shared=false})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(first,$"watch/picks/{pick.Id}/remove",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/auth/drawing/{drawing.Id}/photos/{owner}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await first.GetAsync($"/api/auth/connection-history/{pair}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/auth/connection-history/{pair}/export")).StatusCode);
        var response=await first.GetAsync($"/api/auth/connection-history/{pair}/export");if(!response.IsSuccessStatusCode)Assert.Fail(await response.Content.ReadAsStringAsync());
        using var zip=new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.NotNull(zip.GetEntry($"pictures/drawing-{drawing.Id}-{owner}.png"));
        using var json=JsonDocument.Parse(zip.GetEntry("connection.json")!.Open());
        Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("quizzes")[0].GetProperty("questions")[0].GetProperty("reveal").ValueKind);
        Assert.Empty(json.RootElement.GetProperty("recipes")[0].GetProperty("experiences").EnumerateArray());
        Assert.EndsWith("Z",json.RootElement.GetProperty("connection").GetProperty("disconnectedUtc").GetString());
        (await second.GetAsync($"/api/auth/connection-history/{pair}/export")).EnsureSuccessStatusCode();
        (await Post(stranger,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        Assert.Empty((await stranger.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
        (await Post(first,"watch/picks",PickTitle())).EnsureSuccessStatusCode();
        Assert.Equal(2,await db.Set<SharedPick>().CountAsync());
        await db.Set<ConnectionHistory>().Where(x=>x.PairId==pair).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExportExpiresUtc,DateTime.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.NotFound,(await first.GetAsync($"/api/auth/connection-history/{pair}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/auth/connection-history/{pair}/export")).StatusCode);
        Assert.Empty((await first.GetFromJsonAsync<JsonElement>("/api/auth/connection-history")).GetProperty("connections").EnumerateArray());
        Assert.Single(await db.Set<DrawingPhoto>().ToArrayAsync());
    }

    [Fact]
    public async Task CreatorArchiveRequiresVerifiedConfiguredEmailAndOriginalMembership()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        var owner=await SignIn(factory,first,"creator-test");await SignIn(factory,second,"creator-partner");
        factory.Services.GetRequiredService<IConfiguration>()["ConnectionHistory:CreatorEmail"]="creator-test@example.test";
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var pair=await db.PairMembers.Where(x=>x.AccountId==owner).Select(x=>x.PairId).SingleAsync();
        var drawing=new DrawingRound{PairId=pair,CreatedBy=owner,Prompt="A waffle",Seconds=60};db.Add(drawing);await db.SaveChangesAsync();
        db.Add(new DrawingPhoto{RoundId=drawing.Id,AccountId=owner,Png=AvatarImage.Encode(new byte[128*128*3])});await db.SaveChangesAsync();
        (await Post(second,"together/leave",new{acceptTerms=true})).EnsureSuccessStatusCode();
        await db.Set<ConnectionHistory>().Where(x=>x.PairId==pair).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExportExpiresUtc,DateTime.UtcNow.AddDays(-1)));
        (await first.GetAsync($"/api/auth/connection-history/{pair}/archive")).EnsureSuccessStatusCode();
        (await first.GetAsync($"/api/auth/connection-history/{pair}/photo/drawing/{drawing.Id}/{owner}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/auth/connection-history/{pair}/photo/drawing/{drawing.Id}/{owner}")).StatusCode);
        var export=await first.GetAsync($"/api/auth/connection-history/{pair}/export");if(!export.IsSuccessStatusCode)Assert.Fail(await export.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/auth/connection-history/{pair}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await first.GetAsync("/api/auth/connection-history/unrelated/archive")).StatusCode);
        await db.Users.Where(x=>x.Id==owner).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.EmailConfirmed,false));
        Assert.Equal(HttpStatusCode.NotFound,(await first.GetAsync($"/api/auth/connection-history/{pair}/archive")).StatusCode);
        await db.Users.Where(x=>x.Id==owner).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.EmailConfirmed,true));
        (await Post(first,"delete-account",new{password="abcdefghij",confirm=true})).EnsureSuccessStatusCode();
        await SignIn(factory,first,"creator-test");
        Assert.True((await first.GetFromJsonAsync<JsonElement>("/api/auth/connection-history")).GetProperty("creator").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound,(await first.GetAsync($"/api/auth/connection-history/{pair}/archive")).StatusCode);
    }
}
