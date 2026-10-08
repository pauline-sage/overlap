using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Overlap.Api.Tests;
public sealed partial class TogetherTests
{
    private static object PickTitle(string id = "movie-1", string url = "https://www.themoviedb.org/movie/1") => new {
        id, title = "A film", format = "Movie", description = "Summary", minutes = 90,
        ownCountry = "FR", partnerCountry = "US", ownLink = url, partnerLink = url,
        genres = new[] { "Drama" }, ownServices = new[] { "Netflix" }, partnerServices = new[] { "Prime Video" }, poster = (string?)null
    };
    private static async Task<JsonElement> Picks(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks")).GetProperty("picks");
    private static async Task EnableSharing(HttpClient sender, string category)
    {
        (await Post(sender,"privacy/request",new {category})).EnsureSuccessStatusCode();

    }
    [Fact]
    public async Task PicksStayPrivateUntilOwnerEnablesSharingAndExplicitlySharesItem()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();using var stranger=factory.Client();
        await SignIn(factory,first,"first");await SignIn(factory,second,"second");await SignIn(factory,stranger,"stranger");
        (await Post(first,"watch/picks",PickTitle())).EnsureSuccessStatusCode();
        (await Post(first,"watch/picks",PickTitle())).EnsureSuccessStatusCode();
        var own=await Picks(first);Assert.Equal(1,own.GetArrayLength());var id=own[0].GetProperty("id").GetInt64();
        Assert.False(own[0].GetProperty("shared").GetBoolean());
        (await Post(second,"together/accept",new {token=await Invite(first)})).EnsureSuccessStatusCode();
        Assert.Equal(0,(await Picks(second)).GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"watch/picks/{id}/share",new {shared=true})).StatusCode);
        await EnableSharing(first,"picks");
        Assert.Equal(0,(await Picks(second)).GetArrayLength());
        (await Post(first,$"watch/picks/{id}/share",new {shared=true})).EnsureSuccessStatusCode();
        Assert.Equal(1,(await Picks(second)).GetArrayLength());Assert.Equal(0,(await Picks(stranger)).GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"watch/picks/{id}/decision",new {status="accepted"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(stranger,$"watch/picks/{id}/decision",new {status="accepted"})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(second,$"watch/picks/{id}/share",new {shared=false})).StatusCode);
        (await Post(second,$"watch/picks/{id}/decision",new {status="accepted"})).EnsureSuccessStatusCode();
        Assert.Equal("accepted",(await Picks(first))[0].GetProperty("status").GetString());
        var news=await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
        Assert.Equal(1,news.GetProperty("unreadPicks").GetInt32());
        var pickRevision=news.GetProperty("moviePicks")[0].GetProperty("revision").GetString();
        (await Post(first,"privacy/read",new {kind="pick",id,revision=pickRevision})).EnsureSuccessStatusCode();
        Assert.Equal(0,(await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("unreadPicks").GetInt32());
        (await Post(first,"privacy/cancel",new {category="picks"})).EnsureSuccessStatusCode();
        Assert.Equal(0,(await Picks(second)).GetArrayLength());
        Assert.False((await Picks(first))[0].GetProperty("shared").GetBoolean());
        await EnableSharing(first,"picks");
        Assert.Equal(0,(await Picks(second)).GetArrayLength());
        (await Post(first,$"watch/picks/{id}/share",new {shared=true})).EnsureSuccessStatusCode();
        (await Post(first,"together/leave",new {acceptTerms=true})).EnsureSuccessStatusCode();
        Assert.Equal(0,(await Picks(first)).GetArrayLength());Assert.Equal(0,(await Picks(second)).GetArrayLength());

    }
    [Fact]
    public async Task PicksRequireAuthenticationAndCsrf()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var client=factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/auth/watch/picks")).StatusCode);
        await SignIn(factory,client,"first");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/auth/watch/picks",PickTitle())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,"watch/picks",PickTitle(url:"https://evil.test/a"))).StatusCode);
    }
}
