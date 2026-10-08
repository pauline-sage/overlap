using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Drawing;
using Xunit;

namespace Overlap.Api.Tests;

public sealed class DrawingRulesTests
{
    [Fact]
    public void CategoriesAreExplicitAndShuffleStaysInsideSelection()
    {
        Assert.False(DrawingCatalog.Valid([]));Assert.False(DrawingCatalog.Valid(["unknown"]));
        Assert.False(DrawingCatalog.Valid(["animals","animals"]));
        var animals=DrawingCatalog.Categories.Single(x=>x.Id=="animals").Prompts;
        for(var i=0;i<20;i++)Assert.Contains(DrawingCatalog.Shuffle(["animals"],"A cat"),animals.Where(x=>x!="A cat"));
        Assert.DoesNotContain("snk",DrawingCatalog.DefaultCategories);Assert.DoesNotContain("anime",DrawingCatalog.DefaultCategories);
        Assert.All(DrawingCatalog.Categories.Single(x=>x.Id=="snk").Prompts,p=>Assert.EndsWith("(SNK)",p));
    }
    [Fact]
    public void TimerUsesActualExpiryAndShuffleChangesPrompt()
    {
        var now = DateTime.UtcNow;
        var round = new DrawingRound { PairId="pair", CreatedBy="owner", Prompt="An apple", Seconds=600, CreatedUtc=now };
        Assert.Equal("waiting", DrawingRules.Status(round, now));
        Assert.NotEqual(round.Prompt, DrawingRules.Shuffle(round.Prompt));
        round.StartUtc=now;round.EndUtc=now.AddMinutes(10);
        Assert.Equal("drawing", DrawingRules.Status(round, now.AddMinutes(9)));
        Assert.Equal("finished", DrawingRules.Status(round, now.AddMinutes(10)));
        round.Cancelled=true;Assert.Equal("cancelled", DrawingRules.Status(round, now.AddMinutes(10)));
        Assert.False(DrawingRules.ValidSeconds(0));Assert.False(DrawingRules.ValidSeconds(3601));Assert.True(DrawingRules.ValidSeconds(10));
    }
}

