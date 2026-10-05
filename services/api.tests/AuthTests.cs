using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Overlap.Api.Auth;
using Xunit;

namespace Overlap.Api.Tests;

public sealed class AuthFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly bool unlimitedAuthRequests;
    public AuthFactory(bool unlimitedAuthRequests = false) { this.unlimitedAuthRequests = unlimitedAuthRequests; connection.Open(); }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        foreach (var setting in new Dictionary<string, string>
        {
            ["ConnectionStrings:Accounts"] = "Server=127.0.0.1;Database=tests;User=test",
            ["Auth:PublicAppUrl"] = "http://localhost",
            ["Email:RegistrationNotificationTo"] = "", ["Email:Host"] = "127.0.0.1", ["Email:From"] = "accounts@overlap.test",
            ["Email:Port"] = "1025", ["Email:LocalCapture"] = "true"
        }) builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Email:RegistrationNotificationTo"] = "" }));
        builder.ConfigureServices(services =>
        {
            // Consent scenarios compress many user interactions into one test-server IP.
            if (unlimitedAuthRequests)
            {
                services.RemoveAll<IConfigureOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>();
                services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options => {
                    foreach(var policy in new[] { "auth", "email", "picks", "watch", "quiz" })
                        options.AddPolicy(policy, _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("consent-test"));
                });
            }
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));
            var dispatcher = services.Single(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(AccountMailDispatcher));
            services.Remove(dispatcher);
            var reminderWorker=services.Single(x=>x.ServiceType==typeof(IHostedService) && x.ImplementationType==typeof(Overlap.Api.Notifications.PhoneReminderWorker));
            services.Remove(reminderWorker);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }
    public HttpClient Client()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
        return client;
    }
    public async Task<List<AccountEmail>> Emails()
    {
        using var scope = Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountMail.ProtectionPurpose);
        var pending = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().PendingEmails.OrderBy(x => x.Id).ToListAsync();
        return pending.Select(x => JsonSerializer.Deserialize<AccountEmail>(protector.Unprotect(x.ProtectedContent))!).ToList();
    }
    public async Task<string> LinkToken(string action)
    {
        var email = (await Emails()).Last(x => x.Body.Contains($"/#{action}?"));
        var url = email.Body.Split('\n').Single(x => x.StartsWith("http://"));
        return QueryHelpers.ParseQuery(new Uri(url).Fragment.Split('?', 2)[1])["token"].ToString();
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) connection.Dispose(); }
}

