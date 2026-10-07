using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
using Overlap.Api.Notifications;

namespace Overlap.Api.Schedule;

public sealed record SaveCalendarPreference(bool DailyEnabled,string? TimeZone,int NotifyMinutes);
public sealed record CalendarRevision(string? Revision);
public sealed record SaveProfileTimeZone(string? TimeZone);
public static class CalendarEndpoints
{
    private static IQueryable<CalendarEntry> Visible(AuthDbContext db,string account)=>db.Set<CalendarEntry>().Where(x=>db.PairMembers.Any(m=>m.AccountId==account && m.PairId==x.PairId));
    public static void MapCalendar(this WebApplication app)
    {
        var group=app.MapGroup("/api/auth/calendar").RequireAuthorization().RequireRateLimiting("picks");
        group.MapPost("/timezone",async(SaveProfileTimeZone request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            try{CalendarTime.Zone(request.TimeZone);}catch(ArgumentException e){return Results.Problem(statusCode:400,title:e.Message);}
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var row=await db.Set<CalendarPreference>().FindAsync([account],ct);
            if(row is null){row=new(){AccountId=account};db.Add(row);}
            row.TimeZone=request.TimeZone!;await db.SaveChangesAsync(ct);return Results.NoContent();
        });
        group.MapGet("",async(int year,int month,string timeZone,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>{
            if(year is <1970 or >2100 || month is <1 or >12)return Results.BadRequest();
            try{CalendarTime.Zone(timeZone);}catch(ArgumentException e){return Results.Problem(statusCode:400,title:e.Message);}
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var entries=await Visible(db,account).AsNoTracking().Take(500).ToArrayAsync(ct);
            var from=new DateTime(year,month,1);var today=TimeZoneInfo.ConvertTime(clock.GetUtcNow(),CalendarTime.Zone(timeZone)).Date;
            var daily=CalendarTime.Occurrences(entries,today,today.AddDays(1),timeZone,account);
            return Results.Ok(new{connected=await db.PairMembers.AnyAsync(x=>x.AccountId==account,ct),items=CalendarTime.Occurrences(entries,from.AddDays(-7),from.AddMonths(1).AddDays(7),timeZone,account),ownToday=daily.Count(x=>x.Mine),partnerToday=daily.Count(x=>!x.Mine)});
        });
        group.MapGet("/preferences",async(ClaimsPrincipal user,AuthDbContext db,IConfiguration config,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var preference=await db.Set<CalendarPreference>().AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account,ct);
            var zones=TimeZoneInfo.GetSystemTimeZones().Select(x=>TimeZoneInfo.TryConvertWindowsIdToIanaId(x.Id,out var iana) ? iana! : x.Id).Where(x=>x=="UTC" || x.Contains('/')).Append("UTC").Distinct().Order().ToArray();
            return Results.Ok(new{dailyEnabled=preference?.DailyEnabled ?? false,timeZone=preference?.TimeZone,notifyMinutes=preference?.NotifyMinutes ?? 540,available=PhoneReminderEndpoints.Configured(config),devices=await db.Set<PhoneSubscription>().CountAsync(x=>x.AccountId==account,ct),zones});
        });
        group.MapPost("/preferences",async(SaveCalendarPreference request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            try{CalendarTime.Zone(request.TimeZone);if(request.NotifyMinutes is <0 or >1439)throw new ArgumentException("Choose a valid notification time.");}catch(ArgumentException e){return Results.Problem(statusCode:400,title:e.Message);}
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var row=await db.Set<CalendarPreference>().FindAsync([account],ct);
            if(row is null){row=new(){AccountId=account};db.Add(row);}row.TimeZone=request.TimeZone!;row.NotifyMinutes=request.NotifyMinutes;row.DailyEnabled=request.DailyEnabled;await db.SaveChangesAsync(ct);return Results.NoContent();
        });
        async Task<IResult> Save(long? id,SaveCalendarEntry request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct){
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var pair=await db.PairMembers.Where(x=>x.AccountId==account).Select(x=>x.PairId).SingleOrDefaultAsync(ct);
            if(pair is null)return Results.Problem(statusCode:409,title:"Connect with your person to share a calendar.");
            CalendarEntry entry;try{entry=CalendarTime.Create(request,pair,account,clock.GetUtcNow().UtcDateTime);}catch(ArgumentException e){return Results.Problem(statusCode:400,title:e.Message);}
            if(id is {} existing){
                var old=await Visible(db,account).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==existing,ct);if(old is null)return Results.NotFound();if(old.AuthorId!=account)return Results.Problem(statusCode:403,title:"Only the person who added this appointment can edit it.");
                var changed=await Visible(db,account).Where(x=>x.Id==existing && x.AuthorId==account && x.Revision==request.Revision).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Title,entry.Title).SetProperty(x=>x.Note,entry.Note).SetProperty(x=>x.TimeZone,entry.TimeZone).SetProperty(x=>x.LocalStart,entry.LocalStart).SetProperty(x=>x.LocalEnd,entry.LocalEnd).SetProperty(x=>x.StartUtc,entry.StartUtc).SetProperty(x=>x.EndUtc,entry.EndUtc).SetProperty(x=>x.AllDay,entry.AllDay).SetProperty(x=>x.Yearly,entry.Yearly).SetProperty(x=>x.Revision,entry.Revision).SetProperty(x=>x.UpdatedUtc,entry.UpdatedUtc),ct);
                return changed==1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"This appointment changed. Refresh before editing it.");
            }
            if(await db.Set<CalendarEntry>().CountAsync(x=>x.PairId==pair,ct)>=500)return Results.Problem(statusCode:409,title:"The calendar has 500 entries. Remove an old entry before adding another.");
            db.Add(entry);await db.SaveChangesAsync(ct);return Results.Ok(new{entry.Id});
        }
        group.MapPost("",(SaveCalendarEntry request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>Save(null,request,user,db,clock,ct));
        group.MapPost("/{id:long}",(long id,SaveCalendarEntry request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct)=>Save(id,request,user,db,clock,ct));
        group.MapPost("/{id:long}/remove",async(long id,CalendarRevision request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;var old=await Visible(db,account).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(old is null)return Results.NotFound();if(old.AuthorId!=account)return Results.Problem(statusCode:403,title:"Only the person who added this appointment can remove it.");
            return await Visible(db,account).Where(x=>x.Id==id && x.AuthorId==account && x.Revision==request.Revision).ExecuteDeleteAsync(ct)==1 ? Results.NoContent() : Results.Problem(statusCode:409,title:"This appointment changed. Refresh before removing it.");
        });
    }
}
