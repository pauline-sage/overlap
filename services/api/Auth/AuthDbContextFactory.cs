using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Overlap.Api.Auth;

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables().Build();
        // Migration generation does not connect; applying migrations requires real credentials.
        var connection = config.GetConnectionString("AccountsMigration") ?? config.GetConnectionString("Accounts")
            ?? "Server=127.0.0.1;Database=overlap;User=overlap_app";
        return new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(connection, ServerVersion.Parse(config["Database:ServerVersion"] ?? "8.4.0-mysql")).Options);
    }
}
