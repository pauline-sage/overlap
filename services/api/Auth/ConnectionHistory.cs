using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Overlap.Api.Auth;

public sealed class ConnectionHistory
{
    public required string PairId { get; set; }
    public DateTime? StartedUtc { get; set; }
    public bool Closed { get; set; }
    public DateTime? DisconnectedUtc { get; set; }
    public DateTime? ExportExpiresUtc { get; set; }
}

public sealed record EndConnection(bool AcceptTerms);

public static partial class ConnectionHistoryEndpoints
{
    private static DateTime? Utc(DateTime? value)=>value is null ? null : DateTime.SpecifyKind(value.Value,DateTimeKind.Utc);
    private static string AccountId(ClaimsPrincipal user)=>user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    internal static async Task<bool> Creator(AuthDbContext db, IConfiguration config, string account)
    {
        var email=config["ConnectionHistory:CreatorEmail"]?.Trim();
        if(string.IsNullOrEmpty(email))return false;
        return await db.Users.AnyAsync(x=>x.Id==account && x.EmailConfirmed && x.NormalizedEmail==email.ToUpperInvariant());
    }
    private static IQueryable<ConnectionHistory> Own(AuthDbContext db,string account)=>db.Set<ConnectionHistory>()
        .Where(x=>x.Closed && db.Set<AlbumAccess>().Any(m=>m.PairId==x.PairId && m.AccountId==account));

    public static async Task<IResult> Leave(EndConnection request, ClaimsPrincipal principal, AuthDbContext db, TimeProvider clock)
    {
        if(!request.AcceptTerms)return Results.Problem(statusCode:400,title:"Accept the connection ending conditions first.");
        var account=AccountId(principal);
        await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var pair=await db.PairMembers.Where(x=>x.AccountId==account).Select(x=>x.PairId).SingleOrDefaultAsync();
        if(pair is null)return Results.NoContent();
        var now=clock.GetUtcNow().UtcDateTime;
        var history=await db.Set<ConnectionHistory>().FindAsync(pair);
        if(history is null){history=new ConnectionHistory{PairId=pair};db.Add(history);}
        history.Closed=true;history.DisconnectedUtc=now;history.ExportExpiresUtc=now.AddDays(30);
        // Preserve connection records, but stop pending workflows and all active membership.
        await db.Set<Overlap.Api.Watch.SharedPick>().Where(x=>x.PairId==pair)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CollectionKey,pair).SetProperty(x=>x.SenderHasNews,false).SetProperty(x=>x.RecipientHasNews,false));
        await db.Set<Overlap.Api.Cooking.CookingContest>().Where(x=>x.PairId==pair && (x.Status=="pending" || x.Status=="accepted"))
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"cancelled").SetProperty(x=>x.SenderUnread,false).SetProperty(x=>x.RecipientUnread,false));
        await db.Set<Overlap.Api.Quiz.QuizRound>().Where(x=>x.PairId==pair && (x.Status=="pending" || x.Status=="active"))
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"cancelled"));
        await db.Set<Overlap.Api.Drawing.DrawingRound>().Where(x=>x.PairId==pair && !x.Cancelled && (x.EndUtc==null || x.EndUtc>now || x.PausedSeconds!=null))
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Cancelled,true));
        await db.PairMembers.Where(x=>x.PairId==pair).ExecuteDeleteAsync();
        await db.SaveChangesAsync();await transaction.CommitAsync();
        return Results.NoContent();
    }

    public static void MapConnectionHistory(this WebApplication app)
    {
        var group=app.MapGroup("/api/auth/connection-history").RequireAuthorization().RequireRateLimiting("auth");
        group.MapGet("",async(ClaimsPrincipal user,AuthDbContext db,IConfiguration config,TimeProvider clock)=>{
            var account=AccountId(user);var creator=await Creator(db,config,account);var now=clock.GetUtcNow().UtcDateTime;
            var rows=await Own(db,account).AsNoTracking().Where(x=>creator || x.ExportExpiresUtc>now).OrderByDescending(x=>x.DisconnectedUtc).ToArrayAsync();
            return Results.Ok(new{creator,connections=rows.Select(x=>new{x.PairId,startedUtc=Utc(x.StartedUtc),disconnectedUtc=Utc(x.DisconnectedUtc),exportExpiresUtc=Utc(x.ExportExpiresUtc)})});
        });
        group.MapGet("/{pair}/archive",async(string pair,ClaimsPrincipal user,AuthDbContext db,IConfiguration config)=>{
            var account=AccountId(user);
            if(!await Creator(db,config,account) || !await Own(db,account).AnyAsync(x=>x.PairId==pair))return Results.NotFound();
            return Results.Ok(await Snapshot(db,pair,account));
        });
        group.MapGet("/{pair}/export",Export);
        group.MapGet("/{pair}/photo/{kind}/{id:long}/{owner}",async(string pair,string kind,long id,string owner,ClaimsPrincipal user,AuthDbContext db,IConfiguration config)=>{
            var account=AccountId(user);
            if(!await Creator(db,config,account) || !await Own(db,account).AnyAsync(x=>x.PairId==pair))return Results.NotFound();
            byte[]? png=null;
            if(kind=="drawing")png=await db.Set<Overlap.Api.Drawing.DrawingPhoto>().Where(p=>p.RoundId==id && p.AccountId==owner && db.Set<Overlap.Api.Drawing.DrawingRound>().Any(r=>r.Id==id && r.PairId==pair)).Select(p=>p.Png).SingleOrDefaultAsync();
            if(kind=="cooking")png=await db.Set<Overlap.Api.Cooking.CookingDishPhoto>().Where(p=>p.ContestId==id && p.AccountId==owner && db.Set<Overlap.Api.Cooking.CookingContest>().Any(r=>r.Id==id && r.PairId==pair)).Select(p=>p.Png).SingleOrDefaultAsync();
            return png is null ? Results.NotFound() : Results.File(png,"image/png");
        });
    }
}
