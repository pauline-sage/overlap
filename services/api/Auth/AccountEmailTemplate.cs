using System.Net;
using System.Text;

namespace Overlap.Api.Auth;

public static class AccountEmailTemplate
{
    public static string Render(AccountEmail email, string? publicAppUrl)
    {
        static string Escape(string text) => WebUtility.HtmlEncode(text);
        var title = email.Subject switch
        {
            "Confirm your Overlap account" => "Confirm your email",
            "Reset your Overlap password" => "Reset your password",
            "Your Overlap account is ready" => "Your account is ready",
            "Your Overlap password was reset" or "Your Overlap password was changed" => "Your password was updated",
            "Confirm your new Overlap email" => "Confirm your new email",
            "Your Overlap email was changed" => "Your email was updated",
            "New Overlap registration" => "A new account was created",
            _ => email.Subject
        };
        var content = new StringBuilder();
        foreach (var paragraph in email.Body.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var value = paragraph.Trim();
            var action = ActionLabel(value, publicAppUrl);
            if (action is not null)
            {
                var url = Escape(value);
                content.Append($$"""
                    <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:24px 0"><tr><td bgcolor="#c4e5a0" style="border-radius:12px;text-align:center">
                    <a href="{{url}}" style="display:inline-block;padding:16px 28px;border:1px solid #c4e5a0;border-radius:12px;background-color:#c4e5a0;color:#172511;font-size:16px;font-weight:700;line-height:20px;text-decoration:none;mso-padding-alt:0;text-underline-color:#c4e5a0">{{Escape(action)}}</a>
                    </td></tr></table>
                    <p style="margin:0 0 24px;font-size:13px;line-height:20px;color:#596a5e">Button not working? <a href="{{url}}" style="color:#315b46;text-decoration:underline">Open the secure link</a>.</p>
                    """);
            }
            else content.Append($"<p style=\"margin:0 0 20px;font-size:16px;line-height:26px;color:#39473e;overflow-wrap:anywhere\">{Escape(value).Replace("\n", "<br />")}</p>");
        }
        return $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="color-scheme" content="light"><title>{{Escape(email.Subject)}}</title></head>
            <body style="margin:0;padding:0;background-color:#f5f5ef;font-family:Arial,Helvetica,sans-serif;-webkit-text-size-adjust:100%">
            <div style="display:none;max-height:0;overflow:hidden;font-size:1px;line-height:1px;color:#f5f5ef;mso-hide:all">{{Escape(title)}}. A notification from Overlap.</div>
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" bgcolor="#f5f5ef"><tr><td align="center" style="padding:32px 12px">
            <!--[if mso]><table role="presentation" width="560" cellspacing="0" cellpadding="0" border="0"><tr><td><![endif]-->
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="max-width:560px;border-collapse:separate">
            <tr><td bgcolor="#101714" style="padding:24px 28px;border-radius:20px 20px 0 0;background-color:#101714;color:#f3f6ef;font-size:28px;line-height:34px;font-weight:700;letter-spacing:-1px">overlap<span style="color:#c4e5a0">.</span></td></tr>
            <tr><td bgcolor="#ffffff" style="padding:32px 28px 20px;border:1px solid #cbd5cc;border-top:0;border-radius:0 0 20px 20px">
            <p style="margin:0 0 12px;font-size:11px;line-height:18px;letter-spacing:2px;font-weight:700;color:#596a5e">{{(email.Subject == "New Overlap registration" ? "SITE NOTIFICATION" : "YOUR ACCOUNT")}}</p>
            <h1 style="margin:0 0 24px;font-size:28px;line-height:35px;letter-spacing:-.6px;color:#19271f;font-weight:700">{{Escape(title)}}</h1>
            {{content}}
            <p style="margin:12px 0 0;padding-top:20px;border-top:1px solid #e8ede6;font-size:13px;line-height:21px;color:#596a5e">Keep account links private. Overlap will never ask you to send your password by email.</p>
            </td></tr>
            <tr><td align="center" style="padding:20px 12px;font-size:12px;line-height:20px;color:#596a5e">Overlap &middot; Account notifications</td></tr>
            </table>
            <!--[if mso]></td></tr></table><![endif]-->
            </td></tr></table></body></html>
            """;
    }

    private static string? ActionLabel(string value, string? publicAppUrl)
    {
        if (!Uri.TryCreate(publicAppUrl, UriKind.Absolute, out var app) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var link) ||
            (link.Scheme != "https" && !(link.Scheme == "http" && link.IsLoopback)) ||
            link.UserInfo.Length != 0 || link.Scheme != app.Scheme || link.Host != app.Host || link.Port != app.Port ||
            link.AbsolutePath.TrimEnd('/') != app.AbsolutePath.TrimEnd('/')) return null;
        return link.Fragment.Split('?', 2)[0] switch
        {
            "#confirm-email" => "Confirm email",
            "#reset-password" => "Reset password",
            "#confirm-email-change" => "Confirm new email",
            _ => null
        };
    }
}
