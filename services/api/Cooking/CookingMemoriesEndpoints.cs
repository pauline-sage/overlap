using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Cooking;

public sealed class CookingMemory
{
    public long ContestId { get; set; }
    public required string RecipeContent { get; set; }
    public DateTime CreatedUtc { get; set; }
}
public sealed class CookingDishPhoto
{
    public long ContestId { get; set; }
    public required string AccountId { get; set; }
    public required byte[] Png { get; set; }
    public DateTime UploadedUtc { get; set; }
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed record SaveDishPhoto(string Revision, string? Pixels);
public static class CookingMemoriesEndpoints
{
    public const int PhotoSize = 512;
    private static IQueryable<CookingContest> Authorized(AuthDbContext db, string account) => db.Set<CookingContest>().Where(x=>db.PairMembers.Any(m=>m.AccountId==account && m.PairId==x.PairId));
    private static object Entry(CookingContest contest, CookingMemory? memory, CookingDishPhoto[] photos, string account) => new {
        contestId=contest.Id, revision=contest.Revision,
        startUtc=DateTime.SpecifyKind(contest.StartUtc,DateTimeKind.Utc),
        completed=photos.Length==2, archived=memory is not null,
        recipe=JsonSerializer.Deserialize<Recipe>(memory?.RecipeContent ?? contest.RecipeContent!),
        photos=photos.Select(p=>new {mine=p.AccountId==account,accountId=p.AccountId,revision=p.Revision,uploadedUtc=DateTime.SpecifyKind(p.UploadedUtc,DateTimeKind.Utc)})
    };
    public static void MapCookingMemories(this WebApplication app)
    {
        var group=app.MapGroup("/api/auth/cooking/memories").RequireAuthorization().RequireRateLimiting("picks");
        group.MapGet("",async(ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var contests=await Authorized(db,account).AsNoTracking().Where(x=>db.Set<CookingMemory>().Any(m=>m.ContestId==x.Id)).OrderByDescending(x=>x.StartUtc).Take(100).ToArrayAsync(ct);
            var ids=contests.Select(x=>x.Id).ToArray();
            var memories=await db.Set<CookingMemory>().AsNoTracking().Where(x=>ids.Contains(x.ContestId)).ToArrayAsync(ct);
            // Album responses contain photo metadata only, never pixel bytes.
            var photos=await db.Set<CookingDishPhoto>().AsNoTracking().Where(x=>ids.Contains(x.ContestId)).Select(x=>new CookingDishPhoto{ContestId=x.ContestId,AccountId=x.AccountId,Png=Array.Empty<byte>(),UploadedUtc=x.UploadedUtc,Revision=x.Revision}).ToArrayAsync(ct);
            return Results.Ok(contests.Select(x=>Entry(x,memories.Single(m=>m.ContestId==x.Id),photos.Where(p=>p.ContestId==x.Id).ToArray(),account)));
        });
        group.MapGet("/{id:long}",async(long id,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var contest=await Authorized(db,account).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(contest is null)return Results.NotFound();
            if(contest.Status!="accepted" || contest.RevealedUtc is null || contest.RecipeContent is null)return Results.Problem(statusCode:409,title:"Finish cooking after your shared recipe is revealed.");
            var memory=await db.Set<CookingMemory>().AsNoTracking().SingleOrDefaultAsync(x=>x.ContestId==id,ct);
            var photos=await db.Set<CookingDishPhoto>().AsNoTracking().Where(x=>x.ContestId==id).Select(x=>new CookingDishPhoto{ContestId=x.ContestId,AccountId=x.AccountId,Png=Array.Empty<byte>(),UploadedUtc=x.UploadedUtc,Revision=x.Revision}).ToArrayAsync(ct);
            return Results.Ok(Entry(contest,memory,photos,account));
        });
        group.MapGet("/{id:long}/photos/{photoAccount}",async(long id,string photoAccount,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if(!await Authorized(db,account).AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            var png=await db.Set<CookingDishPhoto>().Where(x=>x.ContestId==id && x.AccountId==photoAccount).Select(x=>x.Png).SingleOrDefaultAsync(ct);
            return png is null ? Results.NotFound() : Results.File(png,"image/png");
        });
        group.MapPost("/{id:long}/photo",async(long id,SaveDishPhoto request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>{
            if(request.Revision is null || request.Revision.Length!=32)return Results.BadRequest();
            byte[]? png=null;
            if(request.Pixels is not null){
                if(request.Pixels.Length!=PhotoSize*PhotoSize*4)return Results.Problem(statusCode:400,title:"Choose a supported dish photo.");
                try{png=AvatarImage.EncodeSquare(Convert.FromBase64String(request.Pixels),PhotoSize);}catch(ArgumentException){return Results.BadRequest();}catch(FormatException){return Results.BadRequest();}
            }
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var contest=await Authorized(db,account).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(contest is null)return Results.NotFound();
            if(contest.Status!="accepted" || contest.RevealedUtc is null || contest.RecipeContent is null)return Results.Problem(statusCode:409,title:"Reveal your recipe before adding a finished-dish photo.");
            await using var transaction=await db.Database.BeginTransactionAsync(ct);
            var revision=Guid.NewGuid().ToString("N");
            var changed=await db.Set<CookingContest>().Where(x=>x.Id==id && x.Revision==request.Revision && x.Status=="accepted" && x.RedrawRequestedBy==null)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revision,revision).SetProperty(x=>x.SenderUnread,contest.ProposedBy!=account).SetProperty(x=>x.RecipientUnread,contest.ProposedBy==account),ct);
            if(changed==0)return Results.Problem(statusCode:409,title:"This contest has changed or a redraw is pending. Refresh and try again.");
            var photo=await db.Set<CookingDishPhoto>().SingleOrDefaultAsync(x=>x.ContestId==id && x.AccountId==account,ct);
            if(png is null){if(photo is not null)db.Remove(photo);}
            else{
                if(!await db.Set<CookingMemory>().AnyAsync(x=>x.ContestId==id,ct))db.Add(new CookingMemory{ContestId=id,RecipeContent=contest.RecipeContent,CreatedUtc=clock.GetUtcNow().UtcDateTime});
                if(photo is null)db.Add(new CookingDishPhoto{ContestId=id,AccountId=account,Png=png,UploadedUtc=clock.GetUtcNow().UtcDateTime});
                else{photo.Png=png;photo.UploadedUtc=clock.GetUtcNow().UtcDateTime;photo.Revision=Guid.NewGuid().ToString("N");}
            }
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);return Results.NoContent();
        });
    }
}
