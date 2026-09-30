using System.Net;
using Overlap.Api.Auth;
using Xunit;
namespace Overlap.Api.Tests;
public sealed class EmailTemplateTests
{
    [Theory]
    [InlineData("confirm-email", "Confirm email")]
    [InlineData("reset-password", "Reset password")]
    [InlineData("confirm-email-change", "Confirm new email")]
    public void ActionLinksKeepTheirExactTokenAndHaveATextAlternative(string action, string label)
    {
        var link=$"https://overlap.example.test/#{action}?email=person%40example.test&token=example%2Btoken%2Fvalue%3D";
        var body=$"Please confirm your request.\n\n{link}\n\nThis link expires in one hour.";
        var email=new AccountEmail("person@example.test","Account request",body);
        var html=AccountEmailTemplate.Render(email,"https://overlap.example.test");
        Assert.Contains($"href=\"{WebUtility.HtmlEncode(link)}\"",html);
        Assert.Contains($">{label}</a>",html);
        Assert.Contains("This link expires in one hour.",html);
        Assert.Contains(link,email.Body);
        Assert.DoesNotContain("<img",html);
        Assert.DoesNotContain("<script",html);
    }
    [Theory]
    [InlineData("https://evil.example/#confirm-email?token=example")]
    [InlineData("https://overlap.example.test.evil.example/#confirm-email?token=example")]
    [InlineData("https://overlap.example.test:8443/#confirm-email?token=example")]
    [InlineData("https://overlap.example.test/elsewhere/#confirm-email?token=example")]
    [InlineData("javascript:alert(1)")]
    public void OnlyConfiguredApplicationActionsBecomeClickable(string link)
    {
        var html=AccountEmailTemplate.Render(new("person@example.test","Request",link),"https://overlap.example.test");
        Assert.DoesNotContain("href=",html);
    }
    [Fact]
    public void DynamicContentIsEscapedAndOldQueuedMessagesStillRender()
    {
        var html=AccountEmailTemplate.Render(new("person@example.test","<script>alert(1)</script>","<img src=x onerror=alert(1)>\nSecond line"),null);
        Assert.DoesNotContain("<script>",html);Assert.DoesNotContain("<img",html);
        Assert.Contains("&lt;script&gt;",html);Assert.Contains("<br />Second line",html);
        Assert.Contains("overlap",html);
    }
}
