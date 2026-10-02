using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
using MySqlConnector;

namespace Overlap.Api.Hosting;

public static class DeploymentCommands
{
    public static async Task<bool> RunAsync(string[] args, IConfiguration config)
    {
        if(args.Contains("--generate-push-config")){
            var path=Path.GetFullPath("appsettings.Push.Local.json");
            using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var parameters=key.ExportParameters(true);
            static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
            var publicKey=Encode([4,..parameters.Q.X!,..parameters.Q.Y!]);var privateKey=Encode(parameters.D!);
            using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            await System.Text.Json.JsonSerializer.SerializeAsync(output,new{Push=new{Subject="https://overlap-t7l6.onrender.com",PublicKey=publicKey,PrivateKey=privateKey,SchedulerSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true});
            Console.WriteLine("Created private push configuration: "+path);return true;
        }
        if (args.Contains("--generate-deployment-certificate"))
        {
            var directory = Path.GetFullPath(Path.Combine(".local", "deployment"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "key-certificate.base64");
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest("CN=Overlap Data Protection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
            using var writer = new StreamWriter(output);
            await writer.WriteAsync(Convert.ToBase64String(certificate.Export(X509ContentType.Pfx)));
            Console.WriteLine("Created private certificate file: " + path);
            Console.WriteLine("Store it securely. Reuse it across deployments; do not commit or share it.");
            return true;
        }
        if (!args.Contains("--migrate")) return false;
        var connection = config.GetConnectionString("AccountsMigration")
            ?? throw new InvalidOperationException("ConnectionStrings:AccountsMigration is required for explicit migrations.");
        if (new MySqlConnectionStringBuilder(connection).SslMode != MySqlSslMode.VerifyFull)
            throw new InvalidOperationException("Hosted migrations require SslMode=VerifyFull.");
        await using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(connection, ServerVersion.Parse(config["Database:ServerVersion"] ?? "8.4.0-mysql")).Options);
        await db.Database.MigrateAsync();
        Console.WriteLine("Database migrations completed.");
        return true;
    }
}
