using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Outings;

public sealed record CancelOuting(string ProposalRevision, string? Reason);
public sealed record OutingReply(string ProposalRevision, string Kind, string? VenueToken);
public static class OutingReplyEndpoints
{
    public static async Task<OutingUpdate?> FindProposal(AuthDbContext db, ClaimsPrincipal user, string revision)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pair = await db.PairMembers.Where(x => x.AccountId == id).Select(x => x.PairId).SingleOrDefaultAsync();
        return await db.Set<OutingUpdate>().AsNoTracking().SingleOrDefaultAsync(x =>
            pair != null && x.PairId == pair && x.SenderId != id && x.Revision == revision && x.VenueJson != null);
    }
    public static SharedVenue? Details(OutingUpdate? proposal, DateTimeOffset now)
    {
        var venue = proposal?.CancelledBy != null || proposal?.VenueJson is null ? null : JsonSerializer.Deserialize<SharedVenue>(proposal.VenueJson);
        return venue?.StartUtc is DateTimeOffset start && start.AddHours(1) > now &&
            venue.Activity != null && OutingMatch.Categories.ContainsKey(venue.Activity) ? venue : null;
    }
    public static void MapOutingReplies(this WebApplication app)
    {
        app.MapPost("/api/auth/outings/cancel", async (CancelOuting request, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock) =>
        {
            if (request.ProposalRevision is not { Length:32 } || request.Reason?.Length > 500)
                return Results.BadRequest();
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair = await db.PairMembers.Where(x => x.AccountId == id).Select(x => x.PairId).SingleOrDefaultAsync();
            var proposal = await db.Set<OutingUpdate>().AsNoTracking().SingleOrDefaultAsync(x => pair != null && x.PairId == pair && x.Revision == request.ProposalRevision);
            var venue = proposal?.VenueJson is null ? null : JsonSerializer.Deserialize<SharedVenue>(proposal.VenueJson);
            if (venue?.StartUtc is not DateTimeOffset start || start <= clock.GetUtcNow())
                return Results.Problem(statusCode:409,title:"This date has passed or changed. Refresh Dates.");
            var revision = Guid.NewGuid().ToString("N");
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            var count = await db.Set<OutingUpdate>().Where(x => pair != null && x.PairId == pair &&
                x.Revision == request.ProposalRevision && x.VenueJson != null && x.CancelledBy == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CancelledBy,id)
                    .SetProperty(x => x.CancellationReason,reason)
                    .SetProperty(x => x.Revision,revision).SetProperty(x => x.ReplyRevision,revision)
                    .SetProperty(x => x.Seen,x => x.SenderId != id)
                    .SetProperty(x => x.ReplySeen,x => x.SenderId == id));
            return count == 1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"This date changed. Refresh Dates.");
        }).RequireAuthorization().RequireRateLimiting("picks");
        app.MapPost("/api/auth/outings/reply", async (OutingReply request, ClaimsPrincipal user,
            AuthDbContext db, IDataProtectionProvider protection, TimeProvider clock) =>
        {
            if (request.Kind is not ("picked" or "surprise") || request.ProposalRevision is not { Length:32 }) return Results.BadRequest();
            var proposal = await FindProposal(db,user,request.ProposalRevision);
            var original = Details(proposal,clock.GetUtcNow());
            if (proposal is null || original is null)
                return Results.Problem(statusCode:409,title:"This date changed or is no longer available. Refresh Dates.");
            SharedVenue? picked = null;
            if (request.Kind == "picked")
            {
                try
                {
                    if (request.VenueToken is not { Length: > 0 and <= 8192 }) return Results.BadRequest();
                    picked = JsonSerializer.Deserialize<SharedVenue>(protection.CreateProtector("Overlap.OutingVenue.v1")
                        .ToTimeLimitedDataProtector().Unprotect(request.VenueToken));
                }
                catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException)
                { return Results.Problem(statusCode:400,title:"Search again before sharing your place."); }
                if (picked is null || picked.StartUtc != original.StartUtc || picked.Activity != original.Activity)
                    return Results.Problem(statusCode:400,title:"Choose a place for this date's activity and time.");
            }
            var replyJson = picked is null ? null : JsonSerializer.Serialize(picked);
            var revision = Guid.NewGuid().ToString("N");
            var caller = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var count = await db.Set<OutingUpdate>().Where(x => x.PairId == proposal.PairId &&
                x.SenderId == proposal.SenderId && x.Revision == request.ProposalRevision && x.CancelledBy == null &&
                db.PairMembers.Any(m => m.AccountId == caller && m.PairId == x.PairId))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReplyKind,request.Kind)
                    .SetProperty(x => x.ReplyVenueJson,replyJson).SetProperty(x => x.ReplyRevision,revision)
                    .SetProperty(x => x.ReplySeen,false).SetProperty(x => x.Seen,true));
            return count == 1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"This date changed. Refresh Dates.");
        }).RequireAuthorization().RequireRateLimiting("picks");
    }
}
