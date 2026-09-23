using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Overlap.Api.Auth;

public sealed record AccountEmail(string Recipient, string Subject, string Body);

public sealed class AccountMail(AuthDbContext db, IDataProtectionProvider protection, IConfiguration config)
{
    public const string ProtectionPurpose = "Overlap.AccountEmails.v1";

    public void Queue(string recipient, string subject, string body, bool containsLink = false)
    {
        var now = DateTime.UtcNow;
        db.PendingEmails.Add(new PendingEmail
        {
            ProtectedContent = protection.CreateProtector(ProtectionPurpose)
                .Protect(JsonSerializer.Serialize(new AccountEmail(recipient, subject, body))),
            CreatedUtc = now,
            NextAttemptUtc = now,
            ExpiresUtc = now.AddHours(containsLink ? 1 : 24)
        });
    }

    public string Link(string action, string email, string token) =>
        $"{config["Auth:PublicAppUrl"]!.TrimEnd('/')}/#{action}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
}

// One dispatcher for this single-instance monolith. Multiple instances require database leasing.
public sealed class AccountMailDispatcher(
    IServiceScopeFactory scopes, IConfiguration config, IHostEnvironment environment,
    IDataProtectionProvider protection, ILogger<AccountMailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Provider exception text can contain addresses or credentials; do not log it.
                logger.LogWarning("Account email delivery is unavailable; pending messages will be retried.");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var now = DateTime.UtcNow;
        await db.PendingEmails.Where(x => x.ExpiresUtc <= now).ExecuteDeleteAsync(cancellationToken);
        var messages = await db.PendingEmails.Where(x => x.NextAttemptUtc <= now && x.Attempts < 8)
            .OrderBy(x => x.Id).Take(10).ToListAsync(cancellationToken);
        foreach (var pending in messages)
        {
            try
            {
                var content = protection.CreateProtector(AccountMail.ProtectionPurpose).Unprotect(pending.ProtectedContent);
                var email = JsonSerializer.Deserialize<AccountEmail>(content)!;
                using var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(config["Email:From"]!));
                message.To.Add(MailboxAddress.Parse(email.Recipient));
                message.Subject = email.Subject;
                message.MessageId = $"overlap-{pending.Id}@{MailboxAddress.Parse(config["Email:From"]!).Domain}";
                message.Body = new TextPart("plain") { Text = email.Body };
                using var smtp = new SmtpClient { Timeout = 15000 };
                var host = config["Email:Host"]!;
                var localCapture = environment.IsDevelopment() && config.GetValue<bool>("Email:LocalCapture")
                    && host is "127.0.0.1" or "localhost";
                var security = localCapture ? SecureSocketOptions.None
                    : config.GetValue<int>("Email:Port") == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                await smtp.ConnectAsync(host, config.GetValue<int>("Email:Port"), security, cancellationToken);
                if (!string.IsNullOrEmpty(config["Email:Username"]))
                    await smtp.AuthenticateAsync(config["Email:Username"]!, config["Email:Password"] ?? "", cancellationToken);
                await smtp.SendAsync(message, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);
                db.PendingEmails.Remove(pending);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                pending.Attempts++;
                pending.NextAttemptUtc = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, pending.Attempts)));
                logger.LogWarning("Account email {MessageId} failed on attempt {Attempt}.", pending.Id, pending.Attempts);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
