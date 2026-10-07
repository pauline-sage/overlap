using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Schedule;
using Xunit;

namespace Overlap.Api.Tests;

public sealed class CalendarTimeTests
{
    private static SaveCalendarEntry Request(string date,string start="09:00",string end="10:00",string zone="Europe/Paris",bool allDay=false,bool yearly=false)=>new("Appointment",null,date,date,start,end,zone,allDay,yearly,null);
    [Fact]
    public void SingleTimeAppointmentHasNoInventedEndAndIncludesDayBoundary()
    {
        var entry=CalendarTime.Create(new SaveCalendarEntry("Dentist",null,"2026-10-12",null,"00:00",null,"Europe/Paris",false,false,null),"pair","owner",DateTime.UtcNow);
        Assert.Null(entry.EndUtc);
        var occurrence=Assert.Single(CalendarTime.Occurrences([entry],new DateTime(2026,10,12),new DateTime(2026,10,13),"Europe/Paris","owner"));
        Assert.Null(occurrence.EndUtc);
        Assert.Empty(CalendarTime.Occurrences([entry],new DateTime(2026,10,11),new DateTime(2026,10,12),"Europe/Paris","owner"));
    }
    [Theory]
    [InlineData("2026-03-29","02:30","04:00")]
    [InlineData("2026-10-25","02:30","04:00")]
    public void InvalidAndAmbiguousDstTimesAreRejected(string date,string start,string end)=>Assert.Throws<ArgumentException>(()=>CalendarTime.Create(Request(date,start,end),"pair","owner",DateTime.UtcNow));
    [Fact]
    public void TimedAppointmentsUseRealInstantsAcrossDifferentLocalDays()
    {
        var entry=CalendarTime.Create(Request("2026-10-07","00:30","01:00"),"pair","owner",DateTime.UtcNow);
        Assert.Equal(new DateTime(2026,10,6,22,30,0,DateTimeKind.Utc),entry.StartUtc);
        Assert.Single(CalendarTime.Occurrences([entry],new(2026,10,6),new(2026,10,7),"America/New_York","partner"));
        Assert.Empty(CalendarTime.Occurrences([entry],new(2026,10,7),new(2026,10,8),"America/New_York","partner"));
    }
    [Fact]
    public void AllDayBirthdaysStayOnTheDateAndRepeatOnFebruary28InNonLeapYears()
    {
        var entry=CalendarTime.Create(Request("2024-02-29",allDay:true,yearly:true),"pair","owner",DateTime.UtcNow);
        Assert.Null(entry.StartUtc);
        var nonLeap=Assert.Single(CalendarTime.Occurrences([entry],new(2027,2,28),new(2027,3,1),"Pacific/Honolulu","partner"));Assert.Equal("2027-02-28",nonLeap.Date);Assert.Equal("2027-03-01",nonLeap.EndDate);
        var leap=Assert.Single(CalendarTime.Occurrences([entry],new(2028,2,29),new(2028,3,1),"Asia/Tokyo","owner"));Assert.Equal("2028-02-29",leap.Date);
    }
    [Fact]
    public void YearlyTimedEventsRecalculateOffsetsAndNeverGuessAClockChange()
    {
        var entry=CalendarTime.Create(Request("2025-03-29",yearly:true),"pair","owner",DateTime.UtcNow);
        var first=Assert.Single(CalendarTime.Occurrences([entry],new(2025,3,29),new(2025,3,30),"UTC","owner"));Assert.Equal(8,first.StartUtc!.Value.Hour);
        var second=Assert.Single(CalendarTime.Occurrences([entry],new(2026,3,29),new(2026,3,30),"UTC","owner"));Assert.Equal(7,second.StartUtc!.Value.Hour);
        var early=CalendarTime.Create(Request("2025-03-29","02:30","04:00",yearly:true),"pair","owner",DateTime.UtcNow);
        Assert.Empty(CalendarTime.Occurrences([early],new(2026,3,29),new(2026,3,30),"UTC","owner"));
    }
}
public sealed partial class TogetherTests
{
    [Fact]
    public async Task CalendarSharesWithMembersOnlyAndProtectsOwnerEditsAndRevisions()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();using var outsider=factory.Client();
        await SignIn(factory,first,"calendar-first");await SignIn(factory,second,"calendar-second");await SignIn(factory,outsider,"calendar-outsider");
        var request=new SaveCalendarEntry("Doctor", "Bring the form","2026-10-14","2026-10-14","11:00",null,"Europe/Paris",false,false,null);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,"calendar",request)).StatusCode);
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        using var unsafeRequest=new HttpRequestMessage(HttpMethod.Post,"/api/auth/calendar"){Content=JsonContent.Create(request)};Assert.Equal(HttpStatusCode.BadRequest,(await first.SendAsync(unsafeRequest)).StatusCode);
        (await Post(first,"calendar",request)).EnsureSuccessStatusCode();
        const string list="/api/auth/calendar?year=2026&month=10&timeZone=Europe%2FParis";
        var item=Assert.Single((await second.GetFromJsonAsync<JsonElement>(list)).GetProperty("items").EnumerateArray());Assert.Equal("Doctor",item.GetProperty("title").GetString());Assert.False(item.GetProperty("mine").GetBoolean());Assert.Equal(JsonValueKind.Null,item.GetProperty("endUtc").ValueKind);var id=item.GetProperty("id").GetInt64();var revision=item.GetProperty("revision").GetString();
        Assert.Empty((await outsider.GetFromJsonAsync<JsonElement>(list)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(second,$"calendar/{id}",request with{Revision=revision})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(outsider,$"calendar/{id}/remove",new{revision})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(second,$"calendar/{id}/remove",new{revision})).StatusCode);
        (await Post(first,$"calendar/{id}",request with{Revision=revision,Title="Check-up"})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"calendar/{id}/remove",new{revision})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"calendar",request with{Date="2026-10-25",EndDate="2026-10-25",StartTime="02:30"})).StatusCode);
        (await Post(first,"calendar/preferences",new{dailyEnabled=true,timeZone="Europe/Paris",notifyMinutes=540})).EnsureSuccessStatusCode();
        (await Post(first,"calendar/timezone",new{timeZone="America/Sao_Paulo"})).EnsureSuccessStatusCode();
        var preference=await first.GetFromJsonAsync<JsonElement>("/api/auth/calendar/preferences");
        Assert.Equal("America/Sao_Paulo",preference.GetProperty("timeZone").GetString());Assert.True(preference.GetProperty("dailyEnabled").GetBoolean());Assert.Equal(540,preference.GetProperty("notifyMinutes").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"calendar/timezone",new{timeZone="invalid"})).StatusCode);
        Assert.False((await second.GetFromJsonAsync<JsonElement>("/api/auth/calendar/preferences")).GetProperty("dailyEnabled").GetBoolean());
        (await Post(second,"together/leave",new{})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<CalendarEntry>().ToArrayAsync());
    }
}
