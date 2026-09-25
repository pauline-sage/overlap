using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Overlap.Api.Auth;

public sealed record Credentials(string? Email, string? Password);
public sealed record EmailRequest(string? Email);
public sealed record TokenRequest(string? Email, string? Token, string? Password);
public sealed record PasswordChange(string? CurrentPassword, string? NewPassword);
public sealed record ProfileRequest(string? DisplayName);
public sealed record EmailChangeRequest(string? NewEmail, string? CurrentPassword);
public sealed record EmailChangeConfirmation(string? Email, string? NewEmail, string? Token);

public static class AuthEndpoints
{
    private const string GenericEmailMessage = "If this address is eligible, an email will arrive shortly. Check your inbox and spam folder.";
    private static bool ValidEmail(string? email) => email is { Length: > 3 and <= 254 }
        && new EmailAddressAttribute().IsValid(email) && !email.Any(char.IsControl);
    private static bool ValidPassword(string? password) => password is { Length: >= 10 and <= 128 };
    private static IResult Invalid(string message) => Results.Problem(statusCode: 400, title: message);
    private static IResult LinkInvalid() => Invalid("This link is invalid or has expired. Request a new email.");
    private static IResult GenericEmail() => Results.Ok(new { message = GenericEmailMessage });

    public static void MapAuth(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/"
            });
            return Results.NoContent();
        });
        group.MapGet("/me", async (ClaimsPrincipal principal, UserManager<Account> users) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null ? Results.Unauthorized() : Results.Ok(new { email = user.Email, displayName = user.DisplayName });
        }).RequireAuthorization();
        group.MapPost("/profile", async (ProfileRequest request, ClaimsPrincipal principal, UserManager<Account> users) =>
        {
            var name = request.DisplayName?.Trim();
            if (name is not { Length: >= 1 and <= 50 } || name.Any(char.IsControl))
                return Invalid("Choose a display name between 1 and 50 characters.");
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            user.DisplayName = name;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(new { email = user.Email, displayName = user.DisplayName })
                : Invalid("Unable to save your name. Please try again.");
        }).RequireAuthorization();
        group.MapPost("/register", Register).RequireRateLimiting("email");
        group.MapPost("/login", Login);
        group.MapPost("/activity", async (ClaimsPrincipal principal, UserManager<Account> users, SignInManager<Account> signIn) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            await signIn.SignInAsync(user, isPersistent: false);
            return Results.NoContent();
        }).RequireAuthorization();
        group.MapPost("/forgot-password", (EmailRequest request, UserManager<Account> users,
            AuthDbContext db, AccountMail mail) => SendLink(request, users, db, mail, false)).RequireRateLimiting("email");
        group.MapPost("/resend-confirmation", (EmailRequest request, UserManager<Account> users,
            AuthDbContext db, AccountMail mail) => SendLink(request, users, db, mail, true)).RequireRateLimiting("email");
        group.MapPost("/confirm-email", ConfirmEmail);
        group.MapPost("/reset-password", ResetPassword);
        group.MapPost("/change-password", ChangePassword).RequireAuthorization();
        group.MapPost("/change-email", ChangeEmail).RequireAuthorization().RequireRateLimiting("email");
        group.MapPost("/confirm-email-change", ConfirmEmailChange).RequireRateLimiting("email");
        group.MapPost("/logout", async (SignInManager<Account> signIn, ClaimsPrincipal principal, OnlinePresence presence) =>
        {
            presence.Leave(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async Task<IResult> Register(Credentials request, UserManager<Account> users,
        AuthDbContext db, AccountMail mail)
    {
        if (!ValidEmail(request.Email) || !ValidPassword(request.Password))
            return Invalid("Enter a valid email and a password between 10 and 128 characters.");
        var watch = Stopwatch.StartNew();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var email = request.Email!.Trim();
            if (await users.FindByEmailAsync(email) is not null) return GenericEmail();
            var user = new Account { UserName = email, Email = email, LastLinkEmailUtc = DateTime.UtcNow };
            var result = await users.CreateAsync(user, request.Password!);
            if (!result.Succeeded) return GenericEmail();
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            mail.Queue(email, "Confirm your Overlap account",
                $"Confirm your email to finish creating your account:\n\n{mail.Link("confirm-email", email, token)}\n\nThis link expires in one hour. If you didn't request this, ignore this email.", true);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return GenericEmail();
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlConnector.MySqlException { Number: 1062 })
        {
            // A racing registration may hit a unique index; keep the response generic.
            return GenericEmail();
        }
        finally { await PadResponse(watch); }
    }

    private static async Task<IResult> Login(Credentials request, UserManager<Account> users,
        SignInManager<Account> signIn, DummyPasswordCheck dummy)
    {
        if (!ValidEmail(request.Email) || request.Password is not { Length: > 0 and <= 128 })
            return Results.Problem(statusCode: 401, title: "Unable to sign in. Check your details and confirm your email, or try again later.");
        var user = await users.FindByEmailAsync(request.Email!.Trim());
        if (user is null || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
            dummy.Check(request.Password);
        else
        {
            var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded) return Results.Ok(new { email = user.Email, displayName = user.DisplayName });
        }
        return Results.Problem(statusCode: 401, title: "Unable to sign in. Check your details and confirm your email, or try again later.");
    }

    private static async Task<IResult> SendLink(EmailRequest request, UserManager<Account> users,
        AuthDbContext db, AccountMail mail, bool confirmation)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (!ValidEmail(request.Email)) return GenericEmail();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var user = await users.FindByEmailAsync(request.Email!.Trim());
            if (user is null || user.EmailConfirmed == confirmation) return GenericEmail();
            // Conditional database update prevents concurrent requests from bypassing the cooldown.
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-10);
            var claimed = await db.Users.Where(x => x.Id == user.Id && (x.LastLinkEmailUtc == null || x.LastLinkEmailUtc <= cutoff))
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.LastLinkEmailUtc, now));
            if (claimed == 0) return GenericEmail();
            var token = confirmation ? await users.GenerateEmailConfirmationTokenAsync(user)
                : await users.GeneratePasswordResetTokenAsync(user);
            var action = confirmation ? "confirm-email" : "reset-password";
            var subject = confirmation ? "Confirm your Overlap account" : "Reset your Overlap password";
            mail.Queue(user.Email!, subject,
                $"{subject}:\n\n{mail.Link(action, user.Email!, token)}\n\nThis link expires in one hour. If you didn't request it, ignore this email. Your password has not changed.", true);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return GenericEmail();
        }
        finally { await PadResponse(watch); }
    }

    private static async Task<IResult> ConfirmEmail(TokenRequest request, UserManager<Account> users,
        AuthDbContext db, AccountMail mail)
    {
        if (!ValidEmail(request.Email) || request.Token is not { Length: > 0 and <= 4096 }) return LinkInvalid();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByEmailAsync(request.Email!.Trim());
        if (user is null || user.EmailConfirmed) return LinkInvalid();
        var result = await users.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded) return LinkInvalid();
        mail.Queue(user.Email!, "Your Overlap account is ready", "Your email is confirmed. You can now sign in to Overlap.\n\nIf you didn't create this account, do not share any verification links.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(new { message = "Email confirmed. You can now sign in." });
    }

    private static async Task<IResult> ResetPassword(TokenRequest request, UserManager<Account> users,
        AuthDbContext db, AccountMail mail)
    {
        if (!ValidPassword(request.Password)) return Invalid("Use a password between 10 and 128 characters.");
        if (!ValidEmail(request.Email) || request.Token is not { Length: > 0 and <= 4096 }) return LinkInvalid();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByEmailAsync(request.Email!.Trim());
        if (user is null || !user.EmailConfirmed) return LinkInvalid();
        var result = await users.ResetPasswordAsync(user, request.Token, request.Password!);
        if (!result.Succeeded) return LinkInvalid();
        mail.Queue(user.Email!, "Your Overlap password was reset",
            "Your password was reset. All previous sessions are now invalid.\n\nIf this wasn't you, use Forgot password in Overlap immediately to recover your account.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(new { message = "Password reset. Sign in with your new password." });
    }

    private static async Task<IResult> ChangePassword(PasswordChange request, ClaimsPrincipal principal,
        UserManager<Account> users, SignInManager<Account> signIn, AuthDbContext db, AccountMail mail)
    {
        if (!ValidPassword(request.NewPassword) || request.CurrentPassword is not { Length: > 0 and <= 128 })
            return Invalid("Enter your current password and a new password between 10 and 128 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (await users.IsLockedOutAsync(user)) return Invalid("Unable to change password. Try again later.");
        var result = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            await transaction.CommitAsync();
            return Invalid("Unable to change password. Check your current password or try again later.");
        }
        var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword!);
        if (!changed.Succeeded) return Invalid("Unable to change password. Please try again.");
        mail.Queue(user.Email!, "Your Overlap password was changed",
            "Your password was changed. All previous sessions are now invalid.\n\nIf this wasn't you, use Forgot password in Overlap immediately to recover your account.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await signIn.SignOutAsync();
        return Results.Ok(new { message = "Password changed. Sign in again with your new password." });
    }

    private static async Task<IResult> ChangeEmail(EmailChangeRequest request, ClaimsPrincipal principal,
        UserManager<Account> users, SignInManager<Account> signIn, AuthDbContext db, AccountMail mail)
    {
        if (!ValidEmail(request.NewEmail) || request.CurrentPassword is not { Length: > 0 and <= 128 })
            return Invalid("Enter a valid new email and your current password.");
        var watch = Stopwatch.StartNew();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
            if (!check.Succeeded)
            {
                await transaction.CommitAsync();
                return Invalid("Unable to change email. Check your password or try again later.");
            }
            var newEmail = request.NewEmail!.Trim();
            if (await users.FindByEmailAsync(newEmail) is not null) return GenericEmail();
            var now = DateTime.UtcNow; var cutoff = now.AddMinutes(-10);
            var claimed = await db.Users.Where(x => x.Id == user.Id && (x.LastLinkEmailUtc == null || x.LastLinkEmailUtc <= cutoff))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLinkEmailUtc, now));
            if (claimed == 0) return GenericEmail();
            var token = await users.GenerateChangeEmailTokenAsync(user, newEmail);
            var link = mail.Link("confirm-email-change", user.Email!, token) + "&newEmail=" + Uri.EscapeDataString(newEmail);
            mail.Queue(newEmail, "Confirm your new Overlap email",
                $"Confirm your new sign-in email:\n\n{link}\n\nThis link expires in one hour. Your old email works until you confirm. If you didn't request this, ignore this email.", true);
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return GenericEmail();
        }
        finally { await PadResponse(watch); }
    }

    private static async Task<IResult> ConfirmEmailChange(EmailChangeConfirmation request, UserManager<Account> users,
        SignInManager<Account> signIn, AuthDbContext db, AccountMail mail)
    {
        if (!ValidEmail(request.Email) || !ValidEmail(request.NewEmail) || request.Token is not { Length: > 0 and <= 4096 }) return LinkInvalid();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByEmailAsync(request.Email!.Trim());
        if (user is null || !user.EmailConfirmed) return LinkInvalid();
        var newEmail = request.NewEmail!.Trim();
        if (await users.FindByEmailAsync(newEmail) is not null) return LinkInvalid();
        var oldEmail = user.Email!;
        try
        {
            if (!(await users.ChangeEmailAsync(user, newEmail, request.Token)).Succeeded) return LinkInvalid();
            if (!(await users.SetUserNameAsync(user, newEmail)).Succeeded) return LinkInvalid();
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return LinkInvalid();
            mail.Queue(oldEmail, "Your Overlap email was changed",
                "Your Overlap sign-in email has changed. All previous sessions were signed out. If you did not request this change, your account may be compromised.");
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlConnector.MySqlException { Number: 1062 }) { return LinkInvalid(); }
        await signIn.SignOutAsync();
        return Results.Ok(new { message = "Email changed. Sign in with your new email address." });
    }

    private static async Task PadResponse(Stopwatch watch)
    {
        // Reduce common account-existence timing differences; rate limiting bounds concurrency.
        var delay = TimeSpan.FromMilliseconds(800 + Random.Shared.Next(100)) - watch.Elapsed;
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
    }
}

public sealed class DummyPasswordCheck
{
    private readonly PasswordHasher<Account> hasher;
    private readonly Account account = new();
    private readonly string hash;
    public DummyPasswordCheck(IOptions<PasswordHasherOptions> options)
    {
        hasher = new PasswordHasher<Account>(options);
        hash = hasher.HashPassword(account, Guid.NewGuid().ToString());
    }
    public void Check(string password) => hasher.VerifyHashedPassword(account, hash, password);
}
