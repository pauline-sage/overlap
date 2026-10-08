using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Books;
using Overlap.Api.Cooking;
using Overlap.Api.Drawing;
using Overlap.Api.Outings;
using Overlap.Api.Quiz;
using Overlap.Api.Schedule;
using Overlap.Api.Watch;

namespace Overlap.Api.Auth;

public static partial class ConnectionHistoryEndpoints
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private static JsonElement Content(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
    internal static async Task<object> Snapshot(AuthDbContext db,string pair,string account)
    {
        var drawings=await db.Set<DrawingRound>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var drawingPhotos=await db.Set<DrawingPhoto>().AsNoTracking().Where(p=>db.Set<DrawingRound>().Any(r=>r.Id==p.RoundId && r.PairId==pair))
            .Select(p=>new{p.RoundId,p.AccountId,p.UploadedUtc,p.OwnerRating,p.PartnerRating}).ToArrayAsync();
        var contests=await db.Set<CookingContest>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var memories=await db.Set<CookingMemory>().AsNoTracking().Where(m=>db.Set<CookingContest>().Any(c=>c.Id==m.ContestId && c.PairId==pair)).ToArrayAsync();
        var cookingPhotos=await db.Set<CookingDishPhoto>().AsNoTracking().Where(p=>db.Set<CookingContest>().Any(c=>c.Id==p.ContestId && c.PairId==pair))
            .Select(p=>new{p.ContestId,p.AccountId,p.UploadedUtc}).ToArrayAsync();
        var rounds=await db.Set<QuizRound>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var answers=await db.Set<QuizAnswer>().AsNoTracking().Where(a=>db.Set<QuizRound>().Any(r=>r.Id==a.RoundId && r.PairId==pair)).ToArrayAsync();
        var books=await db.Set<ReadingEntry>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var ratings=await db.Set<BookRating>().AsNoTracking().Where(r=>db.Set<ReadingEntry>().Any(e=>e.Id==r.EntryId && e.PairId==pair)).ToArrayAsync();
        var recipes=await db.Set<SavedRecipe>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var experiences=await db.Set<RecipeExperience>().AsNoTracking().Where(r=>db.Set<SavedRecipe>().Any(e=>e.Id==r.RecipeId && e.PairId==pair) && (r.AccountId==account || db.Set<SharingGrant>().Any(g=>g.PairId==pair && g.SenderId==r.AccountId && g.Category=="cooking" && g.Status=="accepted"))).ToArrayAsync();
        var picks=await db.Set<SharedPick>().AsNoTracking().Where(x=>x.PairId==pair && x.IsShared).ToArrayAsync();
        var watched=await db.Set<WatchedTitle>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var outings=await db.Set<OutingUpdate>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var places=await db.Set<SavedPlace>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var dates=await db.Set<CalendarEntry>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync();
        var templates=await db.Set<CustomQuiz>().AsNoTracking().Where(x=>x.PairId==pair && x.AuthorId==account).ToArrayAsync();
        return new{
            version=1,connection=await db.Set<ConnectionHistory>().AsNoTracking().Where(x=>x.PairId==pair).Select(x=>new{x.PairId,startedUtc=Utc(x.StartedUtc),disconnectedUtc=Utc(x.DisconnectedUtc),exportExpiresUtc=Utc(x.ExportExpiresUtc)}).SingleAsync(),
            drawings=drawings.Select(x=>new{x.Id,title=x.Prompt,createdUtc=Utc(x.CreatedUtc),startUtc=Utc(x.StartUtc),x.Seconds,x.DrawingNumber,x.DrawingCount,x.Cancelled,
                photos=drawingPhotos.Where(p=>p.RoundId==x.Id).Select(p=>new{p.AccountId,mine=p.AccountId==account,uploadedUtc=Utc(p.UploadedUtc),p.OwnerRating,p.PartnerRating,file=$"pictures/drawing-{x.Id}-{p.AccountId}.png"})}),
            cooking=contests.Select(x=>new{x.Id,x.Status,startUtc=Utc(x.StartUtc),x.Minutes,recipe=memories.FirstOrDefault(m=>m.ContestId==x.Id)?.RecipeContent is {} memory ? Content(memory) : x.RevealedUtc!=null && x.RecipeContent!=null ? Content(x.RecipeContent) : (JsonElement?)null,
                photos=cookingPhotos.Where(p=>p.ContestId==x.Id).Select(p=>new{p.AccountId,mine=p.AccountId==account,uploadedUtc=Utc(p.UploadedUtc),file=$"pictures/cooking-{x.Id}-{p.AccountId}.png"})}),
            books=books.Select(x=>new{x.Id,book=Content(x.Content),x.Status,x.Scope,updatedUtc=Utc(x.UpdatedUtc),ratings=ratings.Where(r=>r.EntryId==x.Id).Select(r=>new{mine=r.AccountId==account,r.Stars})}),
            recipes=recipes.Select(x=>new{x.Id,recipe=Content(x.Content),addedUtc=Utc(x.AddedUtc),experiences=experiences.Where(r=>r.RecipeId==x.Id).Select(r=>new{mine=r.AccountId==account,madeUtc=Utc(r.MadeUtc),r.Stars})}),
            watch=picks.Select(x=>new{x.Id,title=Content(x.Content),x.Status,createdUtc=Utc(x.CreatedUtc)}),
            watched=watched.Select(x=>new{x.Id,title=Content(x.Content),addedUtc=Utc(x.AddedUtc)}),
            outings=outings.Select(x=>new{updatedUtc=Utc(x.UpdatedUtc),venue=x.VenueJson is null ? (JsonElement?)null : Content(x.VenueJson),x.ReplyKind,replyVenue=x.ReplyVenueJson is null ? (JsonElement?)null : Content(x.ReplyVenueJson),x.CancellationReason,cancelled=x.CancelledBy!=null}),
            places=places.Select(x=>new{x.Id,x.Name,x.Location,x.Link,x.Note,createdUtc=Utc(x.CreatedUtc)}),
            dates=dates.Select(x=>new{x.Id,x.Title,x.Note,x.TimeZone,x.LocalStart,x.LocalEnd,startUtc=Utc(x.StartUtc),endUtc=Utc(x.EndUtc),x.AllDay,x.Yearly}),
            quizzes=rounds.Select(r=>QuizPlay.Detail(r,answers.Where(a=>a.RoundId==r.Id).ToArray(),account)),
            customQuizzes=templates.Select(x=>new{x.Id,x.Title,questions=Content(x.QuestionsJson),updatedUtc=Utc(x.UpdatedUtc)})
        };
    }

    private static async Task<IResult> Export(string pair,ClaimsPrincipal user,AuthDbContext db,IConfiguration config,TimeProvider clock,HttpContext context)
    {
        var account=AccountId(user);var creator=await Creator(db,config,account);var now=clock.GetUtcNow().UtcDateTime;
        if(!await Own(db,account).AnyAsync(x=>x.PairId==pair && (creator || x.ExportExpiresUtc>now)))return Results.NotFound();
        var snapshot=await Snapshot(db,pair,account);
        // .NET 9 ZipArchive writes its central directory synchronously on disposal.
        // Permit that only for this streamed download, rather than buffering all photos.
        var body=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
        if(body is not null)body.AllowSynchronousIO=true;
        // Write each photo directly to the response archive; no sensitive temporary files.
        return Results.Stream(async stream=>{
            using var zip=new ZipArchive(stream,ZipArchiveMode.Create,leaveOpen:true);
            await using(var json=zip.CreateEntry("connection.json").Open())await JsonSerializer.SerializeAsync(json,snapshot,Json);
            await foreach(var photo in db.Set<DrawingPhoto>().AsNoTracking().Where(p=>db.Set<DrawingRound>().Any(r=>r.Id==p.RoundId && r.PairId==pair)).AsAsyncEnumerable()){
                await using var file=zip.CreateEntry($"pictures/drawing-{photo.RoundId}-{photo.AccountId}.png").Open();await file.WriteAsync(photo.Png);
            }
            await foreach(var photo in db.Set<CookingDishPhoto>().AsNoTracking().Where(p=>db.Set<CookingContest>().Any(r=>r.Id==p.ContestId && r.PairId==pair)).AsAsyncEnumerable()){
                await using var file=zip.CreateEntry($"pictures/cooking-{photo.ContestId}-{photo.AccountId}.png").Open();await file.WriteAsync(photo.Png);
            }
        },"application/zip",$"overlap-connection-{pair}.zip");
    }
}
