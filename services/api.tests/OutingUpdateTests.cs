using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Outings;
using Xunit;
namespace Overlap.Api.Tests;
public sealed partial class TogetherTests
{
    [Fact]
    public async Task EitherParticipantCanCancelWithAnOptionalMessageAndNotifyTheOther()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);
        using var first=factory.Client();using var second=factory.Client();using var stranger=factory.Client();
        await SignIn(factory,first,"cancel-first");await SignIn(factory,second,"cancel-second");await SignIn(factory,stranger,"cancel-stranger");
        (await Post(second,"together/accept",new {token=await Invite(first)})).EnsureSuccessStatusCode();
        var token=factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Overlap.OutingVenue.v1").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(new SharedVenue("Cafe","Public address","https://example.test",null,"Coffee",DateTimeOffset.UtcNow.AddHours(2),"Europe/Paris")),TimeSpan.FromHours(1));
        foreach(var ownerCancels in new[]{true,false})
        {
            (await Post(first,"outings/update",new {kind="venue",venueToken=token})).EnsureSuccessStatusCode();
            var state=await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
            var revision=state.GetProperty("outingUpdates")[0].GetProperty("revision").GetString();
            Assert.Equal(HttpStatusCode.Conflict,(await Post(stranger,"outings/cancel",new {proposalRevision=revision,reason="no"})).StatusCode);
            var actor=ownerCancels?first:second;
            var recipient=ownerCancels?second:first;
            Assert.Equal(HttpStatusCode.BadRequest,(await Post(actor,"outings/cancel",new {proposalRevision=revision,reason=new string('x',501)})).StatusCode);
            (await Post(actor,"outings/cancel",new {proposalRevision=revision,reason=ownerCancels?"  Something came up  ":null})).EnsureSuccessStatusCode();
            state=await recipient.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
            var update=state.GetProperty("outingUpdates")[0];
            Assert.True(update.GetProperty("cancelled").GetBoolean());
            Assert.False(update.GetProperty("cancelledByMe").GetBoolean());
            Assert.Equal(ownerCancels?"Something came up":null,update.GetProperty("cancellationReason").GetString());
            Assert.Equal(1,state.GetProperty("unreadOutings").GetInt32());
            Assert.Equal(0,(await actor.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("unreadOutings").GetInt32());
            Assert.Equal(HttpStatusCode.Conflict,(await Post(second,"outings/reply",new {proposalRevision=update.GetProperty("revision").GetString(),kind="surprise"})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await Post(actor,"outings/cancel",new {proposalRevision=revision})).StatusCode);
            (await Post(recipient,"privacy/read",new {kind=ownerCancels?"outing":"outing-reply",revision=update.GetProperty("revision").GetString()})).EnsureSuccessStatusCode();
            Assert.Equal(0,(await recipient.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("unreadOutings").GetInt32());
        }
    }
    [Fact]
    public async Task OutingRepliesStayAttachedNotifyTheOwnerAndRequireMatchingTime()
    {
        using var factory = new AuthFactory(unlimitedAuthRequests:true);
        using var first=factory.Client(); using var second=factory.Client(); using var stranger=factory.Client();
        await SignIn(factory,first,"reply-first");await SignIn(factory,second,"reply-second");await SignIn(factory,stranger,"reply-stranger");
        (await Post(second,"together/accept",new {token=await Invite(first)})).EnsureSuccessStatusCode();
        var protector=factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Overlap.OutingVenue.v1").ToTimeLimitedDataProtector();
        var start=DateTimeOffset.UtcNow.AddHours(2);
        string Token(string name,DateTimeOffset time)=>protector.Protect(JsonSerializer.Serialize(new SharedVenue(name,"Public address","https://www.openstreetmap.org",null,"Coffee",time,"Europe/Paris")),TimeSpan.FromHours(1));
        (await Post(first,"outings/update",new {kind="venue",venueToken=Token("First cafe",start)})).EnsureSuccessStatusCode();
        var state=await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
        var revision=state.GetProperty("outingUpdates")[0].GetProperty("revision").GetString();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,"outings/reply",new {proposalRevision=revision,kind="surprise"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(stranger,"outings/reply",new {proposalRevision=revision,kind="surprise"})).StatusCode);
        (await Post(second,"outings/reply",new {proposalRevision=revision,kind="surprise"})).EnsureSuccessStatusCode();
        state=await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
        Assert.Equal(1,state.GetProperty("unreadOutings").GetInt32());
        var update=state.GetProperty("outingUpdates")[0];
        Assert.Equal("surprise",update.GetProperty("replyKind").GetString());
        Assert.Equal(JsonValueKind.Null,update.GetProperty("replyVenue").ValueKind);
        var replyRevision=update.GetProperty("replyRevision").GetString();
        (await Post(first,"privacy/read",new {kind="outing-reply",revision=replyRevision})).EnsureSuccessStatusCode();
        Assert.Equal(0,(await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("unreadOutings").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(second,"outings/reply",new {proposalRevision=revision,kind="picked",venueToken=Token("Other cafe",start.AddHours(1))})).StatusCode);
        (await Post(second,"outings/reply",new {proposalRevision=revision,kind="picked",venueToken=Token("Other cafe",start)})).EnsureSuccessStatusCode();
        state=await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
        Assert.Equal(1,state.GetProperty("unreadOutings").GetInt32());
        update=state.GetProperty("outingUpdates")[0];
        Assert.Equal("First cafe",update.GetProperty("venue").GetProperty("name").GetString());
        Assert.Equal("Other cafe",update.GetProperty("replyVenue").GetProperty("name").GetString());
        (await Post(first,"privacy/read",new {kind="outing-reply",revision=replyRevision})).EnsureSuccessStatusCode();
        Assert.Equal(1,(await first.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("unreadOutings").GetInt32());
        (await Post(first,"outings/update",new {kind="venue",venueToken=Token("New cafe",start)})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,"outings/reply",new {proposalRevision=revision,kind="surprise"})).StatusCode);
    }
    [Fact]
    public async Task OutingUpdatesRequireExplicitAuthenticatedSharingAndStayWithinTheConnection()
    {
        using var factory = new AuthFactory(unlimitedAuthRequests:true);
        using var first=factory.Client(); using var second=factory.Client(); using var stranger=factory.Client();
        await SignIn(factory,first,"first");await SignIn(factory,second,"second");await SignIn(factory,stranger,"stranger");
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,"outings/update",new {kind="on-my-way"})).StatusCode);
        (await Post(second,"together/accept",new {token=await Invite(first)})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,(await first.PostAsJsonAsync("/api/auth/outings/update",new {kind="on-my-way"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"outings/update",new {kind="venue",venueToken="tampered"})).StatusCode);
        var token=factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Overlap.OutingVenue.v1").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(new SharedVenue("Cafe","Public venue address","https://www.openstreetmap.org","https://example.test","Lunch",DateTimeOffset.Parse("2026-10-01T11:00:00Z"),"Europe/Paris")),TimeSpan.FromHours(1));
        (await Post(first,"outings/update",new {kind="venue",venueToken=token})).EnsureSuccessStatusCode();
        var partner=await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
        Assert.Equal("Cafe",partner.GetProperty("outingUpdates")[0].GetProperty("venue").GetProperty("name").GetString());
        Assert.Equal(1,partner.GetProperty("unreadOutings").GetInt32());
        Assert.Equal("Lunch",partner.GetProperty("outingUpdates")[0].GetProperty("venue").GetProperty("activity").GetString());
        var revision=partner.GetProperty("outingUpdates")[0].GetProperty("revision").GetString();
        (await Post(stranger,"privacy/read",new {kind="outing",revision})).EnsureSuccessStatusCode();
        Assert.Equal(1,(await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("unreadOutings").GetInt32());
        (await Post(second,"privacy/read",new {kind="outing",revision})).EnsureSuccessStatusCode();
        Assert.Equal(0,(await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("unreadOutings").GetInt32());
        Assert.Empty((await stranger.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("outingUpdates").EnumerateArray());
        (await Post(second,"outings/update",new {kind="clear"})).EnsureSuccessStatusCode();
        Assert.Single((await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("outingUpdates").EnumerateArray());
        (await Post(first,"outings/update",new {kind="on-my-way"})).EnsureSuccessStatusCode();
        partner=await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy");
        Assert.True(partner.GetProperty("outingUpdates")[0].GetProperty("onMyWay").GetBoolean());
        (await Post(second,"privacy/read",new {kind="outing",revision})).EnsureSuccessStatusCode();
        Assert.Equal(1,(await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("unreadOutings").GetInt32());
        Assert.Equal(JsonValueKind.Null,partner.GetProperty("outingUpdates")[0].GetProperty("venue").ValueKind);
        (await Post(first,"outings/update",new {kind="clear"})).EnsureSuccessStatusCode();
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("outingUpdates").EnumerateArray());
        (await Post(first,"outings/update",new {kind="on-my-way"})).EnsureSuccessStatusCode();
        (await Post(second,"together/leave",new {})).EnsureSuccessStatusCode();
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("outingUpdates").EnumerateArray());
    }
}