public sealed class AuthTests
{
    private const string Email = "person@example.test";
    private const string Password = "abcdefghij";
    private const string NewPassword = "klmnopqrst";
    private static async Task<HttpResponseMessage> Post(HttpClient client, string action, object body)
    {
        var csrf = await client.GetAsync("/api/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var cookie = csrf.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN="));
        var token = Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{action}") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistrationNotifiesOwnerOnceOnlyWhenConfigured(bool enabled)
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        factory.Services.GetRequiredService<IConfiguration>()["Email:RegistrationNotificationTo"] = enabled ? "owner@example.test" : "";
        (await Post(client, "register", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        (await Post(client, "register", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        var messages = await factory.Emails();
        Assert.Single(messages, x => x.Recipient == Email);
        var notices = messages.Where(x => x.Subject == "New Overlap registration").ToList();
        Assert.Equal(enabled ? 1 : 0, notices.Count);
        if (enabled)
        {
            Assert.Equal("owner@example.test", notices[0].Recipient);
            Assert.Contains(Email, notices[0].Body);
            Assert.DoesNotContain(Password, notices[0].Body);
            Assert.DoesNotContain("token=", notices[0].Body);
        }
    }

    private static async Task RegisterAndConfirm(AuthFactory factory, HttpClient client)
    {
        (await Post(client, "register", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        var token = await factory.LinkToken("confirm-email");
        (await Post(client, "confirm-email", new { email = Email, token })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task EmailChangeRequiresPasswordAndVerificationThenInvalidatesSessions()
    {
        using var factory = new AuthFactory(); using var client = factory.Client(); using var session = factory.Client();
        await RegisterAndConfirm(factory, client);
        (await Post(client, "login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        (await Post(session, "login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLinkEmailUtc, DateTime.UtcNow.AddHours(-1)));
        const string next = "new@example.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "change-email", new { newEmail = next, currentPassword = "wrong" })).StatusCode);
        (await Post(client, "change-email", new { newEmail = next, currentPassword = Password })).EnsureSuccessStatusCode();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(Email, me.GetProperty("email").GetString());
        var queued = (await factory.Emails()).Single(x => x.Subject == "Confirm your new Overlap email");
        Assert.Equal(next, queued.Recipient);
        var token = await factory.LinkToken("confirm-email-change");
        (await Post(client, "confirm-email-change", new { email = Email, newEmail = next, token })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(session, "login", new { email = Email, password = Password })).StatusCode);
        (await Post(session, "login", new { email = next, password = Password })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "confirm-email-change", new { email = Email, newEmail = next, token })).StatusCode);
        Assert.Single(await factory.Emails(), x => x.Subject == "Your Overlap email was changed" && x.Recipient == Email);
    }

    [Fact]
    public async Task EmailChangeRejectsTamperedDestinationAndExpiredToken()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        await RegisterAndConfirm(factory, client);
        (await Post(client, "login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLinkEmailUtc, DateTime.UtcNow.AddHours(-1)));
        (await Post(client, "change-email", new { newEmail = "new@example.test", currentPassword = Password })).EnsureSuccessStatusCode();
        var token = await factory.LinkToken("confirm-email-change");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "confirm-email-change", new { email = Email, newEmail = "other@example.test", token })).StatusCode);
        factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan = TimeSpan.FromSeconds(-1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "confirm-email-change", new { email = Email, newEmail = "new@example.test", token })).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(Email, me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task SessionExpiresAfterThirtyMinutesAndActivityRenewsIt()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        await RegisterAndConfirm(factory, client);
        var login = await Post(client, "login", new { email = Email, password = Password });
        login.EnsureSuccessStatusCode();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        Assert.Equal(TimeSpan.FromMinutes(30), options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);
        var read = await client.GetAsync("/api/auth/me"); read.EnsureSuccessStatusCode();
        Assert.False(read.Headers.TryGetValues("Set-Cookie", out var readCookies) && readCookies.Any(x => x.StartsWith("Overlap.Session=")));
        var renewed = await Post(client, "activity", new { }); renewed.EnsureSuccessStatusCode();
        var cookie = renewed.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("Overlap.Session="));
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookie.Split(';')[0].Split('=', 2)[1]))!;
        Assert.Equal(TimeSpan.FromMinutes(30), ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        // Old eight-hour cookies must obey the new inactivity policy too.
        ticket.Properties.IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-31);
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(7);
        using var expired = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        expired.DefaultRequestHeaders.Add("Cookie", "Overlap.Session=" + options.TicketDataFormat.Protect(ticket));
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task DisplayNameIsSavedForTheSignedInAccount()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "profile", new { displayName = "Name" })).StatusCode);
        await RegisterAndConfirm(factory, client);
        (await Post(client, "login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "profile", new { displayName = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "profile", new { displayName = new string('a', 51) })).StatusCode);
        (await Post(client, "profile", new { displayName = "  My nickname  " })).EnsureSuccessStatusCode();
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("My nickname", profile.GetProperty("displayName").GetString());
        (await Post(client, "profile", new { displayName = "New nickname" })).EnsureSuccessStatusCode();
        profile = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("New nickname", profile.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task RegistrationRejectsNineCharacterPassword()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(client, "register", new { email = Email, password = "abcdefghi" })).StatusCode);
        Assert.Empty(await factory.Emails());
    }

    [Fact]
    public async Task RegistrationRequiresConfirmationAndDoesNotStorePlainPassword()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        (await Post(client, "register", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new { email = Email, password = Password })).StatusCode);
        var token = await factory.LinkToken("confirm-email");
        (await Post(client, "confirm-email", new { email = Email, token })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "confirm-email", new { email = Email, token })).StatusCode);
        var login = await Post(client, "login", new { email = Email, password = Password });
        login.EnsureSuccessStatusCode();
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith("Overlap.Session=") && x.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(2, (await factory.Emails()).Count);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<Account>>().FindByEmailAsync(Email);
        Assert.NotNull(user!.PasswordHash); Assert.NotEqual(Password, user.PasswordHash);
        Assert.DoesNotContain(await factory.Emails(), x => x.Body.Contains(Password));
    }

    [Fact]
    public async Task PasswordChangeInvalidatesOtherSessionsAndOldPassword()
    {
        using var factory = new AuthFactory(); using var first = factory.Client(); using var second = factory.Client();
        await RegisterAndConfirm(factory, first);
        (await Post(first, "login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        (await Post(second, "login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        (await Post(first, "change-password", new { currentPassword = Password, newPassword = NewPassword })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(second, "login", new { email = Email, password = Password })).StatusCode);
        (await Post(second, "login", new { email = Email, password = NewPassword })).EnsureSuccessStatusCode();
        Assert.Single(await factory.Emails(), x => x.Subject == "Your Overlap password was changed");
    }

    [Fact]
    public async Task RecoveryIsGenericThrottledAndTokenIsSingleUse()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        await RegisterAndConfirm(factory, client);
        using var session = factory.Client();
        (await Post(session, "login", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.ExecuteUpdateAsync(x => x.SetProperty(u => u.LastLinkEmailUtc, DateTime.UtcNow.AddHours(-1)));
        var known = await Post(client, "forgot-password", new { email = Email });
        var unknown = await Post(client, "forgot-password", new { email = "absent@example.test" });
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        (await Post(client, "forgot-password", new { email = Email })).EnsureSuccessStatusCode();
        Assert.Single(await factory.Emails(), x => x.Subject == "Reset your Overlap password");
        var token = await factory.LinkToken("reset-password");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "reset-password", new { email = Email, token = "tampered", password = NewPassword })).StatusCode);
        (await Post(client, "reset-password", new { email = Email, token, password = NewPassword })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "reset-password", new { email = Email, token, password = Password })).StatusCode);
        (await Post(client, "login", new { email = Email, password = NewPassword })).EnsureSuccessStatusCode();
        Assert.Single(await factory.Emails(), x => x.Subject == "Your Overlap password was reset");
    }

    [Fact]
    public async Task MissingCsrfAndAnonymousAccountAccessAreRejected()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email = Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Empty(await factory.Emails());
    }

    [Fact]
    public async Task LockoutBlocksCorrectPasswordAfterFiveFailures()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        await RegisterAndConfirm(factory, client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new { email = Email, password = "wrong password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new { email = Email, password = Password })).StatusCode);
        Assert.Equal(2, (await factory.Emails()).Count);
    }

    [Fact]
    public async Task ExpiredConfirmationTokenIsRejected()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        (await Post(client, "register", new { email = Email, password = Password })).EnsureSuccessStatusCode();
        var token = await factory.LinkToken("confirm-email");
        factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan = TimeSpan.FromSeconds(-1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "confirm-email", new { email = Email, token })).StatusCode);
        Assert.Single(await factory.Emails());
    }

    [Fact]
    public async Task ShortPasswordsAreRejectedAndRepeatedRegistrationsDoNotSendMail()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "register", new { email = Email, password = "too short" })).StatusCode);
        var first = await Post(client, "register", new { email = Email, password = Password });
        var duplicate = await Post(client, "register", new { email = Email, password = NewPassword });
        Assert.Equal(await first.Content.ReadAsStringAsync(), await duplicate.Content.ReadAsStringAsync());
        Assert.Single(await factory.Emails());
    }

    [Fact]
    public async Task ExcessiveRequestsAreRateLimited()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        for (var i = 0; i < 30; i++) (await client.GetAsync("/api/auth/csrf")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/auth/csrf")).StatusCode);
    }
}
