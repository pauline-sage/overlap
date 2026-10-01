using Overlap.Api.Outings;
using Overlap.Api.Watch;
using Overlap.Api.Books;
using System.Threading.RateLimiting;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.HttpOverrides;
using Overlap.Api.Hosting;
using MySqlConnector;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
if (await DeploymentCommands.RunAsync(args, builder.Configuration)) return;
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OnlinePresence>();

builder.Services.AddSingleton<TmdbCache>();
builder.Services.AddSingleton<BookCatalogCache>();
builder.Services.AddHttpClient<BookCatalog>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<GeoapifyPlaces>(client => client.Timeout = TimeSpan.FromSeconds(12))
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<YouTubeSearch>(client => client.Timeout = TimeSpan.FromSeconds(12))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<TmdbCatalog>(client => client.Timeout = TimeSpan.FromSeconds(12))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var renderProxy = builder.Configuration.GetValue<bool>("Hosting:RenderProxy");
if (renderProxy)
{
    if (builder.Configuration["RENDER"] != "true")
        throw new InvalidOperationException("Render proxy mode requires the Render environment.");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Only the last hop is trusted. Never consume arbitrary leftmost client-supplied headers.
        // Render must be the only ingress; do not enable this on a directly exposed server.
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
var connection = builder.Configuration.GetConnectionString("Accounts");
var authConfigured = !string.IsNullOrWhiteSpace(connection);
if (!builder.Environment.IsDevelopment() && !authConfigured)
    throw new InvalidOperationException("ConnectionStrings:Accounts must be configured.");

if (authConfigured)
{
    var appUrl = builder.Configuration["Auth:PublicAppUrl"];
    if (!Uri.TryCreate(appUrl, UriKind.Absolute, out var uri) ||
        (!builder.Environment.IsDevelopment() && uri.Scheme != "https") ||
        (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
        throw new InvalidOperationException("Auth:PublicAppUrl must be HTTPS (HTTP loopback is allowed in Development).");
    var notificationTo = builder.Configuration["Email:RegistrationNotificationTo"];
    if (!string.IsNullOrWhiteSpace(notificationTo) &&
        (notificationTo.Length > 254 || notificationTo.Any(char.IsControl) ||
         !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(notificationTo)))
        throw new InvalidOperationException("Email:RegistrationNotificationTo must be a valid email address.");
    var transport = builder.Configuration["Email:Transport"] ?? "Smtp";
    if (transport is not ("Smtp" or "Mailjet"))
        throw new InvalidOperationException("Email:Transport must be Smtp or Mailjet.");
    if (string.IsNullOrWhiteSpace(builder.Configuration["Email:From"]) ||
        (transport == "Smtp" && string.IsNullOrWhiteSpace(builder.Configuration["Email:Host"])) ||
        (transport == "Mailjet" && (string.IsNullOrWhiteSpace(builder.Configuration["Email:Username"]) ||
            string.IsNullOrWhiteSpace(builder.Configuration["Email:Password"]))))
        throw new InvalidOperationException("Configure the sender and credentials for the selected email transport.");
    if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Email:LocalCapture"))
        throw new InvalidOperationException("Local email capture cannot be enabled in production.");

    if (!builder.Environment.IsDevelopment())
    {
        if (new MySqlConnectionStringBuilder(connection!).SslMode != MySqlSslMode.VerifyFull)
            throw new InvalidOperationException("Production database connections require SslMode=VerifyFull.");
        var allowedHosts = builder.Configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Contains('*'))
            throw new InvalidOperationException("Set AllowedHosts to the public application hostname.");
    }
    var serverVersion = builder.Configuration["Database:ServerVersion"] ?? "8.4.0-mysql";
    builder.Services.AddDbContext<AuthDbContext>(options => options.UseMySql(connection, ServerVersion.Parse(serverVersion)));
    builder.Services.AddIdentity<Account, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }).AddEntityFrameworkStores<AuthDbContext>().AddDefaultTokenProviders();
    builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
    builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
    builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.Name = "Overlap.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = async context =>
        {
            // Also invalidate previously issued eight-hour tickets after 30 minutes.
            if (context.Properties.IssuedUtc is not { } issued || DateTimeOffset.UtcNow - issued >= TimeSpan.FromMinutes(30))
            {
                context.RejectPrincipal();
                return;
            }
            var expires = context.Properties.ExpiresUtc;
            await SecurityStampValidator.ValidatePrincipalAsync(context);
            // Identity's stamp check can request renewal even with sliding expiry disabled.
            // Only the explicit activity endpoint may extend this ticket.
            context.Properties.IssuedUtc = issued;
            context.Properties.ExpiresUtc = expires;
            context.ShouldRenew = false;
        };
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    });
    builder.Services.AddAuthorization();
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-XSRF-TOKEN";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    });
    var protection = builder.Services.AddDataProtection().SetApplicationName("Overlap");
    if (builder.Environment.IsDevelopment())
    {
        var keys = builder.Configuration["Auth:KeyDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, ".local", "keys");
        protection.PersistKeysToFileSystem(new DirectoryInfo(keys));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
    }
    else
    {
        var encoded = builder.Configuration["Auth:KeyCertificateBase64"];
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("Auth:KeyCertificateBase64 is required for persistent production keys.");
        var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(encoded), null,
            X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("The key-protection certificate must include its private key.");
        builder.Services.AddSingleton(certificate);
        protection.PersistKeysToDbContext<AuthDbContext>().ProtectKeysWithCertificate(certificate);
    }
    builder.Services.AddSingleton<DummyPasswordCheck>();
    builder.Services.AddScoped<AccountMail>();
    builder.Services.AddHttpClient<AccountEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(15))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddHostedService<AccountMailDispatcher>();
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;
        options.AddPolicy("picks", context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy("watch", context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 8, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy("email", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
            RateLimitPartition.GetFixedWindowLimiter("api", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });
}

var app = builder.Build();
if (renderProxy) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
if (authConfigured)
{
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.Use(async (context, next) =>
    {
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/auth"))
        {
            try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: 400, title: "Your session changed. Reload and try again.").ExecuteAsync(context);
                return;
            }
        }
        await next();
    });
    app.MapAuth();
    app.MapAccountDeletion();
    app.MapTogether();
    app.MapPrivacy();
    app.MapWatch();
    app.MapPicks();
    app.MapWatched();
    app.MapBooks();
    app.MapOutings();
    app.MapOutingUpdates();
    app.MapOutingReplies();
    app.MapReadNotifications();
}
else
{
    app.Map("/api/auth/{**path}", () => Results.Problem(statusCode: 503, title: "Account storage is not configured yet."));
}
// Angular currently uses fragment links. Unknown API paths must remain API 404s.
app.MapFallbackToFile("/{*path:nonfile}", "index.html").Add(endpoint =>
{
    var original = endpoint.RequestDelegate!;
    endpoint.RequestDelegate = context => context.Request.Path.StartsWithSegments("/api")
        ? Results.NotFound().ExecuteAsync(context) : original(context);
});
app.Run();

public partial class Program;
