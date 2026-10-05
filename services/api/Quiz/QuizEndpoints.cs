using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Quiz;
public sealed record CreateQuiz(string[]? CategoryIds,long? TemplateId=null);
public sealed record RemoveQuiz(string Revision);
public static class QuizEndpoints
{
    private static IQueryable<QuizRound> Visible(AuthDbContext db,string account)=>db.Set<QuizRound>().Where(x=>db.PairMembers.Any(m=>m.AccountId==account && m.PairId==x.PairId));
    public static void MapQuiz(this WebApplication app){
        var group=app.MapGroup("/api/auth/quiz").RequireAuthorization().RequireRateLimiting("quiz");
        group.MapGet("/catalog",()=>Results.Ok(QuizCatalog.Categories.Select(x=>new{x.Id,x.Title,x.Personal,count=x.Questions.Length})));
        group.MapGet("/rounds",async(ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var connected=await db.PairMembers.AnyAsync(x=>x.AccountId==account,ct);
            var rounds=await Visible(db,account).AsNoTracking().OrderByDescending(x=>x.CreatedUtc).Take(100).ToArrayAsync(ct);
            return Results.Ok(new{connected,rounds=rounds.Select(x=>new{x.Id,x.Title,x.Revision,x.Status,x.CurrentIndex,mine=x.ProposedBy==account,createdUtc=DateTime.SpecifyKind(x.CreatedUtc,DateTimeKind.Utc)})});
        });
        group.MapPost("/rounds",async(CreateQuiz request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var pair=await db.PairMembers.Where(x=>x.AccountId==account).Select(x=>x.PairId).SingleOrDefaultAsync(ct);
            if(pair is null)return Results.Problem(statusCode:409,title:"Connect with your person to play a quiz.");
            if(await db.Set<QuizRound>().CountAsync(x=>x.PairId==pair && (x.Status=="pending" || x.Status=="active"),ct)>=10)return Results.Problem(statusCode:409,title:"Finish or cancel an existing round before starting another.");
            QuizQuestion[] questions;var title="Guess my answer";
            try{
                if(request.TemplateId is {} templateId){
                    var template=await db.Set<CustomQuiz>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==templateId && x.AuthorId==account && x.PairId==pair,ct);
                    if(template is null)return Results.NotFound();title=template.Title;questions=JsonSerializer.Deserialize<QuizQuestion[]>(template.QuestionsJson)!;
                }else{
                var past=await db.Set<QuizRound>().AsNoTracking().Where(x=>x.PairId==pair).Select(x=>x.QuestionsJson).ToArrayAsync(ct);
                questions=QuizCatalog.Select(request.CategoryIds ?? [],past.SelectMany(x=>JsonSerializer.Deserialize<QuizQuestion[]>(x)!).Select(x=>x.Id));
                }
            }catch(ArgumentException e){return Results.Problem(statusCode:400,title:e.Message);}
            var round=new QuizRound{Title=title,PairId=pair,ProposedBy=account,QuestionsJson=JsonSerializer.Serialize(questions),CreatedUtc=clock.GetUtcNow().UtcDateTime};
            db.Add(round);await db.SaveChangesAsync(ct);return Results.Ok(new{round.Id});
        });
        group.MapGet("/rounds/{id:long}",async(long id,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var round=await Visible(db,account).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(round is null)return Results.NotFound();
            var answers=await db.Set<QuizAnswer>().AsNoTracking().Where(x=>x.RoundId==id).ToArrayAsync(ct);return Results.Ok(QuizPlay.Detail(round,answers,account));
        });
        group.MapPost("/rounds/{id:long}/answer",async(long id,QuizSubmission request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var round=await Visible(db,account).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            return round is null ? Results.NotFound() : await QuizPlay.Answer(round,account,request,db,clock,ct);
        });
        group.MapPost("/rounds/{id:long}/action",async(long id,QuizAction request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var round=await Visible(db,account).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            return round is null ? Results.NotFound() : await QuizPlay.Act(round,account,request,db,ct);
        });
        group.MapPost("/rounds/{id:long}/remove",async(long id,RemoveQuiz request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var round=await Visible(db,account).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(round is null)return Results.NotFound();
            if(round.Status is "active" or "pending")return Results.Problem(statusCode:409,title:"Cancel or finish this round before removing it.");
            var removed=await db.Set<QuizRound>().Where(x=>x.Id==id && x.Revision==request.Revision && x.Status==round.Status).ExecuteDeleteAsync(ct);
            return removed==1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"This round has changed. Refresh and try again.");
        });
    }
}
