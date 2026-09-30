using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Outings;

public sealed class OutingUpdate
{
    public required string PairId { get; set; }
    public required string SenderId { get; set; }
    public string? VenueJson { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public bool OnMyWay { get; set; }
    public string Revision { get; set; } = "";
    public bool Seen { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? ReplyKind { get; set; }
    public string? ReplyVenueJson { get; set; }
    public string ReplyRevision { get; set; } = "";
    public bool ReplySeen { get; set; } = true;
}
public sealed record SendOutingUpdate(string Kind, string? VenueToken);
public static class OutingUpdateEndpoints
{
    public static void MapOutingUpdates(this WebApplication app)
    {
        app.MapPost("/api/auth/outings/update", async (SendOutingUpdate request, ClaimsPrincipal user,
            AuthDbContext db, IDataProtectionProvider protection) =>
        {
            if (request.Kind is not ("venue" or "on-my-way" or "clear")) return Results.BadRequest();
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair = await db.PairMembers.Where(x => x.AccountId == id).Select(x => x.PairId).SingleOrDefaultAsync();
            if (pair is null) return Results.Problem(statusCode:409, title:"Connect with your person before sharing.");
            SharedVenue? venue = null;
            if (request.Kind == "venue")
            {
                try
                {
                    if (request.VenueToken is not { Length: > 0 and <= 8192 }) return Results.BadRequest();
                    venue = JsonSerializer.Deserialize<SharedVenue>(protection.CreateProtector("Overlap.OutingVenue.v1")
                        .ToTimeLimitedDataProtector().Unprotect(request.VenueToken));
                    if (venue is null) return Results.BadRequest();
                }
                catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException)
                { return Results.Problem(statusCode:400, title:"Search again before sharing this place."); }
            }
            if (request.Kind == "clear")
            {
                await db.Set<OutingUpdate>().Where(x => x.PairId == pair && x.SenderId == id).ExecuteDeleteAsync();
                return Results.NoContent();
            }
            var update = await db.Set<OutingUpdate>().FindAsync(pair, id);
            if (update is null) { update = new() { PairId = pair, SenderId = id }; db.Add(update); }
            // Each explicit message replaces the previous one; "on my way" shares no venue.
            update.VenueJson = venue is null ? null : JsonSerializer.Serialize(venue);
            update.OnMyWay = request.Kind == "on-my-way";
            update.UpdatedUtc = DateTime.UtcNow;
            update.Revision = Guid.NewGuid().ToString("N");
            update.Seen = false;
            update.CancelledBy = null;
            update.CancellationReason = null;
            update.ReplyKind = null;
            update.ReplyVenueJson = null;
            update.ReplyRevision = "";
            update.ReplySeen = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("picks");
    }
}
