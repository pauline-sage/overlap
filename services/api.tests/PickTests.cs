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
    [Fact]
    public async Task PicksAreSharedOnlyWithinPairAndOnlyPartnerCanDecide()
    {
        using var factory = new AuthFactory(); using var first = factory.Client(); using var second = factory.Client(); using var stranger = factory.Client();
        await SignIn(factory, first, "first"); await SignIn(factory, second, "second"); await SignIn(factory, stranger, "stranger");
        Assert.Equal(HttpStatusCode.Conflict, (await Post(first, "watch/picks", PickTitle())).StatusCode);
        (await Post(second, "together/accept", new { token = await Invite(first) })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, "watch/picks", PickTitle(url: "https://evil.test/a"))).StatusCode);
        (await Post(first, "watch/picks", PickTitle())).EnsureSuccessStatusCode();
        (await Post(first, "watch/picks", PickTitle())).EnsureSuccessStatusCode();
        var list = await second.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks");
        var picks = list.GetProperty("picks"); Assert.Equal(1, picks.GetArrayLength());
        Assert.False(picks[0].GetProperty("mine").GetBoolean());
        var id = picks[0].GetProperty("id").GetInt64();
        Assert.Empty((await stranger.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks")).GetProperty("picks").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(first, $"watch/picks/{id}/decision", new { status = "accepted" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(stranger, $"watch/picks/{id}/decision", new { status = "accepted" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(second, $"watch/picks/{id}/remove", new {})).StatusCode);
        (await Post(second, $"watch/picks/{id}/decision", new { status = "accepted" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(second, $"watch/picks/{id}/decision", new { status = "declined" })).StatusCode);
        list = await first.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks");
        Assert.Equal("accepted", list.GetProperty("picks")[0].GetProperty("status").GetString());
        (await Post(first, "watch/picks", PickTitle("movie-2"))).EnsureSuccessStatusCode();
        list = await second.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks");
        var other = list.GetProperty("picks")[0].GetProperty("id").GetInt64();
        (await Post(second, $"watch/picks/{other}/decision", new { status = "declined" })).EnsureSuccessStatusCode();
        list = await first.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks");
        Assert.Equal("declined", list.GetProperty("picks")[0].GetProperty("status").GetString());
        (await Post(first, "together/leave", new {})).EnsureSuccessStatusCode();
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks")).GetProperty("picks").EnumerateArray());
    }
    [Fact]
    public async Task PicksRequireAuthenticationAndCsrf()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/watch/picks")).StatusCode);
        await SignIn(factory, client, "first");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/watch/picks", PickTitle())).StatusCode);
    }
}
