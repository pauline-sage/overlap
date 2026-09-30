using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Overlap.Api.Auth;

public sealed class AccountEmailSender(HttpClient http, IConfiguration config, IHostEnvironment environment)
{
    public async Task SendAsync(AccountEmail email, long id, CancellationToken cancellationToken)
    {
        var from = MailboxAddress.Parse(config["Email:From"]!);
        if (string.IsNullOrWhiteSpace(from.Name)) from = new MailboxAddress("Overlap", from.Address);
        var html = AccountEmailTemplate.Render(email, config["Auth:PublicAppUrl"]);
        if (config["Email:Transport"] == "Mailjet")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mailjet.com/v3.1/send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                Encoding.UTF8.GetBytes(config["Email:Username"] + ":" + config["Email:Password"])));
            request.Content = JsonContent.Create(new { Messages = new[] { new {
                From = new { Email = from.Address, Name = from.Name },
                To = new[] { new { Email = email.Recipient } },
                Subject = email.Subject, TextPart = email.Body, HTMLPart = html,
                TrackOpens = "disabled", TrackClicks = "disabled"
            } } }, options: new JsonSerializerOptions { PropertyNamingPolicy = null });
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!result.RootElement.TryGetProperty("Messages", out var messages) || messages.GetArrayLength() != 1 ||
                !messages[0].TryGetProperty("Status", out var status) || status.GetString() != "success")
                throw new InvalidOperationException("Mailjet did not accept the message.");
            return;
        }
        using var message = new MimeMessage();
        message.From.Add(from);
        message.To.Add(MailboxAddress.Parse(email.Recipient));
        message.Subject = email.Subject;
        message.MessageId = $"overlap-{id}@{from.Domain}";
        message.Body = new BodyBuilder { TextBody = email.Body, HtmlBody = html }.ToMessageBody();
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
    }
}
