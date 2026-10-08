using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Overlap.Api.Tests;
public sealed partial class TogetherTests
{
    [Fact]
    public async Task SharingPollingAfterConnectingDoesNotConsumeSecurityActionLimit()
    {
        using var factory=new AuthFactory();using var first=factory.Client();using var second=factory.Client();
        await SignIn(factory,first,"poll-first");await SignIn(factory,second,"poll-second");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        for(var i=0;i<32;i++) {
            Assert.Equal(HttpStatusCode.OK,(await first.GetAsync("/api/auth/privacy")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await second.GetAsync("/api/auth/together/")).StatusCode);
        }
        (await Post(first,"privacy/request",new{category="presence"})).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task OnlyOwnerControlsSharingAndAccessChangesImmediately()
    {
        using var factory = new AuthFactory(unlimitedAuthRequests: true);
        using var first = factory.Client(); using var second = factory.Client(); using var stranger = factory.Client();
        var id = await SignIn(factory, first, "first");
        await SignIn(factory, second, "second"); await SignIn(factory, stranger, "stranger");
        Assert.Equal(HttpStatusCode.Conflict, (await Post(first, "privacy/request", new { category = "profile" })).StatusCode);
        (await Post(first, "together/photo", new { pixels = Convert.ToBase64String(new byte[128 * 128 * 3]) })).EnsureSuccessStatusCode();
        (await Post(second, "together/accept", new { token = await Invite(first) })).EnsureSuccessStatusCode();
        async Task<JsonElement> Partner() => (await second.GetFromJsonAsync<JsonElement>("/api/auth/together/")).GetProperty("partner");
        Assert.Equal("first", (await Partner()).GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/auth/together/photo/{id}")).StatusCode);
        (await Post(first, "privacy/request", new { category = "profile" })).EnsureSuccessStatusCode();
        (await Post(first, "privacy/request", new { category = "profile" })).EnsureSuccessStatusCode();
        Assert.Equal("first", (await Partner()).GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync($"/api/auth/together/photo/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/auth/together/photo/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await Post(second, "privacy/respond", new { category = "profile", status = "declined" })).StatusCode);
        (await Post(second, "privacy/cancel", new { category = "profile" })).EnsureSuccessStatusCode();
        Assert.Equal("first", (await Partner()).GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, (await Partner()).GetProperty("online").ValueKind);
        (await Post(first, "privacy/request", new { category = "presence" })).EnsureSuccessStatusCode();
        (await first.GetAsync("/api/auth/together/")).EnsureSuccessStatusCode();
        Assert.True((await Partner()).GetProperty("online").GetBoolean());
        (await Post(first, "privacy/cancel", new { category = "profile" })).EnsureSuccessStatusCode();
        (await Post(first, "privacy/cancel", new { category = "presence" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/auth/together/photo/{id}")).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Partner()).GetProperty("online").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, "privacy/request", new { category = "places" })).StatusCode);
        (await Post(first, "privacy/request", new { category = "profile" })).EnsureSuccessStatusCode();
        (await Post(second, "together/leave", new {acceptTerms=true})).EnsureSuccessStatusCode();
        (await Post(second, "together/accept", new { token = await Invite(first) })).EnsureSuccessStatusCode();
        Assert.Equal("first", (await Partner()).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task SharingChangesRequireAuthenticationAndCsrf()
    {
        using var factory = new AuthFactory(unlimitedAuthRequests: true); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/privacy")).StatusCode);
        await SignIn(factory, client, "first");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/privacy/request", new { category = "profile" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/privacy/cancel", new { category = "profile" })).StatusCode);
    }
}
