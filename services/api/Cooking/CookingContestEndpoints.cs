using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
using Overlap.Api.Outings;
namespace Overlap.Api.Cooking;

public sealed class CookingContest
{
    public long Id { get; set; }
    public required string PairId { get; set; }
    public required string ProposedBy { get; set; }
    public DateTime StartUtc { get; set; }
    public required string TimeZone { get; set; }
    public int Minutes { get; set; }
    public string? RedrawRequestedBy { get; set; }
    public string? RecipeContent { get; set; }
    public DateTime? RevealedUtc { get; set; }
    public string? SenderPreferences { get; set; }
    public string? RecipientPreferences { get; set; }
    public string Status { get; set; } = "pending";
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
    public bool SenderUnread { get; set; }
    public bool RecipientUnread { get; set; } = true;
    public string SenderDismissedRevision { get; set; } = "";
    public string RecipientDismissedRevision { get; set; } = "";
}
public sealed record ProposeCookingContest(string? LocalStart, string TimeZone, int AvailableMinutes);
public sealed record RedrawContestRequest(string Revision,string Action);
public sealed record RespondCookingContest(string Revision, string Action);

public static class CookingContestEndpoints
{
    public static void MapCookingContests(this WebApplication app)
    {
        var group=app.MapGroup("/api/auth/cooking/contests").RequireAuthorization().RequireRateLimiting("picks");
        group.MapGet("/{id:long}",async(long id,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var contest=await db.Set<CookingContest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && db.PairMembers.Any(m=>m.AccountId==account && m.PairId==x.PairId),ct);
            if(contest is null)return Results.NotFound();
            var preferences=await FoodPreferencesEndpoints.ForPair(db,contest.PairId,ct);
            var own=preferences.SingleOrDefault(x=>x.AccountId==account);var partner=preferences.SingleOrDefault(x=>x.AccountId!=account);
            return Results.Ok(new{contest.Revision,redrawPending=contest.RedrawRequestedBy!=null,redrawMine=contest.RedrawRequestedBy==account,serverUtc=clock.GetUtcNow(),partnerConfirmed=partner!=null,
                ownPreferences=FoodPreferencesEndpoints.Read(own),
                partnerPreferences=partner?.ShareWithPartner==true && partner.SharedPairId==contest.PairId ? FoodPreferencesEndpoints.Read(partner) : null,
                revealedUtc=contest.RevealedUtc is {} revealed ? DateTime.SpecifyKind(revealed,DateTimeKind.Utc) : (DateTime?)null,
                recipe=contest.RevealedUtc is null || contest.RecipeContent is null ? null : System.Text.Json.JsonSerializer.Deserialize<Recipe>(contest.RecipeContent)});
        });
        group.MapPost("/{id:long}/preferences",()=>Results.Problem(statusCode:410,title:"Manage your food preferences in Cooking."));
        // Old clients must not reveal or choose a contest recipe early.
        group.MapPost("/{id:long}/recipe",()=>Results.Problem(statusCode:410,title:"Contest recipes are now revealed by shuffling at the scheduled start."));
        group.MapPost("/{id:long}/shuffle",async(long id,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair=await db.PairMembers.Where(x=>x.AccountId==account).Select(x=>x.PairId).SingleOrDefaultAsync(ct);
            var contest=await db.Set<CookingContest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && pair!=null && x.PairId==pair,ct);
            if(contest is null)return Results.NotFound();
            var now=clock.GetUtcNow().UtcDateTime;
            if(contest.Status!="accepted" || now<contest.StartUtc)return Results.Problem(statusCode:409,title:"Shuffle unlocks when your accepted contest starts.");
            if(contest.RevealedUtc is not null)return Results.NoContent();
            return await ContestDraw.Pick(contest,account,false,db,clock,ct);
        });
        group.MapPost("/{id:long}/redraw",async(long id,RedrawContestRequest request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>{
            if(request.Revision is null || request.Revision.Length!=32 || request.Action is not ("request" or "accept" or "decline" or "withdraw"))return Results.BadRequest();
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var contest=await db.Set<CookingContest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && db.PairMembers.Any(m=>m.AccountId==account && m.PairId==x.PairId),ct);
            if(contest is null)return Results.NotFound();
            if(contest.Status!="accepted" || contest.RevealedUtc is null || contest.Revision!=request.Revision)return Results.Problem(statusCode:409,title:"This draw has changed. Refresh and try again.");
            if(await db.Set<CookingMemory>().AnyAsync(x=>x.ContestId==id,ct))return Results.Problem(statusCode:409,title:"This recipe is already in your album. Start a new contest for another recipe.");
            if(request.Action=="accept"){
                if(contest.RedrawRequestedBy is null || contest.RedrawRequestedBy==account)return Results.Problem(statusCode:403,title:"Only your partner can accept your redraw request.");
                return await ContestDraw.Pick(contest,account,true,db,clock,ct);
            }
            if(request.Action=="request" ? contest.RedrawRequestedBy!=null : contest.RedrawRequestedBy==null || (request.Action=="withdraw" ? contest.RedrawRequestedBy!=account : contest.RedrawRequestedBy==account))return Results.Problem(statusCode:409,title:"This redraw request has changed.");
            var requester=request.Action=="request" ? account : null;var revision=Guid.NewGuid().ToString("N");
            var changed=await db.Set<CookingContest>().Where(x=>x.Id==id && x.Revision==request.Revision && x.PairId==contest.PairId && !db.Set<CookingMemory>().Any(m=>m.ContestId==x.Id))
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RedrawRequestedBy,requester).SetProperty(x=>x.Revision,revision).SetProperty(x=>x.SenderUnread,contest.ProposedBy!=account).SetProperty(x=>x.RecipientUnread,contest.ProposedBy==account),ct);
            return changed==1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"This contest has changed. Refresh and try again.");
        });
        group.MapPost("", async (ProposeCookingContest request, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair=await db.PairMembers.Where(x=>x.AccountId==account).Select(x=>x.PairId).SingleOrDefaultAsync(ct);
            if(pair is null) return Results.Problem(statusCode:409,title:"Connect with your person to invite them to cook.");
            if(string.IsNullOrWhiteSpace(request.LocalStart)) return Results.Problem(statusCode:400,title:"Choose a date and time for your contest.");
            OutingWindow window;
            try {window=OutingTime.Resolve(new(request.LocalStart,request.TimeZone,request.TimeZone,request.AvailableMinutes),clock.GetUtcNow());}
            catch(ArgumentException e) {return Results.Problem(statusCode:400,title:e.Message);}
            if(await db.Set<CookingContest>().CountAsync(x=>x.PairId==pair && (x.Status=="pending" || x.Status=="accepted") && x.StartUtc>clock.GetUtcNow().UtcDateTime,ct)>=10)
                return Results.Problem(statusCode:409,title:"You already have ten upcoming contests. Cancel an invitation before adding another.");
            var contest=new CookingContest{PairId=pair,ProposedBy=account,StartUtc=window.StartUtc.UtcDateTime,TimeZone=request.TimeZone,Minutes=request.AvailableMinutes};
            db.Add(contest);await db.SaveChangesAsync(ct);
            return Results.Ok(new{contest.Id});
        });
        group.MapPost("/{id:long}/respond", async (long id,RespondCookingContest request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct) =>
        {
            if(request.Revision is null || request.Revision.Length!=32 || request.Action is not ("accept" or "decline" or "cancel")) return Results.BadRequest();
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair=await db.PairMembers.Where(x=>x.AccountId==account).Select(x=>x.PairId).SingleOrDefaultAsync(ct);
            var contest=await db.Set<CookingContest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && pair!=null && x.PairId==pair,ct);
            if(contest is null)return Results.NotFound();
            var cancel=request.Action=="cancel";
            if(!cancel && contest.ProposedBy==account) return Results.Problem(statusCode:403,title:"Only your partner can respond to this invitation.");
            if(contest.Revision!=request.Revision || (cancel ? contest.Status is not ("pending" or "accepted") : contest.Status!="pending") || contest.StartUtc<=clock.GetUtcNow().UtcDateTime)
                return Results.Problem(statusCode:409,title:"This contest has changed or its start time has passed. Refresh and try again.");
            var revision=Guid.NewGuid().ToString("N");
            var status=request.Action=="accept" ? "accepted" : request.Action=="decline" ? "declined" : "cancelled";
            var changed=await db.Set<CookingContest>().Where(x=>x.Id==id && x.PairId==pair && x.Revision==request.Revision && x.Status==contest.Status)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,status).SetProperty(x=>x.Revision,revision)
                    .SetProperty(x=>x.SenderUnread,contest.ProposedBy!=account).SetProperty(x=>x.RecipientUnread,contest.ProposedBy==account),ct);
            return changed==1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"This contest has changed. Refresh and try again.");
        });
    }
    private static ContestPreferences? ReadPreferences(string? json)=>json is null ? null : System.Text.Json.JsonSerializer.Deserialize<ContestPreferences>(json);
}
