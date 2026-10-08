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
    public async Task CookingAlbumKeepsRecipeButRevokesLiveAccessOnDisconnect()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();using var outsider=factory.Client();
        var owner=await SignIn(factory,first,"memory-first");var partner=await SignIn(factory,second,"memory-second");await SignIn(factory,outsider,"memory-outsider");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var pair=(await db.PairMembers.SingleAsync(x=>x.AccountId==owner)).PairId;
        var recipe=new Recipe(12,34,"Feijoada",["Beans"],["Simmer"],"https://en.wikibooks.org/w/index.php?oldid=34",null);
        var contest=new CookingContest{PairId=pair,ProposedBy=owner,StartUtc=DateTime.UtcNow.AddMinutes(-20),TimeZone="UTC",Minutes=60,Status="accepted",RecipeContent=JsonSerializer.Serialize(recipe),RevealedUtc=DateTime.UtcNow.AddMinutes(-19)};
        db.Add(contest);await db.SaveChangesAsync();var id=contest.Id;
        async Task<JsonElement> Detail(HttpClient client)=>await client.GetFromJsonAsync<JsonElement>($"/api/auth/cooking/memories/{id}");
        var initial=await Detail(first);var originalRevision=initial.GetProperty("revision").GetString();
        var pixels=Convert.ToBase64String(new byte[512*512*3]);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(outsider,$"cooking/memories/{id}/photo",new{revision=originalRevision,pixels})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,$"cooking/memories/{id}/photo",new{revision=originalRevision,pixels="bad"})).StatusCode);
        (await Post(first,$"cooking/memories/{id}/photo",new{revision=originalRevision,pixels})).EnsureSuccessStatusCode();
        var state=await Detail(second);Assert.False(state.GetProperty("completed").GetBoolean());Assert.Single(state.GetProperty("photos").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"cooking/memories/{id}/photo",new{revision=originalRevision,pixels})).StatusCode);
        (await Post(second,$"cooking/memories/{id}/photo",new{revision=state.GetProperty("revision").GetString(),pixels})).EnsureSuccessStatusCode();
        state=await Detail(first);Assert.True(state.GetProperty("completed").GetBoolean());Assert.Equal("Feijoada",state.GetProperty("recipe").GetProperty("title").GetString());
        var photo=await second.GetAsync($"/api/auth/cooking/memories/{id}/photos/{owner}");Assert.Equal("image/png",photo.Content.Headers.ContentType!.MediaType);
        var png=await photo.Content.ReadAsByteArrayAsync();Assert.Equal(512,System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4)));
        Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync($"/api/auth/cooking/memories/{id}/photos/{owner}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"cooking/contests/{id}/redraw",new{revision=state.GetProperty("revision").GetString(),action="request"})).StatusCode);
        var priorPhoto=state.GetProperty("photos").EnumerateArray().Single(x=>x.GetProperty("mine").GetBoolean()).GetProperty("revision").GetString();
        (await Post(first,$"cooking/memories/{id}/photo",new{revision=state.GetProperty("revision").GetString(),pixels})).EnsureSuccessStatusCode();
        state=await Detail(first);Assert.NotEqual(priorPhoto,state.GetProperty("photos").EnumerateArray().Single(x=>x.GetProperty("mine").GetBoolean()).GetProperty("revision").GetString());
        (await Post(first,$"cooking/memories/{id}/photo",new{revision=state.GetProperty("revision").GetString(),pixels=(string?)null})).EnsureSuccessStatusCode();
        state=await Detail(second);Assert.False(state.GetProperty("completed").GetBoolean());Assert.True(Assert.Single(state.GetProperty("photos").EnumerateArray()).GetProperty("mine").GetBoolean());
        var album=await first.GetFromJsonAsync<JsonElement>("/api/auth/cooking/memories");Assert.Single(album.EnumerateArray());Assert.Equal(0,(await outsider.GetFromJsonAsync<JsonElement>("/api/auth/cooking/memories")).GetArrayLength());
        (await Post(first,"together/leave",new {acceptTerms=true})).EnsureSuccessStatusCode();
        Assert.Single(await db.Set<CookingMemory>().AsNoTracking().ToArrayAsync());Assert.Single(await db.Set<CookingDishPhoto>().AsNoTracking().ToArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/auth/cooking/memories/{id}/photos/{partner}")).StatusCode);
        (await Post(outsider,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        Assert.Empty((await first.GetFromJsonAsync<JsonElement>("/api/auth/cooking/memories")).EnumerateArray());
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/memories")).EnumerateArray());

    }
}
