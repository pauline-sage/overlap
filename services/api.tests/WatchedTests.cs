using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Watch;
using Xunit;

namespace Overlap.Api.Tests;

public sealed partial class TogetherTests
{
    [Fact]
    public async Task WatchedHistoryIsSharedIndependentOfPicksAndRemovedOnDisconnect()
    {
        using var factory = new AuthFactory();
        using var first = factory.Client(); using var second = factory.Client(); using var stranger = factory.Client();
        await SignIn(factory,first,"first"); await SignIn(factory,second,"second"); await SignIn(factory,stranger,"stranger");
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,"watch/watched",PickTitle())).StatusCode);
        var token = await Invite(first);
        (await Post(second,"together/accept",new {token})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"watch/watched",new {id="bad"})).StatusCode);
        (await Post(first,"watch/picks",PickTitle())).EnsureSuccessStatusCode();
        (await Post(first,"watch/watched",PickTitle())).EnsureSuccessStatusCode();
        (await Post(second,"watch/watched",PickTitle())).EnsureSuccessStatusCode();
        var own = await first.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks");
        var pickId = own.GetProperty("picks")[0].GetProperty("id").GetInt64();
        (await Post(first,$"watch/picks/{pickId}/remove",new {})).EnsureSuccessStatusCode();
        var partner = await second.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks");
        var watched = partner.GetProperty("watched");
        Assert.Equal(1,watched.GetArrayLength());
        Assert.False(watched[0].GetProperty("mine").GetBoolean());
        var entryId = watched[0].GetProperty("id").GetInt64();
        var other = await stranger.GetFromJsonAsync<JsonElement>("/api/auth/watch/picks");
        Assert.Empty(other.GetProperty("watched").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(stranger,$"watch/watched/{entryId}/remove",new {})).StatusCode);
        (await Post(second,$"watch/watched/{entryId}/remove",new {})).EnsureSuccessStatusCode();
        (await Post(second,"watch/watched",PickTitle())).EnsureSuccessStatusCode();
        (await Post(first,"together/leave",new {})).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<WatchedTitle>().ToListAsync());
    }
}
