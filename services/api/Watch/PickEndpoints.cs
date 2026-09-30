using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;

namespace Overlap.Api.Watch;

public sealed class SharedPick
{
    public long Id { get; set; }
    public string? PairId { get; set; }
    public bool IsShared { get; set; }
    public required string ProposedBy { get; set; }
    public required string TitleId { get; set; }
    public required string Content { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedUtc { get; set; }
    public string? SenderDismissedRevision { get; set; }
    public string? RecipientDismissedRevision { get; set; }
    public string Revision { get; set; } = "";
    public bool SenderHasNews { get; set; }
    public bool RecipientHasNews { get; set; }
}
public sealed record PickDecision(string Status);
public sealed record PickSharing(bool Shared);

public static class PickEndpoints
{
    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static Task<string?> Pair(AuthDbContext db, string user) => db.PairMembers.Where(x => x.AccountId == user).Select(x => x.PairId).SingleOrDefaultAsync();
    public static void MapPicks(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/watch/picks").RequireAuthorization().RequireRateLimiting("picks");
        group.MapGet("", async (ClaimsPrincipal user, AuthDbContext db) =>
        {
            var id = UserId(user);
            var pair = await Pair(db, id);
            var picks = await db.Set<SharedPick>().AsNoTracking().Where(x => x.ProposedBy == id || (pair != null && x.PairId == pair && x.IsShared && db.Set<SharingGrant>().Any(g => g.PairId == pair && g.SenderId == x.ProposedBy && g.Category == "picks" && g.Status == "accepted"))).OrderByDescending(x => x.Id).ToListAsync();
            return Results.Ok(new { connected = pair is not null, picks = picks.Select(x => new { x.Id, x.Status, shared = x.IsShared, mine = x.ProposedBy == id, x.CreatedUtc, title = JsonSerializer.Deserialize<JsonElement>(x.Content) }) });
        });
        group.MapPost("", async (JsonElement title, ClaimsPrincipal user, AuthDbContext db) =>
        {
            if (!Valid(title)) return Results.Problem(statusCode: 400, title: "This title cannot be shared.");
            var id = UserId(user);
            var pair = await Pair(db, id);

            var titleId = title.GetProperty("id").GetString()!;
            if (await db.Set<SharedPick>().AnyAsync(x => x.ProposedBy == id && x.TitleId == titleId)) return Results.NoContent();
            if (await db.Set<SharedPick>().CountAsync(x => x.ProposedBy == id) >= 200) return Results.Problem(statusCode: 409, title: "Your list is full. Remove some of your picks first.");
            db.Add(new SharedPick { PairId = null, ProposedBy = id, TitleId = titleId, Content = title.GetRawText(), CreatedUtc = DateTime.UtcNow });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { if (await db.Set<SharedPick>().AsNoTracking().AnyAsync(x => x.ProposedBy == id && x.TitleId == titleId)) return Results.NoContent(); throw; }
            return Results.NoContent();
        });
        group.MapPost("/{pickId:long}/share", async (long pickId, PickSharing request, ClaimsPrincipal user, AuthDbContext db) =>
        {
            var id = UserId(user);
            var pair = await Pair(db, id);
            if (request.Shared && pair is null) return Results.Problem(statusCode: 409, title: "Connect with your person before sharing a pick.");
            if (request.Shared && !await PrivacyEndpoints.Allowed(db,pair,id,"picks"))
                return Results.Problem(statusCode:409,title:"Enable picks sharing in Settings first.");
            var revision = Guid.NewGuid().ToString("N");
            var count = await db.Set<SharedPick>().Where(x => x.Id == pickId && x.ProposedBy == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsShared, request.Shared)
                    .SetProperty(x => x.PairId, request.Shared ? pair : null).SetProperty(x => x.Status, "pending")
                    .SetProperty(x => x.Revision, revision)
                    .SetProperty(x => x.SenderHasNews, false).SetProperty(x => x.RecipientHasNews, request.Shared));
            return count == 1 ? Results.NoContent() : Results.NotFound();
        });
        group.MapPost("/{pickId:long}/decision", async (long pickId, PickDecision decision, ClaimsPrincipal user, AuthDbContext db) =>
        {
            if (decision.Status is not ("accepted" or "declined")) return Results.BadRequest();
            var id = UserId(user);
            var pair = await Pair(db, id);
            var revision = Guid.NewGuid().ToString("N");
            var count = await db.Set<SharedPick>().Where(x => x.Id == pickId && pair != null && x.PairId == pair && x.IsShared && x.ProposedBy != id && x.Status == "pending" && db.Set<SharingGrant>().Any(g => g.PairId == pair && g.SenderId == x.ProposedBy && g.Category == "picks" && g.Status == "accepted"))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, decision.Status)
                    .SetProperty(x => x.Revision, revision)
                    .SetProperty(x => x.SenderHasNews, true).SetProperty(x => x.RecipientHasNews, false));
            return count == 1 ? Results.NoContent() : Results.Problem(statusCode: 409, title: "This pick is unavailable or has already received a response.");
        });
        group.MapPost("/{pickId:long}/remove", async (long pickId, ClaimsPrincipal user, AuthDbContext db) =>
        {
            var id = UserId(user);
            var pair = await Pair(db, id);
            var count = await db.Set<SharedPick>().Where(x => x.Id == pickId && x.ProposedBy == id).ExecuteDeleteAsync();
            return count == 1 ? Results.NoContent() : Results.NotFound();
        });
    }
    // Store only a bounded display snapshot, never executable markup or arbitrary remote URLs.
    internal static bool Valid(JsonElement title)
    {
        if (title.ValueKind != JsonValueKind.Object || title.GetRawText().Length > 16000) return false;
        foreach (var key in new[] { "id", "title", "format", "description", "ownCountry", "partnerCountry", "ownLink", "partnerLink" })
            if (!title.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return false;
        if (title.GetProperty("id").GetString() is not { Length: > 0 and <= 80 } || title.GetProperty("title").GetString() is not { Length: > 0 and <= 300 }) return false;
        foreach (var key in new[] { "genres", "ownServices", "partnerServices" })
            if (!title.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 30 || array.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) return false;
        foreach (var key in new[] { "ownLink", "partnerLink", "poster" })
        {
            if (!title.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            if (value.ValueKind != JsonValueKind.String || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo != "" || !uri.IsDefaultPort ||
                uri.Host is not ("www.themoviedb.org" or "www.youtube.com" or "youtube.com" or "image.tmdb.org" or "i.ytimg.com")) return false;
        }
        return true;
    }
}
