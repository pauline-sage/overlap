using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Xunit;

namespace Overlap.Api.Tests;

public sealed partial class TogetherTests
{
    private static async Task<HttpResponseMessage> Post(HttpClient client, string action, object body)
    {
        var csrf = await client.GetAsync("/api/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var cookie = csrf.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN="));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{action}") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(cookie.Split(';')[0][11..]));
        return await client.SendAsync(request);
    }
    private static async Task<string> SignIn(AuthFactory factory, HttpClient client, string name)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var account = new Account { UserName = name + "@example.test", Email = name + "@example.test", EmailConfirmed = true, DisplayName = name };
        Assert.True((await manager.CreateAsync(account, "abcdefghij")).Succeeded);
        (await Post(client, "login", new { email = account.Email, password = "abcdefghij" })).EnsureSuccessStatusCode();
        return account.Id;
    }
    private static async Task<string> Invite(HttpClient client)
    {
        var response = await Post(client, "together/invite", new { });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("url").GetString()!.Split("token=")[1];
    }
    [Fact]
    public async Task InvitationIsExplicitSingleUseAndDisconnectRemovesBothMemberships()
    {
        using var factory = new AuthFactory(); using var first = factory.Client(); using var second = factory.Client();
        await SignIn(factory, first, "first"); await SignIn(factory, second, "second");
        var token = await Invite(first);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, "together/accept", new { token })).StatusCode);
        var preview=await Post(second, "together/preview", new { token });
        preview.EnsureSuccessStatusCode();
        Assert.Equal("first",(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayName").GetString());
        var before = await second.GetFromJsonAsync<JsonElement>("/api/auth/together/");
        Assert.Equal(JsonValueKind.Null, before.GetProperty("partner").ValueKind);
        (await Post(second, "together/accept", new { token })).EnsureSuccessStatusCode();
        var after = await first.GetFromJsonAsync<JsonElement>("/api/auth/together/");
        Assert.Equal("second", after.GetProperty("partner").GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(second, "together/accept", new { token })).StatusCode);
        (await Post(second, "together/leave", new {acceptTerms=true})).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().PairMembers.ToListAsync());
    }
    [Fact]
    public async Task RevokedReplacedAndExpiredInvitationsCannotBeAccepted()
    {
        using var factory = new AuthFactory(); using var first = factory.Client(); using var second = factory.Client();
        var id = await SignIn(factory, first, "first"); await SignIn(factory, second, "second");
        var old = await Invite(first); var token = await Invite(first);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(second, "together/accept", new { token = old })).StatusCode);
        (await Post(first, "together/revoke", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(second, "together/accept", new { token })).StatusCode);
        token = await Invite(first);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.InvitationExpiresUtc, DateTime.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(second, "together/accept", new { token })).StatusCode);
    }
    [Fact]
    public async Task PhotosAreCanonicalPngAndOnlyVisibleToOwnerAndPartner()
    {
        using var factory = new AuthFactory(); using var first = factory.Client(); using var second = factory.Client(); using var stranger = factory.Client();
        var id = await SignIn(factory, first, "first"); await SignIn(factory, second, "second"); await SignIn(factory, stranger, "stranger");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, "together/photo", new { pixels = "not a photo" })).StatusCode);
        (await Post(first, "together/photo", new { pixels = Convert.ToBase64String(new byte[128 * 128 * 3]) })).EnsureSuccessStatusCode();
        var photo = await first.GetAsync($"/api/auth/together/photo/{id}"); photo.EnsureSuccessStatusCode();
        Assert.Equal("image/png", photo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, (await photo.Content.ReadAsByteArrayAsync())[..8]);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/auth/together/photo/{id}")).StatusCode);
        var token = await Invite(first);
        (await Post(second, "together/accept", new { token })).EnsureSuccessStatusCode();
        await EnableSharing(first, "profile");
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync($"/api/auth/together/photo/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/auth/together/photo/{id}")).StatusCode);
        (await Post(first, "together/photo", new { pixels = (string?)null })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/auth/together/photo/{id}")).StatusCode);
    }
    [Fact]
    public async Task ConnectedAccountCannotAcceptAnotherInvitation()
    {
        using var factory = new AuthFactory(); using var first = factory.Client(); using var second = factory.Client(); using var third = factory.Client();
        await SignIn(factory, first, "first"); await SignIn(factory, second, "second"); await SignIn(factory, third, "third");
        var token = await Invite(first);
        (await Post(second, "together/accept", new { token })).EnsureSuccessStatusCode();
        var other = await Invite(third);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(second, "together/accept", new { token = other })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var members = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().PairMembers.ToListAsync();
        Assert.Equal(2, members.Count);
        Assert.Single(members.Select(x => x.PairId).Distinct());
    }

    [Fact]
    public void AvatarEncodingPreservesPixelsWithoutMetadata()
    {
        var pixels = Enumerable.Range(0, 128 * 128 * 3).Select(x => (byte)(x % 251)).ToArray();
        var png = AvatarImage.Encode(pixels);
        var chunks = new List<string>();
        byte[] compressed = [];
        for (var offset = 8; offset < png.Length;)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            chunks.Add(type);
            if (type == "IDAT") compressed = png.AsSpan(offset + 8, length).ToArray();
            offset += length + 12;
        }
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunks);
        using var zip = new System.IO.Compression.ZLibStream(new MemoryStream(compressed), System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream(); zip.CopyTo(output);
        var raw = output.ToArray();
        Assert.Equal(128 * (128 * 3 + 1), raw.Length);
        for (var row = 0; row < 128; row++)
        {
            Assert.Equal(0, raw[row * 385]);
            Assert.Equal(pixels.AsSpan(row * 384, 384).ToArray(), raw.AsSpan(row * 385 + 1, 384).ToArray());
        }
    }

    [Fact]
    public async Task AnonymousAndMissingCsrfRequestsAreRejected()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/together/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "together/invite", new { })).StatusCode);
        await SignIn(factory, client, "first");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/together/invite", new { })).StatusCode);
    }
}
