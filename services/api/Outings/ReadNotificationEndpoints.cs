using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
using Overlap.Api.Watch;
namespace Overlap.Api.Outings;

public sealed record ReadNotification(string Kind, string Revision, long? Id, bool Dismiss = false);
public static class ReadNotificationEndpoints
{
    public static void MapReadNotifications(this WebApplication app)
    {
        app.MapPost("/api/auth/privacy/read", async (ReadNotification request, ClaimsPrincipal user, AuthDbContext db) =>
        {
            if (request.Revision is null || request.Revision.Length > 32 || request.Kind is not ("outing" or "outing-reply" or "pick")) return Results.BadRequest();
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair = await db.PairMembers.Where(x => x.AccountId == id).Select(x => x.PairId).SingleOrDefaultAsync();
            if (pair is null) return Results.NoContent();
            if (request.Kind == "outing")
                await db.Set<OutingUpdate>().Where(x => x.PairId == pair && x.SenderId != id && x.Revision == request.Revision)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Seen, true).SetProperty(x => x.RecipientDismissedRevision, x => request.Dismiss ? request.Revision : x.RecipientDismissedRevision));
            else if (request.Kind == "outing-reply")
                await db.Set<OutingUpdate>().Where(x => x.PairId == pair && x.SenderId == id && (x.ReplyRevision == request.Revision || x.ReplyRevision == "" && x.Revision == request.Revision))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReplySeen, true).SetProperty(x => x.SenderDismissedRevision, x => request.Dismiss ? request.Revision : x.SenderDismissedRevision));
            else
            {
                await db.Set<SharedPick>().Where(x => x.PairId == pair && x.Id == request.Id && x.Revision == request.Revision && x.ProposedBy == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.SenderHasNews, false).SetProperty(x => x.SenderDismissedRevision, x => request.Dismiss ? request.Revision : x.SenderDismissedRevision));
                await db.Set<SharedPick>().Where(x => x.PairId == pair && x.Id == request.Id && x.Revision == request.Revision && x.ProposedBy != id && x.IsShared &&
                    db.Set<SharingGrant>().Any(g => g.PairId == pair && g.SenderId == x.ProposedBy && g.Category == "picks" && g.Status == "accepted"))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecipientHasNews, false).SetProperty(x => x.RecipientDismissedRevision, x => request.Dismiss ? request.Revision : x.RecipientDismissedRevision));
            }
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("auth");
    }
}
