using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Overlap.Api.Auth;

public sealed record PhotoRequest(string? Pixels);
public sealed record InvitationRequest(string? Token);

public static class TogetherEndpoints
{
    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static IResult Invalid(string message) => Results.Problem(statusCode: 400, title: message);

    public static void MapTogether(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/together").RequireAuthorization().RequireRateLimiting("auth");
        group.MapGet("/", Status);
        group.MapGet("/photo/{id}", Photo);
        group.MapPost("/photo", SavePhoto);
        group.MapPost("/invite", CreateInvite);
        group.MapPost("/revoke", async (ClaimsPrincipal principal, AuthDbContext db) =>
        {
            await db.Users.Where(x => x.Id == UserId(principal)).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.InvitationHash, (string?)null).SetProperty(x => x.InvitationExpiresUtc, (DateTime?)null));
            return Results.NoContent();
        });
        group.MapPost("/preview", async (InvitationRequest request, ClaimsPrincipal principal, AuthDbContext db) =>
        {
            var owner = await FindInvitation(request.Token, db);
            if (owner is null || owner.Id == UserId(principal)) return Invalid("This invitation is invalid, expired, or belongs to you.");
            if (await db.PairMembers.AnyAsync(x => x.AccountId == owner.Id || x.AccountId == UserId(principal)))
                return Invalid("One of you is already connected. Leave that connection before joining another.");
            return Results.Ok(new { displayName = "Your person", expiresUtc = owner.InvitationExpiresUtc });
        });
        group.MapPost("/accept", Accept);
        group.MapPost("/leave", async (ClaimsPrincipal principal, AuthDbContext db) =>
        {
            var pair = await db.PairMembers.Where(x => x.AccountId == UserId(principal)).Select(x => x.PairId).SingleOrDefaultAsync();
            if (pair is not null) {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Set<Overlap.Api.Watch.SharedPick>().Where(x => x.PairId == pair).ExecuteUpdateAsync(s => s.SetProperty(x => x.PairId, (string?)null).SetProperty(x => x.IsShared, false).SetProperty(x => x.Status, "pending"));
                await db.Set<Overlap.Api.Outings.OutingUpdate>().Where(x => x.PairId == pair).ExecuteDeleteAsync();
                await db.Set<Overlap.Api.Watch.WatchedTitle>().Where(x => x.PairId == pair).ExecuteDeleteAsync();
                await db.Set<Overlap.Api.Books.ReadingEntry>().Where(x => x.PairId == pair).ExecuteDeleteAsync();
                await db.Set<SharingGrant>().Where(x => x.PairId == pair).ExecuteDeleteAsync();
                await db.PairMembers.Where(x => x.PairId == pair).ExecuteDeleteAsync();
                await transaction.CommitAsync();

            }
            return Results.NoContent();
        });
    }

    private static async Task<IResult> Status(ClaimsPrincipal principal, AuthDbContext db, OnlinePresence presence)
    {
        var id = UserId(principal);
        var own = await db.Users.Where(x => x.Id == id).Select(x => new { x.DisplayName, HasPhoto = x.Photo != null, x.InvitationExpiresUtc, x.SharePresence }).SingleAsync();
        if (own.SharePresence) presence.Seen(id); else presence.Leave(id);
        var pair = await db.PairMembers.Where(x => x.AccountId == id).Select(x => x.PairId).SingleOrDefaultAsync();
        var partnerId = pair is null ? null : await db.PairMembers.Where(x => x.PairId == pair && x.AccountId != id).Select(x => x.AccountId).SingleOrDefaultAsync();
        var profileAllowed = partnerId is not null && await PrivacyEndpoints.Allowed(db,pair,partnerId,"profile");
        var presenceAllowed = partnerId is not null && await PrivacyEndpoints.Allowed(db,pair,partnerId,"presence");
        var partner = await db.Users.Where(x => x.Id == partnerId).Select(x => new { x.Id, DisplayName = profileAllowed && x.ShareDisplayName ? x.DisplayName : null, HasPhoto = profileAllowed && x.SharePhoto && x.Photo != null, x.SharePresence }).SingleOrDefaultAsync();
        return Results.Ok(new { id, own.HasPhoto, online = true, invitationExpiresUtc = own.InvitationExpiresUtc > DateTime.UtcNow ? own.InvitationExpiresUtc : null,
            partner = partner is null ? null : new { partner.Id, partner.DisplayName, partner.HasPhoto, online = presenceAllowed && partner.SharePresence ? (bool?)presence.IsOnline(partner.Id) : null } });
    }

    private static async Task<IResult> Photo(string id, ClaimsPrincipal principal, AuthDbContext db)
    {
        var caller = UserId(principal);
        if (id != caller)
        {
            var pair = await db.PairMembers.Where(x => x.AccountId == caller).Select(x => x.PairId).SingleOrDefaultAsync();
            if (pair is null || !await db.PairMembers.AnyAsync(x => x.AccountId == id && x.PairId == pair) ||
                !await db.Users.AnyAsync(x => x.Id == id && x.SharePhoto) ||
                !await PrivacyEndpoints.Allowed(db,pair,id,"profile")) return Results.NotFound();
        }
        var photo = await db.Users.Where(x => x.Id == id).Select(x => x.Photo).SingleOrDefaultAsync();
        return photo is null ? Results.NotFound() : Results.File(photo, "image/png");
    }

    private static async Task<IResult> SavePhoto(HttpContext context, ClaimsPrincipal principal, AuthDbContext db)
    {
        // Only this endpoint raises the default 16 KiB request limit, before reading JSON.
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 70 * 1024;
        PhotoRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<PhotoRequest>(); }
        catch (System.Text.Json.JsonException) { return Invalid("Choose a valid photo."); }
        if (request is null) return Invalid("Choose a valid photo.");
        byte[]? photo = null;
        if (request.Pixels is not null)
        {
            if (request.Pixels.Length != 65536) return Invalid("Photo must contain 128 by 128 RGB pixels.");
            try { photo = AvatarImage.Encode(Convert.FromBase64String(request.Pixels)); }
            catch (ArgumentException) { return Invalid("Choose a valid photo."); }
            catch (FormatException) { return Invalid("Choose a valid photo."); }
        }
        await db.Users.Where(x => x.Id == UserId(principal)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Photo, photo));
        return Results.NoContent();
    }

    private static async Task<IResult> CreateInvite(ClaimsPrincipal principal, AuthDbContext db, IConfiguration config)
    {
        var id = UserId(principal);
        if (await db.PairMembers.AnyAsync(x => x.AccountId == id)) return Invalid("You are already connected.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = Hash(token);
        var expires = DateTime.UtcNow.AddHours(48);
        await db.Users.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.InvitationHash, hash).SetProperty(x => x.InvitationExpiresUtc, expires));
        return Results.Ok(new { url = $"{config["Auth:PublicAppUrl"]!.TrimEnd('/')}/#invite?token={token}", expiresUtc = expires });
    }
    private static Task<Account?> FindInvitation(string? token, AuthDbContext db)
    {
        if (token is null || token.Length != 64) return Task.FromResult<Account?>(null);
        var hash = Hash(token);
        return db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.InvitationHash == hash && x.InvitationExpiresUtc > DateTime.UtcNow);
    }
    private static async Task<IResult> Accept(InvitationRequest request, ClaimsPrincipal principal, AuthDbContext db)
    {
        var id = UserId(principal);
        var owner = await FindInvitation(request.Token, db);
        if (owner is null || owner.Id == id) return Invalid("This invitation is invalid, expired, or belongs to you.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var claimed = await db.Users.Where(x => x.Id == owner.Id && x.InvitationHash == owner.InvitationHash && x.InvitationExpiresUtc > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.InvitationHash, (string?)null).SetProperty(x => x.InvitationExpiresUtc, (DateTime?)null));
        if (claimed != 1) return Invalid("This invitation is no longer available.");
        if (await db.PairMembers.AnyAsync(x => x.AccountId == id || x.AccountId == owner.Id)) return Invalid("One of you is already connected.");
        var pair = Guid.NewGuid().ToString("N");
        db.PairMembers.AddRange(new PairMember { AccountId = owner.Id, PairId = pair }, new PairMember { AccountId = id, PairId = pair });
        try
        {
            await db.SaveChangesAsync();
            await db.Users.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.InvitationHash, (string?)null).SetProperty(x => x.InvitationExpiresUtc, (DateTime?)null));
            await transaction.CommitAsync();
        }
        catch (DbUpdateException) { return Invalid("A connection changed. Refresh and try again."); }
        return Results.NoContent();
    }
}
