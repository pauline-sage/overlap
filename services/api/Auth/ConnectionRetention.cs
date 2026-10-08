using Microsoft.EntityFrameworkCore;

namespace Overlap.Api.Auth;

public sealed class ConnectionRetention(AuthDbContext db,IConfiguration config,TimeProvider clock)
{
    private IQueryable<ConnectionHistory> Expired(DateTime now,string creatorEmail)=>db.Set<ConnectionHistory>().Where(h=>
        h.Closed && h.ExportExpiresUtc!=null && h.ExportExpiresUtc<=now &&
        !db.PairMembers.Any(m=>m.PairId==h.PairId) &&
        !db.Set<AlbumAccess>().Any(m=>m.PairId==h.PairId && db.Users.Any(u=>u.Id==m.AccountId && u.EmailConfirmed && u.NormalizedEmail==creatorEmail)));

    public async Task<int> PurgeExpired(CancellationToken ct)
    {
        // Missing deployment configuration must never silently delete creator archives.
        var email=config["ConnectionHistory:CreatorEmail"]?.Trim().ToUpperInvariant();
        if(string.IsNullOrEmpty(email))return 0;
        var now=clock.GetUtcNow().UtcDateTime;
        var pairs=await Expired(now,email).OrderBy(x=>x.ExportExpiresUtc).Select(x=>x.PairId).Take(25).ToArrayAsync(ct);
        var removed=0;
        foreach(var pair in pairs){
            await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            if(!await Expired(now,email).AnyAsync(x=>x.PairId==pair,ct))continue;
            await db.Set<Overlap.Api.Watch.SharedPick>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Outings.OutingUpdate>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Watch.WatchedTitle>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Books.ReadingEntry>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Cooking.SavedRecipe>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Cooking.CookingContest>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Quiz.QuizRound>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Quiz.CustomQuiz>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Outings.SavedPlace>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Schedule.CalendarEntry>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<Overlap.Api.Drawing.DrawingRound>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<SharingGrant>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<AlbumAccess>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            await db.Set<ConnectionHistory>().Where(x=>x.PairId==pair).ExecuteDeleteAsync(ct);
            // Existing foreign keys cascade photos, ratings, answers and recipe-draw records.
            await transaction.CommitAsync(ct);removed++;
        }
        return removed;
    }
}

public sealed class ConnectionRetentionWorker(IServiceScopeFactory scopes,IConfiguration config,TimeProvider clock,ILogger<ConnectionRetentionWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(string.IsNullOrWhiteSpace(config["ConnectionHistory:CreatorEmail"])){
            log.LogWarning("Connection cleanup is disabled until the private creator configuration is supplied.");return;
        }
        await Task.Yield();
        using var timer=new PeriodicTimer(TimeSpan.FromHours(1),clock);
        try{
            do{
                try{
                    int removed;
                    do{
                        await using var scope=scopes.CreateAsyncScope();
                        removed=await scope.ServiceProvider.GetRequiredService<ConnectionRetention>().PurgeExpired(stoppingToken);
                        if(removed>0)log.LogInformation("Removed {Count} expired connection archives.",removed);
                    }while(removed==25 && !stoppingToken.IsCancellationRequested);
                }catch(Exception error) when(!stoppingToken.IsCancellationRequested){log.LogWarning(error,"Expired connection cleanup failed; it will retry on the next pass.");}
            }while(await timer.WaitForNextTickAsync(stoppingToken));
        }catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){ }
    }
}
