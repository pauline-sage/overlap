using System.Globalization;

namespace Overlap.Api.Schedule;

public sealed class CalendarEntry
{
    public long Id { get; set; }
    public required string PairId { get; set; }
    public required string AuthorId { get; set; }
    public required string Title { get; set; }
    public string? Note { get; set; }
    public required string TimeZone { get; set; }
    public DateTime LocalStart { get; set; }
    public DateTime LocalEnd { get; set; }
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public bool AllDay { get; set; }
    public bool Yearly { get; set; }
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime UpdatedUtc { get; set; }
}
public sealed class CalendarPreference
{
    public required string AccountId { get; set; }
    public bool DailyEnabled { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public string NotificationTimeZone { get; set; } = "UTC";
    public int NotifyMinutes { get; set; } = 540;
}
public sealed class CalendarDailyDelivery
{
    public long SubscriptionId { get; set; }
    public required string LocalDate { get; set; }
    public DateTime AttemptedUtc { get; set; }
    public DateTime? SentUtc { get; set; }
}
public sealed record SaveCalendarEntry(string? Title, string? Note, string? Date, string? EndDate, string? StartTime, string? EndTime, string? TimeZone, bool AllDay, bool Yearly, string? Revision);
public sealed record CalendarOccurrence(long Id, string Title, string? Note, bool Mine, bool AllDay, bool Yearly, string TimeZone, string Date, string EndDate, DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, string SourceDate, string SourceEndDate, string StartTime, string EndTime, string Revision);

public static class CalendarTime
{
    public static TimeZoneInfo Zone(string? id)
    {
        if(string.IsNullOrWhiteSpace(id) || id.Length>100 || (id!="UTC" && !id.Contains('/'))) throw new ArgumentException("Choose an IANA timezone.");
        try{return TimeZoneInfo.FindSystemTimeZoneById(id);}
        catch(Exception e) when(e is TimeZoneNotFoundException or InvalidTimeZoneException){throw new ArgumentException("Choose a valid timezone.");}
    }
    public static DateTimeOffset Instant(DateTime local, TimeZoneInfo zone)
    {
        local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
        if(zone.IsInvalidTime(local))throw new ArgumentException("That time does not exist because the clocks change. Choose another time.");
        if(zone.IsAmbiguousTime(local))throw new ArgumentException("That time occurs twice because the clocks change. Choose a time outside the clock change.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone));
    }
    public static DateTimeOffset Boundary(DateTime local,TimeZoneInfo zone)
    {
        local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
        while(zone.IsInvalidTime(local))local=local.AddMinutes(1);
        if(zone.IsAmbiguousTime(local))return new DateTimeOffset(local,zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone));
    }
    public static CalendarEntry Create(SaveCalendarEntry request,string pair,string account,DateTime now)
    {
        if(string.IsNullOrWhiteSpace(request.Title) || request.Title.Length>160 || request.Note?.Length>1000)throw new ArgumentException("Use a title up to 160 characters and an optional note up to 1,000 characters.");
        var zone=Zone(request.TimeZone);
        DateTime Parse(string? date,string? time){
            if(!DateTime.TryParseExact(date+"T"+(request.AllDay ? "00:00" : time),"yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local) || local.Year is <1970 or >2100)throw new ArgumentException("Choose a valid date and time between 1970 and 2100.");
            return DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
        }
        var start=Parse(request.Date,request.StartTime);
        var singleTime=!request.AllDay && string.IsNullOrEmpty(request.EndTime);
        var end=singleTime ? start : request.AllDay ? Parse(request.EndDate ?? request.Date,"00:00").AddDays(1) : Parse(request.EndDate ?? request.Date,request.EndTime);
        if((!singleTime && end<=start) || end-start>TimeSpan.FromDays(7))throw new ArgumentException("The end must be after the start, within seven days.");
        DateTime? startUtc=null,endUtc=null;
        if(!request.AllDay){startUtc=Instant(start,zone).UtcDateTime;endUtc=singleTime ? null : Instant(end,zone).UtcDateTime;if(endUtc is not null && endUtc<=startUtc)throw new ArgumentException("The end must be after the start.");}
        return new(){PairId=pair,AuthorId=account,Title=request.Title.Trim(),Note=string.IsNullOrWhiteSpace(request.Note)?null:request.Note.Trim(),TimeZone=request.TimeZone!,LocalStart=start,LocalEnd=end,StartUtc=startUtc,EndUtc=endUtc,AllDay=request.AllDay,Yearly=request.Yearly,UpdatedUtc=now};
    }
    public static IReadOnlyList<CalendarOccurrence> Occurrences(IEnumerable<CalendarEntry> entries,DateTime fromLocalDate,DateTime toLocalDate,string viewerZone,string viewerAccount)
    {
        var zone=Zone(viewerZone);var fromUtc=Boundary(fromLocalDate,zone);var toUtc=Boundary(toLocalDate,zone);
        var result=new List<CalendarOccurrence>();
        foreach(var entry in entries){
            var years=entry.Yearly ? Enumerable.Range(Math.Max(entry.LocalStart.Year,fromLocalDate.Year-1),Math.Max(0,toLocalDate.Year-Math.Max(entry.LocalStart.Year,fromLocalDate.Year-1)+1)) : [entry.LocalStart.Year];
            foreach(var year in years){
                var start=entry.Yearly ? new DateTime(year,entry.LocalStart.Month,Math.Min(entry.LocalStart.Day,DateTime.DaysInMonth(year,entry.LocalStart.Month))).Add(entry.LocalStart.TimeOfDay) : entry.LocalStart;
                var end=start+(entry.LocalEnd-entry.LocalStart);
                DateTimeOffset? startInstant=null,endInstant=null;
                if(entry.AllDay){if(start.Date>=toLocalDate || end.Date<=fromLocalDate)continue;}
                else {
                    try{startInstant=Instant(start,Zone(entry.TimeZone));endInstant=entry.EndUtc is null ? null : Instant(end,Zone(entry.TimeZone));}
                    // A yearly timed appointment in a DST gap/overlap needs an owner correction; never guess an offset.
                    catch(ArgumentException){continue;}
                    if(startInstant>=toUtc || (endInstant is null ? startInstant<fromUtc : endInstant<=fromUtc))continue;
                }
                result.Add(new(entry.Id,entry.Title,entry.Note,entry.AuthorId==viewerAccount,entry.AllDay,entry.Yearly,entry.TimeZone,start.ToString("yyyy-MM-dd"),end.ToString("yyyy-MM-dd"),startInstant,endInstant,entry.LocalStart.ToString("yyyy-MM-dd"),(entry.AllDay ? entry.LocalEnd.AddDays(-1) : entry.LocalEnd).ToString("yyyy-MM-dd"),entry.LocalStart.ToString("HH:mm"),entry.LocalEnd.ToString("HH:mm"),entry.Revision));
            }
        }
        return result.OrderBy(x=>x.Date).ThenBy(x=>x.StartUtc).ToArray();
    }
}
