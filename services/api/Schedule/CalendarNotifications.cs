using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
using Overlap.Api.Cooking;
using Overlap.Api.Notifications;
using Overlap.Api.Outings;

namespace Overlap.Api.Schedule;

public interface ICalendarPushSender
{
    Task<PushResult> Send(BrowserPushSubscription subscription,string localDate,int eventCount,int ttl,CancellationToken ct);
}
public sealed class CalendarPushSender(IConfiguration config,IHttpClientFactory clients):ICalendarPushSender
{
    public async Task<PushResult> Send(BrowserPushSubscription subscription,string localDate,int eventCount,int ttl,CancellationToken ct)
    {
        using var http=clients.CreateClient("phone-push");var client=new PushServiceClient(http){AutoRetryAfter=false};
        using var authentication=new VapidAuthentication(config["Push:PublicKey"],config["Push:PrivateKey"]){Subject=config["Push:Subject"]};
        try{
            await client.RequestPushMessageDeliveryAsync(new PushSubscription{Endpoint=subscription.Endpoint,Keys=new Dictionary<string,string>{{"p256dh",subscription.Keys.P256dh},{"auth",subscription.Keys.Auth}}},new PushMessage(JsonSerializer.Serialize(new{kind="calendar",localDate,eventCount})){TimeToLive=ttl,Urgency=PushMessageUrgency.Normal},authentication,ct);
            return PushResult.Sent;
        }catch(PushServiceClientException e){return e.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound ? PushResult.Expired : PushResult.Retry;}
        catch(HttpRequestException){return PushResult.Retry;}
    }
}
public sealed class CalendarReminderDispatcher(IServiceScopeFactory scopes,IConfiguration config,TimeProvider clock,IDataProtectionProvider protection,ICalendarPushSender sender,ILogger<CalendarReminderDispatcher> log)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task Dispatch(CancellationToken ct)
    {
        if(!PhoneReminderEndpoints.Configured(config) || !await gate.WaitAsync(0,ct))return;
        try{
            using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var preferences=await db.Set<CalendarPreference>().AsNoTracking().Where(x=>x.DailyEnabled && db.Set<PhoneSubscription>().Any(s=>s.AccountId==x.AccountId)).ToArrayAsync(ct);
            foreach(var preference in preferences){
                TimeZoneInfo zone;try{zone=CalendarTime.Zone(preference.TimeZone);}catch(ArgumentException){continue;}
                var now=clock.GetUtcNow();var local=TimeZoneInfo.ConvertTime(now,zone);
                if(local.Hour*60+local.Minute<preference.NotifyMinutes)continue;
                var pair=await db.PairMembers.Where(x=>x.AccountId==preference.AccountId).Select(x=>x.PairId).SingleOrDefaultAsync(ct);
                if(pair is null || await db.PairMembers.CountAsync(x=>x.PairId==pair,ct)!=2)continue;
                var date=local.Date;var key=date.ToString("yyyy-MM-dd");
                var subscriptions=await db.Set<PhoneSubscription>().AsNoTracking().Where(x=>x.AccountId==preference.AccountId && !db.Set<CalendarDailyDelivery>().Any(d=>d.SubscriptionId==x.Id && d.LocalDate==key)).ToArrayAsync(ct);
                if(subscriptions.Length==0)continue;
                var eventCount=await CountPlans(db,pair,preference.AccountId,date,preference.TimeZone,ct);
                if(eventCount==0)continue;
                foreach(var device in subscriptions){
                    if(!await db.PairMembers.AnyAsync(x=>x.PairId==pair && x.AccountId==preference.AccountId,ct) || !await db.Set<PhoneSubscription>().AnyAsync(x=>x.Id==device.Id,ct) || !await db.Set<CalendarPreference>().AnyAsync(x=>x.AccountId==preference.AccountId && x.DailyEnabled && x.TimeZone==preference.TimeZone && x.NotifyMinutes==preference.NotifyMinutes,ct))continue;
                    BrowserPushSubscription subscription;
                    try{subscription=JsonSerializer.Deserialize<BrowserPushSubscription>(protection.CreateProtector("Overlap.PhonePush.v1").Unprotect(device.ProtectedSubscription))!;}catch(Exception e) when(e is CryptographicException or JsonException){log.LogWarning("A calendar phone subscription could not be read.");continue;}
                    // Persist the unique device/day claim before calling the provider. An uncertain send is not retried.
                    // This gives at-most-once attempts, even across restarts or overlapping worker/scheduler calls.
                    var delivery=new CalendarDailyDelivery{SubscriptionId=device.Id,LocalDate=key,AttemptedUtc=now.UtcDateTime};db.Add(delivery);
                    try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){db.Entry(delivery).State=EntityState.Detached;continue;}
                    var ttl=Math.Max(1,(int)(CalendarTime.Boundary(date.AddDays(1),zone)-clock.GetUtcNow()).TotalSeconds);
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    PushResult result;try{result=await sender.Send(subscription,key,eventCount,ttl,timeout.Token);}catch(OperationCanceledException) when(!ct.IsCancellationRequested){continue;}
                    if(result==PushResult.Expired)await db.Set<PhoneSubscription>().Where(x=>x.Id==device.Id).ExecuteDeleteAsync(ct);
                    if(result==PushResult.Sent){delivery.SentUtc=clock.GetUtcNow().UtcDateTime;await db.SaveChangesAsync(ct);}
                }
            }
            await db.Set<CalendarDailyDelivery>().Where(x=>x.AttemptedUtc<clock.GetUtcNow().UtcDateTime.AddDays(-45)).ExecuteDeleteAsync(ct);
        }finally{gate.Release();}
    }
    private static async Task<int> CountPlans(AuthDbContext db,string pair,string account,DateTime date,string zone,CancellationToken ct)
    {
        var entries=await db.Set<CalendarEntry>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync(ct);
        var count=CalendarTime.Occurrences(entries,date,date.AddDays(1),zone,account).Count;
        var from=CalendarTime.Boundary(date,CalendarTime.Zone(zone)).UtcDateTime;var to=CalendarTime.Boundary(date.AddDays(1),CalendarTime.Zone(zone)).UtcDateTime;
        count+=await db.Set<CookingContest>().CountAsync(x=>x.PairId==pair && x.Status=="accepted" && x.StartUtc>=from && x.StartUtc<to,ct);
        var outings=await db.Set<OutingUpdate>().AsNoTracking().Where(x=>x.PairId==pair && x.CancelledBy==null && x.VenueJson!=null).Select(x=>x.VenueJson).ToArrayAsync(ct);
        return count+outings.Count(json=>JsonSerializer.Deserialize<SharedVenue>(json!)?.StartUtc is {} instant && instant.UtcDateTime>=from && instant.UtcDateTime<to);
    }
}
