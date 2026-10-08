using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
namespace Overlap.Api.Auth;

public sealed class SharingGrant
{
    public required string PairId { get; set; }
    public required string SenderId { get; set; }
    public required string Category { get; set; }
    public string Status { get; set; } = "accepted";
}
public sealed record SharingCategory(string Category);

public static class PrivacyEndpoints
{
    private static readonly string[] Categories = ["profile", "presence", "picks", "books", "cooking"];
    private static string Id(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static Task<string?> Pair(AuthDbContext db, string id) => db.PairMembers.Where(x => x.AccountId == id).Select(x => x.PairId).SingleOrDefaultAsync();
    public static Task<bool> Allowed(AuthDbContext db, string? pair, string sender, string category) =>
        db.Set<SharingGrant>().AnyAsync(x => pair != null && x.PairId == pair && x.SenderId == sender && x.Category == category && x.Status == "accepted");
    public static async Task Request(AuthDbContext db, string pair, string sender, string category)
    {
        var existing = await db.Set<SharingGrant>().FindAsync(pair, sender, category);
        if (existing is null) db.Add(new SharingGrant { PairId = pair, SenderId = sender, Category = category });
    }
    public static void MapPrivacy(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/privacy").RequireAuthorization().RequireRateLimiting("auth");
        group.MapGet("", async (ClaimsPrincipal user, AuthDbContext db) =>
        {
            var id = Id(user); var pair = await Pair(db,id);
            var grants = await db.Set<SharingGrant>().AsNoTracking().Where(x => pair != null && x.PairId == pair).ToListAsync();
            var receivedPicks = await db.Set<Overlap.Api.Watch.SharedPick>().CountAsync(x => pair != null && x.PairId == pair && x.ProposedBy != id && x.IsShared && x.Status == "pending" && db.Set<SharingGrant>().Any(g => g.PairId == pair && g.SenderId == x.ProposedBy && g.Category == "picks" && g.Status == "accepted"));
            var outingUpdates = await db.Set<Overlap.Api.Outings.OutingUpdate>().AsNoTracking()
                .Where(x => pair != null && x.PairId == pair).ToListAsync();
            outingUpdates = outingUpdates.Where(x => {
                var venue = x.VenueJson == null ? null : System.Text.Json.JsonSerializer.Deserialize<Overlap.Api.Outings.SharedVenue>(x.VenueJson);
                return !x.OnMyWay && venue?.Activity != null && venue.StartUtc != null;
            }).ToList();
            var partnerId = await db.PairMembers.Where(x => pair != null && x.PairId == pair && x.AccountId != id).Select(x => x.AccountId).SingleOrDefaultAsync();
            var partnerName = partnerId is null ? null : await db.Users.Where(x => x.Id == partnerId).Select(x => x.DisplayName).SingleOrDefaultAsync();
            var picks = await db.Set<Overlap.Api.Watch.SharedPick>().AsNoTracking().Where(x => (x.ProposedBy == id && x.CollectionKey == "personal") ||
                (pair != null && x.PairId == pair && x.IsShared && db.Set<SharingGrant>().Any(g => g.PairId == pair && g.SenderId == x.ProposedBy && g.Category == "picks" && g.Status == "accepted"))).OrderByDescending(x => x.Id).ToListAsync();
            var recipes=await db.Set<Overlap.Api.Cooking.SavedRecipe>().AsNoTracking().Where(x=>pair!=null && x.PairId==pair && x.SharedBy!=id && x.ShareRevision!="").OrderByDescending(x=>x.Id).ToListAsync();
            var contests=await db.Set<Overlap.Api.Cooking.CookingContest>().AsNoTracking().Where(x=>pair!=null && x.PairId==pair).OrderBy(x=>x.StartUtc).ToListAsync();
            var quizInvitations=await db.Set<Overlap.Api.Quiz.QuizRound>().AsNoTracking().Where(x=>pair!=null && x.PairId==pair && x.ProposedBy!=id && x.Status=="pending").Select(x=>new{x.Id}).ToArrayAsync();
            var drawingInvitations=await db.Set<Overlap.Api.Drawing.DrawingRound>().AsNoTracking()
                .Where(x=>pair!=null && x.PairId==pair && x.CreatedBy!=id && !x.Cancelled && x.StartUtc==null && x.EndUtc==null && x.PausedSeconds==null)
                .Select(x=>new{x.Id,x.Prompt,x.Seconds,x.DrawingCount,x.DrawingNumber}).ToArrayAsync();
            return Results.Ok(new { quizInvitations, drawingInvitations, connected = pair != null, receivedPicks, partnerName,
                unreadContests=contests.Count(x=>x.ProposedBy==id ? x.SenderUnread : x.RecipientUnread),
                cookingContests=contests.Select(x=>new{x.Id,x.Status,x.Revision,redrawPending=x.RedrawRequestedBy!=null,redrawMine=x.RedrawRequestedBy==id,availableMinutes=x.Minutes,x.TimeZone,recipeTitle=x.RevealedUtc is null || x.RecipeContent is null ? null : System.Text.Json.JsonSerializer.Deserialize<Overlap.Api.Cooking.Recipe>(x.RecipeContent)!.Title,startUtc=DateTime.SpecifyKind(x.StartUtc,DateTimeKind.Utc),mine=x.ProposedBy==id,
                    unread=x.ProposedBy==id ? x.SenderUnread : x.RecipientUnread,
                    dismissed=(x.ProposedBy==id ? x.SenderDismissedRevision : x.RecipientDismissedRevision)==x.Revision}),
                unreadRecipes=recipes.Count(x=>!x.ShareSeen),
                recipeShares=recipes.Select(x=>new{x.Id,revision=x.ShareRevision,unread=!x.ShareSeen,dismissed=x.ShareDismissed,title=System.Text.Json.JsonSerializer.Deserialize<Overlap.Api.Cooking.Recipe>(x.Content)!.Title}),
                unreadOutings = outingUpdates.Count(x => x.SenderId == id ? !x.ReplySeen : !x.Seen),
                unreadPicks = picks.Count(x => x.IsShared && x.PairId == pair && (x.ProposedBy == id ? x.SenderHasNews : x.RecipientHasNews)),
                moviePicks = picks.Select(x => new { x.Id, x.Revision, dismissed = (x.ProposedBy == id ? x.SenderDismissedRevision : x.RecipientDismissedRevision) == x.Revision, mine = x.ProposedBy == id, x.Status,
                    unread = x.IsShared && x.PairId == pair && (x.ProposedBy == id ? x.SenderHasNews : x.RecipientHasNews),
                    shared = x.IsShared, title = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(x.Content) }),
                outingUpdates = outingUpdates.Select(x => new { dismissed = x.SenderId == id ? x.SenderDismissedRevision == (x.ReplyRevision == "" ? x.Revision : x.ReplyRevision) : x.RecipientDismissedRevision == x.Revision, mine = x.SenderId == id, x.OnMyWay, x.UpdatedUtc, x.Revision, unread = x.SenderId == id ? !x.ReplySeen : !x.Seen,
                    cancelled = x.CancelledBy != null, cancelledByMe = x.CancelledBy == id, x.CancellationReason, x.ReplyKind, x.ReplyRevision,
                    replyVenue = x.ReplyVenueJson == null ? null : System.Text.Json.JsonSerializer.Deserialize<Overlap.Api.Outings.SharedVenue>(x.ReplyVenueJson),
                    venue = x.VenueJson == null ? null : System.Text.Json.JsonSerializer.Deserialize<Overlap.Api.Outings.SharedVenue>(x.VenueJson) }),
                outgoing = grants.Where(x => x.SenderId == id).Select(x => new {x.Category,x.Status}),
                incoming = grants.Where(x => x.SenderId != id).Select(x => new {x.Category,x.Status}) });
        }).RequireRateLimiting("account-read");
        group.MapPost("/request", async (SharingCategory request, ClaimsPrincipal user, AuthDbContext db) =>
        {
            if (!Categories.Contains(request.Category)) return Results.BadRequest();
            var id = Id(user); var pair = await Pair(db,id);
            if (pair is null) return Results.Problem(statusCode:409,title:"Connect with your person before sharing.");
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Request(db,pair,id,request.Category);
            var grant = db.Set<SharingGrant>().Local.Single(x=>x.PairId==pair&&x.SenderId==id&&x.Category==request.Category);
            grant.Status = "accepted";
            if (request.Category == "profile") await db.Users.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SharePhoto,true).SetProperty(x=>x.ShareDisplayName,true));
            if (request.Category == "presence") await db.Users.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SharePresence,true));
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.NoContent();
        });
        // Older clients cannot change their partner's sharing preferences.
        group.MapPost("/respond", () => Results.Problem(statusCode: 410,
            title: "Sharing is now controlled by its owner. Refresh the app."));
        group.MapPost("/cancel", async (SharingCategory request, ClaimsPrincipal user, AuthDbContext db) =>
        {
            if (!Categories.Contains(request.Category)) return Results.BadRequest();
            var id = Id(user); var pair = await Pair(db,id);
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Set<SharingGrant>().Where(x=>pair!=null&&x.PairId==pair&&x.SenderId==id&&x.Category==request.Category)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"withdrawn"));
            if (request.Category=="profile") await db.Users.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SharePhoto,false).SetProperty(x=>x.ShareDisplayName,false));
            if (request.Category=="presence") await db.Users.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SharePresence,false));
            if (request.Category=="picks") await db.Set<Overlap.Api.Watch.SharedPick>().Where(x=>x.ProposedBy==id && x.CollectionKey=="personal").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsShared,false).SetProperty(x=>x.PairId,(string?)null).SetProperty(x=>x.Status,"pending"));
            await transaction.CommitAsync(); return Results.NoContent();
        });
    }
}
