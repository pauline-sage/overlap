using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Quiz;
public sealed class QuizRound
{
    public long Id {get;set;}
    public string Title {get;set;}="Guess my answer";
    public required string PairId {get;set;}
    public required string ProposedBy {get;set;}
    public required string QuestionsJson {get;set;}
    public string Status {get;set;}="pending";
    public int CurrentIndex {get;set;}
    public string Revision {get;set;}=Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc {get;set;}
    public DateTime? CompletedUtc {get;set;}
}
public sealed class QuizAnswer
{
    public long RoundId {get;set;}
    public int QuestionIndex {get;set;}
    public required string AccountId {get;set;}
    public int? OwnChoice {get;set;}
    public int? GuessChoice {get;set;}
    public bool Skipped {get;set;}
    public DateTime SubmittedUtc {get;set;}
}
public sealed record QuizSubmission(int QuestionIndex,int? OwnChoice,int? GuessChoice,bool Skipped);
public sealed record QuizAction(string Revision,string Action);
public sealed record QuizReveal(int? OwnAnswer,int? OwnGuess,int? PartnerAnswer,int? PartnerGuess,bool OwnSkipped,bool PartnerSkipped,bool? OwnCorrect,bool? PartnerCorrect);
public static class QuizPlay
{
    public static QuizQuestion[] Questions(QuizRound round)=>JsonSerializer.Deserialize<QuizQuestion[]>(round.QuestionsJson)!;
    public static QuizReveal? Reveal(QuizAnswer? own,QuizAnswer? partner)=>own is null || partner is null ? null : new(
        own.OwnChoice,own.GuessChoice,partner.OwnChoice,partner.GuessChoice,own.Skipped,partner.Skipped,
        own.Skipped || partner.Skipped ? null : own.GuessChoice==partner.OwnChoice,
        own.Skipped || partner.Skipped ? null : partner.GuessChoice==own.OwnChoice);
    public static object Detail(QuizRound round,QuizAnswer[] answers,string account)=>new{
        round.Id,round.Title,round.Status,round.CurrentIndex,round.Revision,mine=round.ProposedBy==account,
        createdUtc=DateTime.SpecifyKind(round.CreatedUtc,DateTimeKind.Utc),
        completedUtc=round.CompletedUtc is {} completed ? DateTime.SpecifyKind(completed,DateTimeKind.Utc) : (DateTime?)null,
        questions=Questions(round).Select((q,index)=>{
            var own=answers.SingleOrDefault(a=>a.QuestionIndex==index && a.AccountId==account);
            var partner=answers.SingleOrDefault(a=>a.QuestionIndex==index && a.AccountId!=account);
            return new{q.Id,q.CategoryId,q.Prompt,q.Choices,ownSubmitted=own!=null,partnerSubmitted=partner!=null,
                ownAnswer=own is null ? null : new{own.OwnChoice,own.GuessChoice,own.Skipped},reveal=Reveal(own,partner)};
        })
    };
    public static async Task<IResult> Answer(QuizRound round,string account,QuizSubmission request,AuthDbContext db,TimeProvider clock,CancellationToken ct){
        if(round.Status!="active" || request.QuestionIndex!=round.CurrentIndex)return Conflict("This round has moved on. Refresh to continue.");
        var question=Questions(round)[round.CurrentIndex];
        if(request.Skipped ? request.OwnChoice!=null || request.GuessChoice!=null : request.OwnChoice is null || request.GuessChoice is null || request.OwnChoice<0 || request.GuessChoice<0 || request.OwnChoice>=question.Choices.Length || request.GuessChoice>=question.Choices.Length)return Results.Problem(statusCode:400,title:"Choose your answer and your guess, or skip this question.");
        if(await db.Set<QuizAnswer>().AnyAsync(a=>a.RoundId==round.Id && a.QuestionIndex==request.QuestionIndex && a.AccountId==account,ct))return Conflict("Your submitted answers are locked.");
        var revision=Guid.NewGuid().ToString("N");
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        if(await db.Set<QuizRound>().Where(x=>x.Id==round.Id && x.Status=="active" && x.Revision==round.Revision && x.CurrentIndex==request.QuestionIndex)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revision,revision),ct)!=1)return Conflict("Your partner just updated this round. Refresh and submit again.");
        db.Add(new QuizAnswer{RoundId=round.Id,QuestionIndex=request.QuestionIndex,AccountId=account,OwnChoice=request.OwnChoice,GuessChoice=request.GuessChoice,Skipped=request.Skipped,SubmittedUtc=clock.GetUtcNow().UtcDateTime});
        await db.SaveChangesAsync(ct);
        if(round.CurrentIndex==4 && await db.Set<QuizAnswer>().CountAsync(a=>a.RoundId==round.Id && a.QuestionIndex==4,ct)==2)
            await db.Set<QuizRound>().Where(x=>x.Id==round.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"completed").SetProperty(x=>x.CompletedUtc,clock.GetUtcNow().UtcDateTime),ct);
        await transaction.CommitAsync(ct);return Results.NoContent();
    }
    public static async Task<IResult> Act(QuizRound round,string account,QuizAction request,AuthDbContext db,CancellationToken ct){
        if(request.Revision is null || request.Revision!=round.Revision)return Conflict("This round has changed. Refresh and try again.");
        var revision=Guid.NewGuid().ToString("N");
        if(request.Action=="next"){
            if(round.Status!="active" || round.CurrentIndex>=4 || await db.Set<QuizAnswer>().CountAsync(x=>x.RoundId==round.Id && x.QuestionIndex==round.CurrentIndex,ct)!=2)return Conflict("Both of you must answer or skip before continuing.");
            var changed=await db.Set<QuizRound>().Where(x=>x.Id==round.Id && x.Revision==request.Revision && x.Status=="active")
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CurrentIndex,round.CurrentIndex+1).SetProperty(x=>x.Revision,revision),ct);
            return changed==1 ? Results.NoContent() : Conflict("This round has moved on. Refresh to continue.");
        }
        if(request.Action is "accept" or "decline"){
            if(round.ProposedBy==account)return Results.Problem(statusCode:403,title:"Only your partner can respond to this invitation.");
            if(round.Status!="pending")return Conflict("This invitation is no longer pending.");
        }else if(request.Action!="cancel")return Results.BadRequest();
        else if(round.Status is not ("pending" or "active"))return Conflict("This round is already finished.");
        var status=request.Action=="accept" ? "active" : request.Action=="decline" ? "declined" : "cancelled";
        var rows=await db.Set<QuizRound>().Where(x=>x.Id==round.Id && x.Revision==request.Revision && x.Status==round.Status)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,status).SetProperty(x=>x.Revision,revision),ct);
        return rows==1 ? Results.NoContent() : Conflict("This round has changed. Refresh and try again.");
    }
    private static IResult Conflict(string title)=>Results.Problem(statusCode:409,title:title);
}
