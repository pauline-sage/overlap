using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Quiz;
public sealed class CustomQuiz
{
    public long Id {get;set;}
    public required string PairId {get;set;}
    public required string AuthorId {get;set;}
    public required string Title {get;set;}
    public required string QuestionsJson {get;set;}
    public string Revision {get;set;}=Guid.NewGuid().ToString("N");
    public DateTime UpdatedUtc {get;set;}
}
public sealed record CustomQuestion(string? Prompt,string[]? Choices);
public sealed record SaveCustomQuiz(string? Title,CustomQuestion[]? Questions,string? Revision);
public static class CustomQuizEndpoints
{
    public static QuizQuestion[] Validate(SaveCustomQuiz request){
        if(string.IsNullOrWhiteSpace(request.Title) || request.Title.Length>80 || request.Questions is null || request.Questions.Length!=5)throw new ArgumentException("Give your quiz a name and exactly five questions.");
        return request.Questions.Select((q,index)=>{
            if(q is null || string.IsNullOrWhiteSpace(q.Prompt) || q.Prompt.Length>300 || q.Choices is null || q.Choices.Length!=4 || q.Choices.Any(c=>string.IsNullOrWhiteSpace(c) || c.Length>100) || q.Choices.Select(c=>c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4)throw new ArgumentException("Each question needs a prompt and four different choices.");
            return new QuizQuestion("custom-"+index,"custom",q.Prompt.Trim(),q.Choices.Select(c=>c.Trim()).ToArray());
        }).ToArray();
    }
    private static IQueryable<CustomQuiz> Own(AuthDbContext db,string account)=>db.Set<CustomQuiz>().Where(x=>x.AuthorId==account && db.PairMembers.Any(m=>m.AccountId==account && m.PairId==x.PairId));
    public static void MapCustomQuizzes(this WebApplication app){
        var group=app.MapGroup("/api/auth/quiz/templates").RequireAuthorization().RequireRateLimiting("quiz");
        group.MapGet("",async(ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var rows=await Own(db,account).AsNoTracking().OrderByDescending(x=>x.UpdatedUtc).ToArrayAsync(ct);
            return Results.Ok(rows.Select(x=>new{x.Id,x.Title,x.Revision,questions=JsonSerializer.Deserialize<QuizQuestion[]>(x.QuestionsJson)}));
        });
        async Task<IResult> Save(long? id,SaveCustomQuiz request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct){
            QuizQuestion[] questions;try{questions=Validate(request);}catch(ArgumentException e){return Results.Problem(statusCode:400,title:e.Message);}
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var pair=await db.PairMembers.Where(m=>m.AccountId==account).Select(m=>m.PairId).SingleOrDefaultAsync(ct);
            if(pair is null)return Results.Problem(statusCode:409,title:"Connect with your person to create a shared quiz.");
            var json=JsonSerializer.Serialize(questions);var title=request.Title!.Trim();var revision=Guid.NewGuid().ToString("N");var now=clock.GetUtcNow().UtcDateTime;
            if(id is {} existing){
                if(!await Own(db,account).AnyAsync(x=>x.Id==existing,ct))return Results.NotFound();
                var changed=await Own(db,account).Where(x=>x.Id==existing && x.Revision==request.Revision).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Title,title).SetProperty(x=>x.QuestionsJson,json).SetProperty(x=>x.Revision,revision).SetProperty(x=>x.UpdatedUtc,now),ct);
                return changed==1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"Your quiz has changed. Reopen it before saving.");
            }
            if(await Own(db,account).CountAsync(ct)>=100)return Results.Problem(statusCode:409,title:"Remove a custom quiz before adding another.");
            var quiz=new CustomQuiz{PairId=pair,AuthorId=account,Title=title,QuestionsJson=json,UpdatedUtc=now};db.Add(quiz);await db.SaveChangesAsync(ct);return Results.Ok(new{quiz.Id});
        }
        group.MapPost("",(SaveCustomQuiz request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>Save(null,request,user,db,clock,ct));
        group.MapPost("/{id:long}",(long id,SaveCustomQuiz request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>Save(id,request,user,db,clock,ct));
        group.MapPost("/{id:long}/remove",async(long id,RemoveQuiz request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if(!await Own(db,account).AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            return await Own(db,account).Where(x=>x.Id==id && x.Revision==request.Revision).ExecuteDeleteAsync(ct)==1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"Your quiz has changed. Refresh and try again.");
        });
    }
}
