using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Overlap.Api.Auth;
using Overlap.Api.Notifications;
using Overlap.Api.Schedule;
using Overlap.Api.Outings;
using System.Text.Json;
using Xunit;

namespace Overlap.Api.Tests;
public sealed partial class TogetherTests
{
    private sealed class CalendarPush:ICalendarPushSender
    {
        public List<(string Endpoint,string Date,int Count)> Sent=[];public PushResult Result=PushResult.Sent;
        public Task<PushResult> Send(BrowserPushSubscription device,string day,int eventCount,int ttl,CancellationToken ct){Sent.Add((device.Endpoint,day,eventCount));Assert.InRange(ttl,1,100000);return Task.FromResult(Result);}
    }
    [Fact]
    public async Task CalendarNotificationsRespectOptInLocalTimeAndOncePerDeviceDayAcrossDispatcherRestarts()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        var owner=await SignIn(factory,first,"calendar-push-owner");var partner=await SignIn(factory,second,"calendar-push-partner");(await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        var config=factory.Services.GetRequiredService<IConfiguration>();config["Push:PublicKey"]="test";config["Push:PrivateKey"]="test";config["Push:Subject"]="https://example.test";
        (await Post(first,"phone-reminders/subscribe",Device("calendar-owner"))).EnsureSuccessStatusCode();(await Post(second,"phone-reminders/subscribe",Device("calendar-partner"))).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();var pair=(await db.PairMembers.SingleAsync(x=>x.AccountId==owner)).PairId;
        db.Add(CalendarTime.Create(new("Birthday",null,"2024-10-25","2024-10-25",null,null,"Europe/Paris",true,true,null),pair,owner,DateTime.UtcNow));db.Add(CalendarTime.Create(new("Doctor",null,"2026-10-25","2026-10-25",null,null,"Europe/Paris",true,false,null),pair,partner,DateTime.UtcNow));db.Add(new OutingUpdate{PairId=pair,SenderId=owner,Revision="outing",UpdatedUtc=DateTime.UtcNow,VenueJson=JsonSerializer.Serialize(new SharedVenue("Cafe","Public address","https://example.test",StartUtc:new DateTimeOffset(2026,10,25,15,0,0,TimeSpan.Zero)))});await db.SaveChangesAsync();
        (await Post(first,"calendar/preferences",new{dailyEnabled=true,timeZone="Europe/Paris",notifyMinutes=540})).EnsureSuccessStatusCode();
        (await Post(second,"calendar/preferences",new{dailyEnabled=false,timeZone="America/New_York",notifyMinutes=540})).EnsureSuccessStatusCode();
        var clock=new ReminderClock(new DateTimeOffset(2026,10,25,7,59,0,TimeSpan.Zero));var push=new CalendarPush();
        CalendarReminderDispatcher Dispatcher()=>new(factory.Services.GetRequiredService<IServiceScopeFactory>(),config,clock,factory.Services.GetRequiredService<IDataProtectionProvider>(),push,NullLogger<CalendarReminderDispatcher>.Instance);
        await Dispatcher().Dispatch(default);Assert.Empty(push.Sent);
        clock.Now=clock.Now.AddMinutes(1);await Dispatcher().Dispatch(default);Assert.Single(push.Sent);Assert.Equal("2026-10-25",push.Sent[0].Date);Assert.Equal(3,push.Sent[0].Count);
        await Dispatcher().Dispatch(default);Assert.Single(push.Sent);
        (await Post(second,"calendar/preferences",new{dailyEnabled=true,timeZone="America/New_York",notifyMinutes=540})).EnsureSuccessStatusCode();
        await Dispatcher().Dispatch(default);Assert.Single(push.Sent);
        clock.Now=new(2026,10,25,13,0,0,TimeSpan.Zero);push.Result=PushResult.Retry;await Dispatcher().Dispatch(default);Assert.Equal(2,push.Sent.Count);Assert.Equal(3,push.Sent[1].Count);await Dispatcher().Dispatch(default);Assert.Equal(2,push.Sent.Count);
        clock.Now=clock.Now.AddDays(1);await Dispatcher().Dispatch(default);Assert.Equal(2,push.Sent.Count);
        (await Post(second,"together/leave",new{})).EnsureSuccessStatusCode();clock.Now=clock.Now.AddYears(1).AddDays(-1);await Dispatcher().Dispatch(default);Assert.Equal(2,push.Sent.Count);
    }
}
