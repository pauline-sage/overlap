using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Overlap.Api.Auth;
using Overlap.Api.Cooking;
using Overlap.Api.Notifications;
using Xunit;

namespace Overlap.Api.Tests;
public sealed partial class TogetherTests
{
    private static BrowserPushSubscription Device(string name){
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var point=key.ExportParameters(false);
        static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        return new("https://fcm.googleapis.com/send/"+name,new(Encode([4,..point.Q.X!,..point.Q.Y!]),Encode(RandomNumberGenerator.GetBytes(16))));
    }
    private sealed class ReminderClock(DateTimeOffset now):TimeProvider{public DateTimeOffset Now=now;public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class RecordingPush:IReminderPushSender{
        public List<(long Contest,string Endpoint,int Ttl)> Sent=[];public PushResult Result=PushResult.Sent;
        public Task<PushResult> Send(BrowserPushSubscription subscription,long contestId,int ttl,CancellationToken ct){Sent.Add((contestId,subscription.Endpoint,ttl));return Task.FromResult(Result);}
    }
    [Fact]
    public async Task PhoneSubscriptionsAreOwnerControlledEncryptedAndSchedulerIsProtected(){
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var owner=factory.Client();using var other=factory.Client();using var anonymous=factory.Client();
        await SignIn(factory,owner,"push-owner");await SignIn(factory,other,"push-other");
        var config=factory.Services.GetRequiredService<IConfiguration>();config["Push:PublicKey"]="test";config["Push:PrivateKey"]="test";config["Push:Subject"]="https://example.test";config["Push:SchedulerSecret"]=new string('x',64);
        var device=Device("owner");
        Assert.Equal(HttpStatusCode.Unauthorized,(await Post(anonymous,"phone-reminders/subscribe",device)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(owner,"phone-reminders/subscribe",device with{Endpoint="https://127.0.0.1/private"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(owner,"phone-reminders/subscribe",device with{Endpoint="https://fcm.googleapis.com.attacker.test/push"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(owner,"phone-reminders/subscribe",device with{Keys=new("broken","broken")})).StatusCode);
        using var unprotected=new HttpRequestMessage(HttpMethod.Post,"/api/auth/phone-reminders/subscribe"){Content=JsonContent.Create(device)};
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.SendAsync(unprotected)).StatusCode);
        (await Post(owner,"phone-reminders/subscribe",device)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(other,"phone-reminders/subscribe",device)).StatusCode);
        Assert.False((await (await Post(other,"phone-reminders/status",new{device.Endpoint})).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enabled").GetBoolean());
        (await Post(other,"phone-reminders/unsubscribe",new{device.Endpoint})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row=await db.Set<PhoneSubscription>().SingleAsync();Assert.DoesNotContain(device.Endpoint,row.ProtectedSubscription);Assert.DoesNotContain(device.Keys.Auth,row.ProtectedSubscription);
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsync("/api/reminders/dispatch",null)).StatusCode);
        (await Post(owner,"phone-reminders/unsubscribe",new{device.Endpoint})).EnsureSuccessStatusCode();Assert.Empty(await db.Set<PhoneSubscription>().ToListAsync());
    }
    [Fact]
    public async Task RemindersUseUtcWindowForBothMembersAndDoNotRepeatOrSendCancelledExpiredOrPrivateDevices(){
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        var owner=await SignIn(factory,first,"reminder-first");var partner=await SignIn(factory,second,"reminder-second");
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        var config=factory.Services.GetRequiredService<IConfiguration>();config["Push:PublicKey"]="test";config["Push:PrivateKey"]="test";config["Push:Subject"]="https://example.test";
        var firstDevice=Device("first");var secondDevice=Device("second");
        foreach(var item in new[]{(first,firstDevice),(second,secondDevice)})(await Post(item.Item1,"phone-reminders/subscribe",item.Item2)).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();var pair=(await db.PairMembers.SingleAsync(x=>x.AccountId==owner)).PairId;
        var clock=new ReminderClock(new DateTimeOffset(2026,10,25,0,58,0,TimeSpan.Zero));
        CookingContest Contest(int minutes,string status)=>new(){PairId=pair,ProposedBy=owner,StartUtc=clock.Now.UtcDateTime.AddMinutes(minutes),Status=status,TimeZone="Europe/Paris",Minutes=60};
        var due=Contest(2,"accepted");var future=Contest(3,"accepted");var cancelled=Contest(1,"cancelled");var pending=Contest(1,"pending");var past=Contest(0,"accepted");
        db.AddRange(due,future,cancelled,pending,past);await db.SaveChangesAsync();
        var sender=new RecordingPush();var dispatcher=new ContestReminderDispatcher(factory.Services.GetRequiredService<IServiceScopeFactory>(),config,clock,factory.Services.GetRequiredService<IDataProtectionProvider>(),sender,NullLogger<ContestReminderDispatcher>.Instance);
        await dispatcher.Dispatch(default);Assert.Equal(2,sender.Sent.Count);Assert.All(sender.Sent,x=>{Assert.Equal(due.Id,x.Contest);Assert.Equal(120,x.Ttl);});
        await dispatcher.Dispatch(default);Assert.Equal(2,sender.Sent.Count);
        (await Post(second,"phone-reminders/unsubscribe",new{secondDevice.Endpoint})).EnsureSuccessStatusCode();
        clock.Now=clock.Now.AddMinutes(1);await dispatcher.Dispatch(default);Assert.Equal(3,sender.Sent.Count);Assert.Equal(firstDevice.Endpoint,sender.Sent.Last().Endpoint);Assert.Equal(future.Id,sender.Sent.Last().Contest);
        var retry=Contest(1,"accepted");db.Add(retry);await db.SaveChangesAsync();sender.Result=PushResult.Retry;
        await dispatcher.Dispatch(default);Assert.Equal(4,sender.Sent.Count);await dispatcher.Dispatch(default);Assert.Equal(4,sender.Sent.Count);
        clock.Now=clock.Now.AddSeconds(31);sender.Result=PushResult.Expired;await dispatcher.Dispatch(default);Assert.Equal(5,sender.Sent.Count);Assert.Empty(await db.Set<PhoneSubscription>().ToListAsync());
        Assert.Equal(0,await db.Set<ContestReminderDelivery>().CountAsync());
    }
}
