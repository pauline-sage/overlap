using System.Globalization;
namespace Overlap.Api.Outings;

public sealed record OutingWindowRequest(string? LocalStart, string OwnTimeZone, string PartnerTimeZone, int Minutes);
public sealed record OutingWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc, string OwnStart, string OwnEnd, string PartnerStart, string PartnerEnd);
public static class OutingTime
{
    public static OutingWindow Resolve(OutingWindowRequest request, DateTimeOffset now)
    {
        if (request.Minutes is < 15 or > 240) throw new ArgumentException("Choose between 15 minutes and 4 hours.");
        var own = Zone(request.OwnTimeZone); var partner = Zone(request.PartnerTimeZone);
        DateTimeOffset start;
        if (string.IsNullOrEmpty(request.LocalStart)) start = now.ToUniversalTime();
        else {
            if (!DateTime.TryParseExact(request.LocalStart, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new ArgumentException("Choose a valid date and time.");
            var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            if (own.IsInvalidTime(local)) throw new ArgumentException("That local time does not exist because the clocks change. Choose another time.");
            if (own.IsAmbiguousTime(local)) throw new ArgumentException("That local time occurs twice because the clocks change. Choose a time outside the clock change.");
            start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, own));
            if (start < now.AddMinutes(-1) || start > now.AddDays(90)) throw new ArgumentException("Choose a time within the next 90 days.");
        }
        var end = start.AddMinutes(request.Minutes);
        string Local(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
        return new(start, end, Local(start,own), Local(end,own), Local(start,partner), Local(end,partner));
    }
    private static TimeZoneInfo Zone(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || (id != "UTC" && !id.Contains('/'))) throw new ArgumentException("Choose an IANA timezone for each person.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Unknown timezone. Choose a timezone from the list."); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("This timezone is unavailable."); }
    }
}
