using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Overlap.Api.Auth;
using Xunit;

namespace Overlap.Api.Tests;

public sealed class DeploymentTests
{
    private static X509Certificate2 Certificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }

    [Fact]
    public void EncryptedDatabaseKeysSurviveNewServiceProvider()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var certificate = Certificate();
        ServiceProvider Provider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));
            services.AddDataProtection().SetApplicationName("Overlap")
                .PersistKeysToDbContext<AuthDbContext>().ProtectKeysWithCertificate(certificate);
            return services.BuildServiceProvider();
        }
        string encrypted;
        using (var first = Provider())
        {
            using var scope = first.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.Database.EnsureCreated();
            encrypted = first.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountMail.ProtectionPurpose).Protect("queued-email");
            var xml = Assert.Single(db.DataProtectionKeys).Xml!;
            Assert.Contains("encryptedSecret", xml);
            Assert.DoesNotContain("<masterKey", xml);
        }
        using var second = Provider();
        Assert.Equal("queued-email", second.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(AccountMail.ProtectionPurpose).Unprotect(encrypted));
    }

    private sealed class MailHandler(string response, HttpStatusCode code) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://api.mailjet.com/v3.1/send", request.RequestUri!.ToString());
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(code) { Content = new StringContent(response) };
        }
    }

    [Theory]
    [InlineData("success", 200, true)]
    [InlineData("error", 200, false)]
    [InlineData("error", 429, false)]
    [InlineData("error", 500, false)]
    public async Task MailjetRequiresAcceptedMessage(string status, int code, bool accepted)
    {
        using var handler = new MailHandler(JsonSerializer.Serialize(new { Messages = new[] { new { Status = status } } }), (HttpStatusCode)code);
        using var http = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Email:Transport"] = "Mailjet", ["Email:From"] = "Overlap <sender@example.test>",
            ["Email:Username"] = "test-key", ["Email:Password"] = "test-secret"
        }).Build();
        var sender = new AccountEmailSender(http, config, new TestEnvironment());
        var exception = await Record.ExceptionAsync(() => sender.SendAsync(new AccountEmail("recipient@example.test", "Subject", "Body"), 1, default));
        if (accepted) Assert.Null(exception); else Assert.NotNull(exception);
        using var json = JsonDocument.Parse(handler.Body!);
        var message = json.RootElement.GetProperty("Messages")[0];
        Assert.Equal("recipient@example.test", message.GetProperty("To")[0].GetProperty("Email").GetString());
        Assert.Equal("Body", message.GetProperty("TextPart").GetString());
    }

    [Fact]
    public async Task ProductionServesUiAndSecureCookiesBehindProxyWithoutSwallowingApi404()
    {
        using var factory = new ProductionFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://overlap.example.test"), AllowAutoRedirect = false });
        using (var scope = factory.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.9");
        Assert.Equal("<html>Overlap test</html>", await client.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/not-a-route")).StatusCode);
        var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, csrf.StatusCode);
        Assert.All(csrf.Headers.GetValues("Set-Cookie"), cookie => Assert.Contains("secure", cookie.ToLowerInvariant()));
        Assert.Contains("max-age", csrf.Headers.GetValues("Strict-Transport-Security").Single());
        client.DefaultRequestHeaders.Remove("X-Forwarded-Proto");
        var redirect = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirect.StatusCode);
        Assert.Equal("https", redirect.Headers.Location!.Scheme);
    }

    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly X509Certificate2 certificate = Certificate();
        private readonly string webRoot = Path.Combine(Path.GetTempPath(), "overlap-web-" + Guid.NewGuid());
        public ProductionFactory()
        {
            connection.Open();
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(Path.Combine(webRoot, "index.html"), "<html>Overlap test</html>");
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production").UseWebRoot(webRoot);
            foreach (var item in new Dictionary<string, string> {
                ["ConnectionStrings:Accounts"] = "Server=unused;Database=test;User=test;SslMode=VerifyFull",
                ["Auth:PublicAppUrl"] = "https://overlap.example.test", ["AllowedHosts"] = "overlap.example.test",
                ["Auth:KeyCertificateBase64"] = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx)),
                ["Email:Transport"] = "Mailjet", ["Email:From"] = "sender@example.test",
                ["Email:Username"] = "key", ["Email:Password"] = "secret",
                ["Hosting:RenderProxy"] = "true", ["RENDER"] = "true"
            }) builder.UseSetting(item.Key, item.Value);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AuthDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));
                services.Remove(services.Single(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(AccountMailDispatcher)));
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { connection.Dispose(); certificate.Dispose(); if (Directory.Exists(webRoot)) Directory.Delete(webRoot, true); }
        }
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
