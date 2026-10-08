using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Cooking;
using Xunit;
namespace Overlap.Api.Tests;

public sealed partial class TogetherTests
{
    [Fact]
    public async Task RedrawRequiresPartnerConsentAndExcludesEveryPriorDrawAndMadeRecipe()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        var owner=await SignIn(factory,first,"redraw-first");await SignIn(factory,second,"redraw-second");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        foreach(var client in new[]{first,second})(await Post(client,"cooking/preferences",new{avoidRedMeat=false,excludedIngredients=Array.Empty<string>()})).EnsureSuccessStatusCode();
        async Task<long> CreateContest(){
            var response=await Post(first,"cooking/contests",new{localStart=DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm"),timeZone="UTC",availableMinutes=60});response.EnsureSuccessStatusCode();
            var id=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
            var revision=(await second.GetFromJsonAsync<JsonElement>($"/api/auth/cooking/contests/{id}")).GetProperty("revision").GetString();
            (await Post(second,$"cooking/contests/{id}/respond",new{revision,action="accept"})).EnsureSuccessStatusCode();
            using var scope=factory.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<CookingContest>().Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.StartUtc,DateTime.UtcNow.AddSeconds(-1)));return id;
        }
        var contestId=await CreateContest();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();var pair=(await db.PairMembers.SingleAsync(x=>x.AccountId==owner)).PairId;
        for(var i=1;i<=3;i++)db.Add(new SavedRecipe{PairId=pair,AddedBy=owner,SourceId=i,AddedUtc=DateTime.UtcNow,Content=JsonSerializer.Serialize(new Recipe(i,1,"Dish "+i,["Milk"],["Mix"],"https://en.wikibooks.org",null))});await db.SaveChangesAsync();
        var made=await db.Set<SavedRecipe>().SingleAsync(x=>x.SourceId==1);(await Post(first,$"cooking/collection/{made.Id}/experience",new{made=true,stars=4})).EnsureSuccessStatusCode();
        (await Post(first,$"cooking/contests/{contestId}/shuffle",new{})).EnsureSuccessStatusCode();
        async Task<JsonElement> Detail()=>await first.GetFromJsonAsync<JsonElement>($"/api/auth/cooking/contests/{contestId}");
        var original=await Detail();var oldRecipe=original.GetProperty("recipe").GetProperty("id").GetInt64();Assert.NotEqual(1,oldRecipe);
        async Task<HttpResponseMessage> Redraw(HttpClient client,string action)=>await Post(client,$"cooking/contests/{contestId}/redraw",new{revision=(await Detail()).GetProperty("revision").GetString(),action});
        (await Redraw(first,"request")).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await Redraw(first,"accept")).StatusCode);
        Assert.Equal(oldRecipe,(await Detail()).GetProperty("recipe").GetProperty("id").GetInt64());
        (await Redraw(second,"decline")).EnsureSuccessStatusCode();Assert.Equal(oldRecipe,(await Detail()).GetProperty("recipe").GetProperty("id").GetInt64());
        (await Redraw(first,"request")).EnsureSuccessStatusCode();var consentRevision=(await Detail()).GetProperty("revision").GetString();
        (await Redraw(second,"accept")).EnsureSuccessStatusCode();var next=await Detail();Assert.NotEqual(oldRecipe,next.GetProperty("recipe").GetProperty("id").GetInt64());Assert.NotEqual(1,next.GetProperty("recipe").GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"cooking/contests/{contestId}/redraw",new{revision=consentRevision,action="accept"})).StatusCode);
        (await Redraw(first,"request")).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Redraw(second,"accept")).StatusCode);
        Assert.Equal(next.GetProperty("recipe").GetRawText(),(await Detail()).GetProperty("recipe").GetRawText());
        (await Redraw(first,"withdraw")).EnsureSuccessStatusCode();
        var another=await CreateContest();Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"cooking/contests/{another}/shuffle",new{})).StatusCode);
        Assert.Equal(2,await db.Set<ContestRecipeDraw>().CountAsync());
    }
    [Fact]
    public async Task PersonalFoodListsRequireOwnerPermissionAndDoNotExposeToANewConnection()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();using var stranger=factory.Client();
        await SignIn(factory,first,"food-owner");await SignIn(factory,second,"food-partner");await SignIn(factory,stranger,"food-stranger");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        (await Post(first,"cooking/preferences",new{avoidRedMeat=true,excludedIngredients=new[]{"tuna"},shareWithPartner=false})).EnsureSuccessStatusCode();
        var state=await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences");
        Assert.True(state.GetProperty("partnerConfirmed").GetBoolean());Assert.False(state.GetProperty("partnerShared").GetBoolean());Assert.Equal(JsonValueKind.Null,state.GetProperty("partnerPreferences").ValueKind);
        (await Post(first,"cooking/preferences",new{avoidRedMeat=true,excludedIngredients=new[]{"tuna"},shareWithPartner=true})).EnsureSuccessStatusCode();
        state=await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences");Assert.Equal("tuna",state.GetProperty("partnerPreferences").GetProperty("excludedIngredients")[0].GetString());
        Assert.Equal(JsonValueKind.Null,(await stranger.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences")).GetProperty("partnerPreferences").ValueKind);
        (await Post(first,"cooking/preferences",new{avoidRedMeat=true,excludedIngredients=new[]{"tuna"},shareWithPartner=false})).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null,(await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences")).GetProperty("partnerPreferences").ValueKind);
        (await Post(first,"cooking/preferences",new{avoidRedMeat=true,excludedIngredients=new[]{"tuna"},shareWithPartner=true})).EnsureSuccessStatusCode();
        (await Post(first,"cooking/preferences",new{avoidRedMeat=false,excludedIngredients=new[]{" spinach ","olives","SPINACH"},shareWithPartner=true})).EnsureSuccessStatusCode();
        var updated=await first.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences");
        Assert.False(updated.GetProperty("ownPreferences").GetProperty("avoidRedMeat").GetBoolean());
        Assert.Equal(new[]{"spinach","olives"},updated.GetProperty("ownPreferences").GetProperty("excludedIngredients").EnumerateArray().Select(x=>x.GetString()).ToArray());
        Assert.Equal(new[]{"spinach","olives"},(await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences")).GetProperty("partnerPreferences").GetProperty("excludedIngredients").EnumerateArray().Select(x=>x.GetString()).ToArray());
        (await Post(first,"cooking/preferences",new{avoidRedMeat=true,excludedIngredients=Array.Empty<string>(),shareWithPartner=false})).EnsureSuccessStatusCode();
        Assert.Empty((await first.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences")).GetProperty("ownPreferences").GetProperty("excludedIngredients").EnumerateArray());
        (await Post(first,"together/leave",new {acceptTerms=true})).EnsureSuccessStatusCode();
        Assert.True((await first.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences")).GetProperty("ownPreferences").GetProperty("avoidRedMeat").GetBoolean());
        (await Post(stranger,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        state=await stranger.GetFromJsonAsync<JsonElement>("/api/auth/cooking/preferences");Assert.True(state.GetProperty("partnerConfirmed").GetBoolean());Assert.False(state.GetProperty("partnerShared").GetBoolean());Assert.Equal(JsonValueKind.Null,state.GetProperty("partnerPreferences").ValueKind);
    }
    [Fact]
    public async Task ShuffleLocksUntilStartAndBothMembersReceiveOnePersistedRecipe()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();using var stranger=factory.Client();
        var owner=await SignIn(factory,first,"shuffle-first");await SignIn(factory,second,"shuffle-second");await SignIn(factory,stranger,"shuffle-stranger");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        var created=await Post(first,"cooking/contests",new{localStart=DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm"),timeZone="UTC",availableMinutes=60});created.EnsureSuccessStatusCode();
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"cooking/contests/{id}/shuffle",new{})).StatusCode);
        var contest=(await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("cookingContests")[0];var revision=contest.GetProperty("revision").GetString();
        (await Post(second,$"cooking/contests/{id}/respond",new{revision,action="accept"})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"cooking/contests/{id}/shuffle",new{})).StatusCode);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();var pair=(await db.PairMembers.SingleAsync(x=>x.AccountId==owner)).PairId;
        await db.Set<CookingContest>().Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.StartUtc,DateTime.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"cooking/contests/{id}/shuffle",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await Post(stranger,"cooking/preferences",new{avoidRedMeat=true,excludedIngredients=new[]{"tuna"}})).StatusCode);
        (await Post(first,"cooking/preferences",new{avoidRedMeat=true,excludedIngredients=Array.Empty<string>()})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"cooking/contests/{id}/shuffle",new{})).StatusCode);
        (await Post(second,"cooking/preferences",new{avoidRedMeat=false,excludedIngredients=new[]{"tuna"}})).EnsureSuccessStatusCode();
        var preferences=(await first.GetFromJsonAsync<JsonElement>($"/api/auth/cooking/contests/{id}"));
        Assert.True(preferences.GetProperty("ownPreferences").GetProperty("avoidRedMeat").GetBoolean());Assert.True(preferences.GetProperty("partnerConfirmed").GetBoolean());Assert.Equal(JsonValueKind.Null,preferences.GetProperty("partnerPreferences").ValueKind);
        for(var i=1;i<=2;i++)db.Add(new SavedRecipe{PairId=pair,AddedBy=owner,SourceId=i,AddedUtc=DateTime.UtcNow,Content=JsonSerializer.Serialize(new Recipe(i,34,"Recipe "+i,[i==1 ? "Beef" : "Tuna"],["Mix"],"https://en.wikibooks.org",null))});
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"cooking/contests/{id}/shuffle",new{})).StatusCode);
        db.Add(new SavedRecipe{PairId=pair,AddedBy=owner,SourceId=3,AddedUtc=DateTime.UtcNow,Content=JsonSerializer.Serialize(new Recipe(3,34,"Flan",["Milk"],["Mix"],"https://en.wikibooks.org",null))});await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound,(await Post(stranger,$"cooking/contests/{id}/shuffle",new{})).StatusCode);
        Assert.Equal(JsonValueKind.Null,(await second.GetFromJsonAsync<JsonElement>($"/api/auth/cooking/contests/{id}")).GetProperty("recipe").ValueKind);
        (await Post(first,$"cooking/contests/{id}/shuffle",new{})).EnsureSuccessStatusCode();
        var firstDraw=await first.GetFromJsonAsync<JsonElement>($"/api/auth/cooking/contests/{id}");
        Assert.Equal("Flan",firstDraw.GetProperty("recipe").GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NoContent,(await Post(first,"cooking/preferences",new{avoidRedMeat=false,excludedIngredients=Array.Empty<string>()})).StatusCode);
        (await Post(second,$"cooking/contests/{id}/shuffle",new{})).EnsureSuccessStatusCode();
        var secondDraw=await second.GetFromJsonAsync<JsonElement>($"/api/auth/cooking/contests/{id}");
        Assert.Equal(firstDraw.GetProperty("recipe").GetRawText(),secondDraw.GetProperty("recipe").GetRawText());
        Assert.Equal(firstDraw.GetProperty("revealedUtc").GetString(),secondDraw.GetProperty("revealedUtc").GetString());
        await db.Set<SavedRecipe>().Where(x=>x.PairId==pair).ExecuteDeleteAsync();
        Assert.Equal(firstDraw.GetProperty("recipe").GetRawText(),(await second.GetFromJsonAsync<JsonElement>($"/api/auth/cooking/contests/{id}")).GetProperty("recipe").GetRawText());
    }
    [Fact]
    public async Task CookingContestRequiresPartnerAcceptanceAndRevisionAwareNotifications()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);
        using var first=factory.Client();using var second=factory.Client();using var stranger=factory.Client();
        await SignIn(factory,first,"contest-first");await SignIn(factory,second,"contest-second");await SignIn(factory,stranger,"contest-stranger");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        var start=DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm");
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"cooking/contests",new{localStart="",timeZone="UTC",availableMinutes=60})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"cooking/contests",new{localStart=DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm"),timeZone="UTC",availableMinutes=60})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"cooking/contests",new{localStart=start,timeZone="UTC",availableMinutes=1})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(stranger,"cooking/contests",new{localStart=start,timeZone="UTC",availableMinutes=60})).StatusCode);
        var created=await Post(first,"cooking/contests",new{localStart=start,timeZone="UTC",availableMinutes=60});created.EnsureSuccessStatusCode();
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var news=await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
        var contest=Assert.Single(news.GetProperty("cookingContests").EnumerateArray());
        Assert.Equal("pending",contest.GetProperty("status").GetString());Assert.Equal(1,news.GetProperty("unreadContests").GetInt32());
        Assert.EndsWith("Z",contest.GetProperty("startUtc").GetString());
        var revision=contest.GetProperty("revision").GetString();
        Assert.Equal(HttpStatusCode.Gone,(await Post(first,$"cooking/contests/{id}/recipe",new{revision,recipeId=1})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(first,$"cooking/contests/{id}/respond",new{revision,action="accept"})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(stranger,$"cooking/contests/{id}/respond",new{revision,action="accept"})).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("cookingContests").EnumerateArray());
        (await Post(second,$"cooking/contests/{id}/respond",new{revision,action="accept"})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"cooking/contests/{id}/respond",new{revision,action="decline"})).StatusCode);
        news=await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy");contest=news.GetProperty("cookingContests")[0];
        Assert.Equal("accepted",contest.GetProperty("status").GetString());Assert.Equal(1,news.GetProperty("unreadContests").GetInt32());
        (await Post(first,"privacy/read",new{kind="contest",id,revision,dismiss=true})).EnsureSuccessStatusCode();
        news=await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy");Assert.Equal(1,news.GetProperty("unreadContests").GetInt32());
        revision=contest.GetProperty("revision").GetString();
        (await Post(first,"privacy/read",new{kind="contest",id,revision,dismiss=true})).EnsureSuccessStatusCode();
        news=await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy");Assert.True(news.GetProperty("cookingContests")[0].GetProperty("dismissed").GetBoolean());
        Assert.Equal(0,news.GetProperty("unreadContests").GetInt32());
        (await Post(second,$"cooking/contests/{id}/respond",new{revision,action="cancel"})).EnsureSuccessStatusCode();
        news=await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy");Assert.False(news.GetProperty("cookingContests")[0].GetProperty("dismissed").GetBoolean());Assert.Equal(1,news.GetProperty("unreadContests").GetInt32());
        Assert.Equal("cancelled",news.GetProperty("cookingContests")[0].GetProperty("status").GetString());
        (await Post(first,"together/leave",new {acceptTerms=true})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();Assert.Single(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<CookingContest>().ToListAsync());
    }
    [Fact]
    public async Task DeclinedAndElapsedContestsCannotBeAccepted()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        await SignIn(factory,first,"contest-expired-first");await SignIn(factory,second,"contest-expired-second");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        var created=await Post(first,"cooking/contests",new{localStart=DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm"),timeZone="UTC",availableMinutes=45});created.EnsureSuccessStatusCode();
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var contest=(await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("cookingContests")[0];var revision=contest.GetProperty("revision").GetString();
        (await Post(second,$"cooking/contests/{id}/respond",new{revision,action="decline"})).EnsureSuccessStatusCode();
        contest=(await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("cookingContests")[0];revision=contest.GetProperty("revision").GetString();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"cooking/contests/{id}/respond",new{revision,action="accept"})).StatusCode);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Set<CookingContest>().Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"pending").SetProperty(x=>x.StartUtc,DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"cooking/contests/{id}/respond",new{revision,action="accept"})).StatusCode);
    }
}