public sealed partial class TogetherTests
{
    [Fact]
    public async Task DrawingSessionsRetainSelectedCategoriesAndEnforceCountAndSingleAdvancement()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        await SignIn(factory,first,"draw-session-owner");await SignIn(factory,second,"draw-session-partner");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"drawing",new{seconds=60,categories=Array.Empty<string>(),drawingCount=2})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"drawing",new{seconds=60,categories=new[]{"animals"},drawingCount=11})).StatusCode);
        var created=await Post(first,"drawing",new{seconds=60,categories=new[]{"animals"},drawingCount=2});created.EnsureSuccessStatusCode();
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        async Task<JsonElement> State(long roundId)=>(await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray().Single(x=>x.GetProperty("id").GetInt64()==roundId);
        var round=await State(id);var revision=round.GetProperty("revision").GetString();var session=round.GetProperty("sessionId").GetString();
        Assert.Equal(2,round.GetProperty("drawingCount").GetInt32());Assert.Equal(1,round.GetProperty("drawingNumber").GetInt32());
        Assert.Contains(round.GetProperty("prompt").GetString()!,DrawingCatalog.Categories.Single(x=>x.Id=="animals").Prompts);
        (await Post(first,$"drawing/{id}/shuffle",new{revision})).EnsureSuccessStatusCode();round=await State(id);revision=round.GetProperty("revision").GetString();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"drawing/{id}/next",new{revision})).StatusCode);
        (await Post(second,$"drawing/{id}/join",new{revision})).EnsureSuccessStatusCode();round=await State(id);revision=round.GetProperty("revision").GetString();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Set<DrawingRound>().Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.EndUtc,DateTime.UtcNow.AddMinutes(-1)));
        var pixels=Convert.ToBase64String(new byte[512*512*3]);(await Post(first,$"drawing/{id}/photo",new{revision,pixels})).EnsureSuccessStatusCode();
        round=await State(id);revision=round.GetProperty("revision").GetString();
        var next=await Post(first,$"drawing/{id}/next",new{revision});next.EnsureSuccessStatusCode();
        var nextId=(await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"drawing/{id}/next",new{revision})).StatusCode);
        round=await State(id);Assert.True(round.GetProperty("nextCreated").GetBoolean());Assert.Single(round.GetProperty("photos").EnumerateArray());
        // Even refreshed revisions cannot create a duplicate next round.
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"drawing/{id}/next",new{revision=round.GetProperty("revision").GetString()})).StatusCode);
        round=await State(nextId);Assert.Equal(session,round.GetProperty("sessionId").GetString());Assert.Equal(2,round.GetProperty("drawingNumber").GetInt32());
        Assert.Equal("animals",Assert.Single(round.GetProperty("categories").EnumerateArray()).GetString());Assert.Equal("waiting",round.GetProperty("status").GetString());
        revision=round.GetProperty("revision").GetString();(await Post(second,$"drawing/{nextId}/join",new{revision})).EnsureSuccessStatusCode();
        await db.Set<DrawingRound>().Where(x=>x.Id==nextId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.EndUtc,DateTime.UtcNow.AddMinutes(-1)));
        round=await State(nextId);revision=round.GetProperty("revision").GetString();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"drawing/{nextId}/next",new{revision})).StatusCode);
        // Stopping between drawings keeps uploaded photos and prevents continuation.
        created=await Post(first,"drawing",new{seconds=60,categories=new[]{"snk"},drawingCount=2});created.EnsureSuccessStatusCode();
        id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();round=await State(id);revision=round.GetProperty("revision").GetString();
        (await Post(second,$"drawing/{id}/join",new{revision})).EnsureSuccessStatusCode();
        await db.Set<DrawingRound>().Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.EndUtc,DateTime.UtcNow.AddMinutes(-1)));
        round=await State(id);revision=round.GetProperty("revision").GetString();
        (await Post(first,$"drawing/{id}/photo",new{revision,pixels})).EnsureSuccessStatusCode();
        round=await State(id);revision=round.GetProperty("revision").GetString();
        (await Post(second,$"drawing/{id}/cancel",new{revision})).EnsureSuccessStatusCode();
        round=await State(id);Assert.Single(round.GetProperty("photos").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"drawing/{id}/next",new{revision=round.GetProperty("revision").GetString()})).StatusCode);
    }
    [Fact]
    public async Task DrawingInvitationsNotifyOnlyPartnerAndRequireTheirDecision()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);
        using var first=factory.Client();using var second=factory.Client();using var outsider=factory.Client();
        await SignIn(factory,first,"draw-invite-owner");await SignIn(factory,second,"draw-invite-partner");await SignIn(factory,outsider,"draw-invite-outsider");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        (await Post(first,"drawing",new{seconds=60})).EnsureSuccessStatusCode();
        async Task<JsonElement> State()=>Assert.Single((await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
        async Task<int> Invites(HttpClient client)=>(await client.GetFromJsonAsync<JsonElement>("/api/auth/privacy")).GetProperty("drawingInvitations").GetArrayLength();
        var round=await State();var id=round.GetProperty("id").GetInt64();var revision=round.GetProperty("revision").GetString();
        Assert.Equal(JsonValueKind.Null,round.GetProperty("startUtc").ValueKind);
        Assert.Equal(0,await Invites(first));Assert.Equal(1,await Invites(second));Assert.Equal(0,await Invites(outsider));
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"drawing/{id}/decline",new{revision})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(outsider,$"drawing/{id}/decline",new{revision})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"drawing/{id}/decline",new{revision="stale"})).StatusCode);
        (await Post(second,$"drawing/{id}/decline",new{revision})).EnsureSuccessStatusCode();
        Assert.Equal(0,await Invites(second));Assert.Equal("cancelled",(await State()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"drawing/{id}/join",new{revision})).StatusCode);
        // A declined invitation frees setup; accepting the next one starts its timer.
        var created=await Post(first,"drawing",new{seconds=30});created.EnsureSuccessStatusCode();
        var nextId=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        round=(await second.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray().Single(x=>x.GetProperty("id").GetInt64()==nextId);
        revision=round.GetProperty("revision").GetString();Assert.Equal(1,await Invites(second));
        (await Post(second,$"drawing/{nextId}/join",new{revision})).EnsureSuccessStatusCode();
        Assert.Equal(0,await Invites(second));
    }

    [Fact]
    public async Task PausedRoundsPreserveTimeAndResumeWithoutOverlappingAnotherRound()
    {
        using var factory = new AuthFactory(unlimitedAuthRequests: true);
        using var first = factory.Client(); using var second = factory.Client(); using var outsider = factory.Client();
        await SignIn(factory, first, "draw-pause-owner"); await SignIn(factory, second, "draw-pause-partner");
        await SignIn(factory, outsider, "draw-pause-outsider");
        (await Post(second, "together/accept", new { token = await Invite(first) })).EnsureSuccessStatusCode();
        var created = await Post(first, "drawing", new { seconds = 600 }); created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        async Task<JsonElement> State() => (await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing"))
            .GetProperty("rounds").EnumerateArray().Single(x => x.GetProperty("id").GetInt64() == id);
        string Revision(JsonElement value) => value.GetProperty("revision").GetString()!;
        var round = await State(); var revision = Revision(round);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(outsider, $"drawing/{id}/pause", new { revision })).StatusCode);
        (await Post(first, $"drawing/{id}/pause", new { revision })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(second, $"drawing/{id}/resume", new { revision })).StatusCode);
        round = await State(); Assert.Equal("paused", round.GetProperty("status").GetString());
        Assert.Equal(600, round.GetProperty("pausedSeconds").GetInt32());
        revision = Revision(round);
        (await Post(second, $"drawing/{id}/resume", new { revision })).EnsureSuccessStatusCode();
        round = await State(); Assert.Equal("waiting", round.GetProperty("status").GetString());
        revision = Revision(round);
        (await Post(second, $"drawing/{id}/join", new { revision })).EnsureSuccessStatusCode();
        round = await State(); revision = Revision(round);
        (await Post(second, $"drawing/{id}/pause", new { revision })).EnsureSuccessStatusCode();
        round = await State(); var savedSeconds = round.GetProperty("pausedSeconds").GetInt32();
        Assert.InRange(savedSeconds, 590, 600); revision = Revision(round);
        // Simulate returning after the original deadline; the persisted pause overrides it.
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Set<DrawingRound>().Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndUtc, DateTime.UtcNow.AddDays(-1)));
        Assert.Equal("paused", (await State()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(first, $"drawing/{id}/photo", new { revision, pixels = "bad" })).StatusCode);
        var another = await Post(first, "drawing", new { seconds = 30 }); another.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(first, $"drawing/{id}/resume", new { revision })).StatusCode);
        var other = (await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray()
            .Single(x => x.GetProperty("id").GetInt64() != id);
        (await Post(first, $"drawing/{other.GetProperty("id").GetInt64()}/cancel", new { revision = Revision(other) })).EnsureSuccessStatusCode();
        var before = DateTimeOffset.UtcNow;
        (await Post(first, $"drawing/{id}/resume", new { revision })).EnsureSuccessStatusCode();
        round = await State(); Assert.Equal("drawing", round.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, round.GetProperty("pausedSeconds").ValueKind);
        Assert.InRange((round.GetProperty("endUtc").GetDateTimeOffset() - before).TotalSeconds, savedSeconds - 1, savedSeconds + 5);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(second, $"drawing/{id}/pause", new { revision })).StatusCode);
    }

    [Fact]
    public async Task EitherMemberCanCancelButStaleRemovalsAreRejected()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        await SignIn(factory,first,"draw-cancel-owner");await SignIn(factory,second,"draw-cancel-partner");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        (await Post(first,"drawing",new{seconds=30})).EnsureSuccessStatusCode();
        var round=Assert.Single((await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
        var id=round.GetProperty("id").GetInt64();var revision=round.GetProperty("revision").GetString();
        (await Post(second,$"drawing/{id}/join",new{revision})).EnsureSuccessStatusCode();
        round=Assert.Single((await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
        Assert.Equal(TimeSpan.FromSeconds(30),round.GetProperty("endUtc").GetDateTimeOffset()-round.GetProperty("startUtc").GetDateTimeOffset());
        revision=round.GetProperty("revision").GetString();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"drawing/{id}/remove",new{revision})).StatusCode);
        (await Post(second,$"drawing/{id}/cancel",new{revision})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"drawing/{id}/remove",new{revision})).StatusCode);
        round=Assert.Single((await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
        Assert.Equal("cancelled",round.GetProperty("status").GetString());revision=round.GetProperty("revision").GetString();
        (await Post(first,$"drawing/{id}/remove",new{revision})).EnsureSuccessStatusCode();
        Assert.Empty((await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
    }
    [Fact]
    public async Task DrawingRoundsProtectTimerPhotosRatingsAndRevokeLiveAccessOnDisconnect()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);
        using var first=factory.Client();using var second=factory.Client();using var outsider=factory.Client();
        var owner=await SignIn(factory,first,"draw-owner");var partner=await SignIn(factory,second,"draw-partner");await SignIn(factory,outsider,"draw-outsider");
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,"drawing",new{seconds=600})).StatusCode);
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"drawing",new{seconds=0})).StatusCode);
        var created=await Post(first,"drawing",new{seconds=600});created.EnsureSuccessStatusCode();
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        async Task<JsonElement> State(HttpClient client)=>Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
        string Revision(JsonElement state)=>state.GetProperty("revision").GetString()!;
        var round=await State(first);var revision=Revision(round);var prompt=round.GetProperty("prompt").GetString();
        Assert.Equal("waiting",round.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"drawing/{id}/join",new{revision})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(second,$"drawing/{id}/shuffle",new{revision})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(outsider,$"drawing/{id}/join",new{revision})).StatusCode);
        (await Post(first,$"drawing/{id}/shuffle",new{revision})).EnsureSuccessStatusCode();
        round=await State(first);Assert.NotEqual(prompt,round.GetProperty("prompt").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(second,$"drawing/{id}/join",new{revision})).StatusCode);
        revision=Revision(round);(await Post(second,$"drawing/{id}/join",new{revision})).EnsureSuccessStatusCode();
        round=await State(first);Assert.Equal("drawing",round.GetProperty("status").GetString());revision=Revision(round);
        var start=round.GetProperty("startUtc").GetDateTimeOffset();var end=round.GetProperty("endUtc").GetDateTimeOffset();Assert.Equal(TimeSpan.FromMinutes(10),end-start);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,"drawing",new{seconds=300})).StatusCode);
        var pixels=Convert.ToBase64String(new byte[512*512*3]);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"drawing/{id}/photo",new{revision,pixels})).StatusCode);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Set<DrawingRound>().Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.StartUtc,DateTime.UtcNow.AddMinutes(-11)).SetProperty(x=>x.EndUtc,DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,$"drawing/{id}/photo",new{revision,pixels="bad"})).StatusCode);
        (await Post(first,$"drawing/{id}/photo",new{revision,pixels})).EnsureSuccessStatusCode();
        round=await State(second);revision=Revision(round);var photo=Assert.Single(round.GetProperty("photos").EnumerateArray());
        Assert.DoesNotContain("png",photo.GetRawText(),StringComparison.OrdinalIgnoreCase);
        var photoRevision=photo.GetProperty("revision").GetString();
        Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync($"/api/auth/drawing/{id}/photos/{owner}")).StatusCode);
        var png=await second.GetAsync($"/api/auth/drawing/{id}/photos/{owner}");Assert.Equal("image/png",png.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(second,$"drawing/{id}/rating",new{revision,photoAccount=owner,photoRevision,score=6})).StatusCode);
        (await Post(second,$"drawing/{id}/rating",new{revision,photoAccount=owner,photoRevision,score=4})).EnsureSuccessStatusCode();
        round=await State(first);revision=Revision(round);photo=Assert.Single(round.GetProperty("photos").EnumerateArray());Assert.Equal(4,photo.GetProperty("partnerRating").GetInt32());Assert.Equal(JsonValueKind.Null,photo.GetProperty("myRating").ValueKind);
        (await Post(first,$"drawing/{id}/rating",new{revision,photoAccount=owner,photoRevision,score=5})).EnsureSuccessStatusCode();
        round=await State(second);photo=Assert.Single(round.GetProperty("photos").EnumerateArray());Assert.Equal(4,photo.GetProperty("myRating").GetInt32());Assert.Equal(5,photo.GetProperty("partnerRating").GetInt32());
        revision=Revision(round);(await Post(first,$"drawing/{id}/photo",new{revision,pixels})).EnsureSuccessStatusCode();
        round=await State(second);photo=Assert.Single(round.GetProperty("photos").EnumerateArray());Assert.Equal(JsonValueKind.Null,photo.GetProperty("myRating").ValueKind);Assert.Equal(JsonValueKind.Null,photo.GetProperty("partnerRating").ValueKind);
        revision=Revision(round);(await Post(second,$"drawing/{id}/photo",new{revision,pixels})).EnsureSuccessStatusCode();
        Assert.Equal(2,(await State(first)).GetProperty("photos").GetArrayLength());
        Assert.Equal(0,(await outsider.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").GetArrayLength());
        (await Post(first,"together/leave",new {acceptTerms=true})).EnsureSuccessStatusCode();
        Assert.Single(await db.Set<DrawingRound>().AsNoTracking().ToArrayAsync());Assert.Equal(2,await db.Set<DrawingPhoto>().CountAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/auth/drawing/{id}/photos/{owner}")).StatusCode);
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
        (await Post(outsider,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync($"/api/auth/drawing/{id}/photos/{owner}")).StatusCode);
        Assert.Empty((await first.GetFromJsonAsync<JsonElement>("/api/auth/drawing")).GetProperty("rounds").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(second,$"drawing/{id}/join",new{revision})).StatusCode);

    }
}
