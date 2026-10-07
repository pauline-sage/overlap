using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
using Overlap.Api.Cooking;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace Overlap.Api.Notifications;

public sealed class PhoneSubscription
{
    public long Id { get; set; }
    public required string AccountId { get; set; }
    public required string EndpointHash { get; set; }
    public required string ProtectedSubscription { get; set; }
}
public sealed class ContestReminderDelivery
{
    public long ContestId { get; set; }
    public long SubscriptionId { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? SentUtc { get; set; }
    public int Attempts { get; set; }
}
public sealed record BrowserPushKeys(string P256dh,string Auth);
public sealed record BrowserPushSubscription(string Endpoint,BrowserPushKeys Keys);
public sealed record RemovePhoneSubscription(string Endpoint);
public enum PushResult { Sent, Expired, Retry }
public interface IReminderPushSender
{
    Task<PushResult> Send(BrowserPushSubscription subscription,long contestId,int ttl,CancellationToken ct);
}
public sealed class ReminderPushSender(IConfiguration config,IHttpClientFactory clients):IReminderPushSender
{
    public async Task<PushResult> Send(BrowserPushSubscription subscription,long contestId,int ttl,CancellationToken ct)
    {
        using var http=clients.CreateClient("phone-push");
        var client=new PushServiceClient(http){AutoRetryAfter=false};
        using var authentication=new VapidAuthentication(config["Push:PublicKey"],config["Push:PrivateKey"]){Subject=config["Push:Subject"]};
        try {
            await client.RequestPushMessageDeliveryAsync(new PushSubscription{Endpoint=subscription.Endpoint,Keys=new Dictionary<string,string>{{"p256dh",subscription.Keys.P256dh},{"auth",subscription.Keys.Auth}}},
                new PushMessage(JsonSerializer.Serialize(new {title="Cooking together soon",body="Your cooking contest starts in a minute or two.",contestId})){TimeToLive=ttl,Urgency=PushMessageUrgency.High},authentication,ct);
            return PushResult.Sent;
        } catch(PushServiceClientException e) {return e.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound ? PushResult.Expired : PushResult.Retry;}
        catch(HttpRequestException){return PushResult.Retry;}
    }
}
public static class PhoneReminderEndpoints
{
    public static bool Configured(IConfiguration config)=>!string.IsNullOrWhiteSpace(config["Push:PublicKey"]) && !string.IsNullOrWhiteSpace(config["Push:PrivateKey"]) && !string.IsNullOrWhiteSpace(config["Push:Subject"]);
    public static string Hash(string endpoint)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
    public static bool Valid(BrowserPushSubscription? request)
    {
        if(request?.Keys is null || request.Endpoint is null || request.Endpoint.Length>2048 || !Uri.TryCreate(request.Endpoint,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Port!=443 || uri.UserInfo.Length!=0 || uri.Fragment.Length!=0)return false;
        var host=uri.IdnHost;
        if(host!="fcm.googleapis.com" && host!="web.push.apple.com" && host!="updates.push.services.mozilla.com" && !host.EndsWith(".push.services.mozilla.com",StringComparison.Ordinal))return false;
        if(request.Keys.P256dh is null || request.Keys.Auth is null || request.Keys.P256dh.Length>100 || request.Keys.Auth.Length>30)return false;
        try{var point=Decode(request.Keys.P256dh);if(point.Length!=65 || point[0]!=4 || Decode(request.Keys.Auth).Length!=16)return false;
            using var key=ECDsa.Create(new ECParameters{Curve=ECCurve.NamedCurves.nistP256,Q=new ECPoint{X=point[1..33],Y=point[33..65]}});return true;
        }catch(Exception e) when(e is FormatException or CryptographicException or ArgumentException){return false;}
    }
    private static byte[] Decode(string value)=>Convert.FromBase64String(value.Replace('-','+').Replace('_','/').PadRight((value.Length+3)/4*4,'='));
    public static void MapPhoneReminders(this WebApplication app)
    {
        var group=app.MapGroup("/api/auth/phone-reminders").RequireAuthorization().RequireRateLimiting("picks");
        group.MapGet("",(IConfiguration config)=>Results.Ok(new{available=Configured(config),publicKey=Configured(config)?config["Push:PublicKey"]:null}));
        group.MapPost("/subscribe",async(BrowserPushSubscription request,ClaimsPrincipal user,AuthDbContext db,IConfiguration config,IDataProtectionProvider protection,CancellationToken ct)=>{
            if(!Configured(config))return Results.Problem(statusCode:503,title:"Phone reminders are not configured yet.");
            if(!Valid(request))return Results.BadRequest();
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var hash=Hash(request.Endpoint);
            var row=await db.Set<PhoneSubscription>().SingleOrDefaultAsync(x=>x.EndpointHash==hash,ct);
            if(row is not null && row.AccountId!=account)return Results.Problem(statusCode:409,title:"This device is linked to another account. Disable its reminders first.");
            if(row is null){if(await db.Set<PhoneSubscription>().CountAsync(x=>x.AccountId==account,ct)>=5)return Results.Problem(statusCode:409,title:"Five devices already have reminders enabled.");row=new(){AccountId=account,EndpointHash=hash,ProtectedSubscription=""};db.Add(row);}
            row.ProtectedSubscription=protection.CreateProtector("Overlap.PhonePush.v1").Protect(JsonSerializer.Serialize(request));
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){return Results.Problem(statusCode:409,title:"The device subscription changed. Try again.");}
            return Results.NoContent();
        });
        group.MapPost("/status",async(RemovePhoneSubscription request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            if(request.Endpoint is null || request.Endpoint.Length>2048)return Results.BadRequest();
            var hash=Hash(request.Endpoint);var account=user.FindFirstValue(ClaimTypes.NameIdentifier);
            return Results.Ok(new{enabled=await db.Set<PhoneSubscription>().AnyAsync(x=>x.EndpointHash==hash && x.AccountId==account,ct)});
        });
        group.MapPost("/unsubscribe",async(RemovePhoneSubscription request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            if(request.Endpoint is null || request.Endpoint.Length>2048)return Results.BadRequest();
            var hash=Hash(request.Endpoint);var account=user.FindFirstValue(ClaimTypes.NameIdentifier);
            await db.Set<PhoneSubscription>().Where(x=>x.AccountId==account && x.EndpointHash==hash).ExecuteDeleteAsync(ct);return Results.NoContent();
        });
        app.MapPost("/api/reminders/dispatch",async(HttpContext context,IConfiguration config,ContestReminderDispatcher dispatcher,Overlap.Api.Schedule.CalendarReminderDispatcher calendar)=>{
            var secret=config["Push:SchedulerSecret"];
            if(string.IsNullOrWhiteSpace(secret) || secret.Length<32)return Results.NotFound();
            var expected=SHA256.HashData(Encoding.UTF8.GetBytes("Bearer "+secret));var supplied=SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()));
            if(!CryptographicOperations.FixedTimeEquals(expected,supplied))return Results.Unauthorized();
            await dispatcher.Dispatch(context.RequestAborted);await calendar.Dispatch(context.RequestAborted);return Results.NoContent();
        }).RequireRateLimiting("auth");
    }
}
public sealed class ContestReminderDispatcher(IServiceScopeFactory scopes,IConfiguration config,TimeProvider clock,IDataProtectionProvider protection,IReminderPushSender sender,ILogger<ContestReminderDispatcher> log)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task Dispatch(CancellationToken ct)
    {
        if(!PhoneReminderEndpoints.Configured(config) || !await gate.WaitAsync(0,ct))return;
        try {
            using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now=clock.GetUtcNow().UtcDateTime;var soon=now.AddMinutes(2);
            var candidates=await (from contest in db.Set<CookingContest>()
                from member in db.PairMembers.Where(x=>x.PairId==contest.PairId)
                from subscription in db.Set<PhoneSubscription>().Where(x=>x.AccountId==member.AccountId)
                where contest.Status=="accepted" && contest.StartUtc>now && contest.StartUtc<=soon && db.PairMembers.Count(x=>x.PairId==contest.PairId)==2
                    && !db.Set<ContestReminderDelivery>().Any(x=>x.ContestId==contest.Id && x.SubscriptionId==subscription.Id && (x.SentUtc!=null || x.Attempts>=3 || x.NextAttemptUtc>now))
                select new {ContestId=contest.Id,SubscriptionId=subscription.Id,subscription.ProtectedSubscription}).Take(100).ToListAsync(ct);
            foreach(var item in candidates){
                var row=await db.Set<ContestReminderDelivery>().FindAsync([item.ContestId,item.SubscriptionId],ct);
                // The unique key and lease keep overlapping scheduler calls from sending twice.
                if(row is null){row=new(){ContestId=item.ContestId,SubscriptionId=item.SubscriptionId,NextAttemptUtc=now};db.Add(row);try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){db.Entry(row).State=EntityState.Detached;continue;}}
                now=clock.GetUtcNow().UtcDateTime;var lease=now.AddSeconds(30);
                var claimed=await db.Set<ContestReminderDelivery>().Where(x=>x.ContestId==item.ContestId && x.SubscriptionId==item.SubscriptionId && x.SentUtc==null && x.Attempts<3 && x.NextAttemptUtc<=now)
                    .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.NextAttemptUtc,lease).SetProperty(x=>x.Attempts,x=>x.Attempts+1),ct);
                if(claimed==0)continue;
                var contest=await db.Set<CookingContest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==item.ContestId && x.Status=="accepted" && x.StartUtc>now && db.PairMembers.Count(m=>m.PairId==x.PairId)==2,ct);
                if(contest is null || !await db.Set<PhoneSubscription>().AnyAsync(x=>x.Id==item.SubscriptionId,ct))continue;
                BrowserPushSubscription subscription;
                try{subscription=JsonSerializer.Deserialize<BrowserPushSubscription>(protection.CreateProtector("Overlap.PhonePush.v1").Unprotect(item.ProtectedSubscription))!;}catch(CryptographicException){log.LogWarning("A phone subscription could not be decrypted.");continue;}
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
                PushResult result;try{result=await sender.Send(subscription,contest.Id,Math.Max(1,(int)(contest.StartUtc-now).TotalSeconds),timeout.Token);}catch(OperationCanceledException) when(!ct.IsCancellationRequested){continue;}
                if(result==PushResult.Expired)await db.Set<PhoneSubscription>().Where(x=>x.Id==item.SubscriptionId).ExecuteDeleteAsync(ct);
                if(result==PushResult.Sent){var sent=clock.GetUtcNow().UtcDateTime;await db.Set<ContestReminderDelivery>().Where(x=>x.ContestId==item.ContestId && x.SubscriptionId==item.SubscriptionId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SentUtc,sent),ct);}
            }
        } finally{gate.Release();}
    }
}
public sealed class PhoneReminderWorker(ContestReminderDispatcher dispatcher,Overlap.Api.Schedule.CalendarReminderDispatcher calendar,ILogger<PhoneReminderWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        while(await timer.WaitForNextTickAsync(stoppingToken)){
            try{await dispatcher.Dispatch(stoppingToken);await calendar.Dispatch(stoppingToken);}catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}catch(Exception){log.LogWarning("Phone reminder dispatch failed; it will retry on the next tick.");}
        }
    }
}
