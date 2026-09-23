using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.Services.AddProblemDetails();
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
    if (string.IsNullOrWhiteSpace(builder.Configuration["Email:Host"]) ||
        string.IsNullOrWhiteSpace(builder.Configuration["Email:From"]))
        throw new InvalidOperationException("Configure Email:Host and Email:From before enabling accounts.");
    if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Email:LocalCapture"))
        throw new InvalidOperationException("Local email capture cannot be enabled in production.");

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
    var keys = builder.Configuration["Auth:KeyDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, ".local", "keys");
    var protection = builder.Services.AddDataProtection().SetApplicationName("Overlap")
        .PersistKeysToFileSystem(new DirectoryInfo(keys));
    if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
    builder.Services.AddSingleton<DummyPasswordCheck>();
    builder.Services.AddScoped<AccountMail>();
    builder.Services.AddHostedService<AccountMailDispatcher>();
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;
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
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
if (authConfigured)
{
    app.UseRateLimiter();
    app.UseAuthentication();
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
    app.MapTogether();
}
else
{
    app.Map("/api/auth/{**path}", () => Results.Problem(statusCode: 503, title: "Account storage is not configured yet."));
}
app.Run();

public partial class Program;
