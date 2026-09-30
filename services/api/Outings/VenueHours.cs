using System.Globalization;
using System.Text.RegularExpressions;
namespace Overlap.Api.Outings;

public static class VenueHours
{
    // Deliberately conservative: special dates, holidays and unsupported OSM syntax remain unknown.
    public static string Assess(string? hours, string zoneId, DateTimeOffset start, DateTimeOffset end)
    {
        if (string.IsNullOrWhiteSpace(hours)) return "unknown";
        if (hours.Trim() == "24/7") return "listed-open";
        var days = new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
        var rules = new List<(HashSet<int> Days, int Start, int End)>();
        foreach (var section in hours.Split(';')) {
            var match = Regex.Match(section.Trim(), @"^((?:Mo|Tu|We|Th|Fr|Sa|Su)(?:-(?:Mo|Tu|We|Th|Fr|Sa|Su))?(?:,(?:Mo|Tu|We|Th|Fr|Sa|Su))*) ([0-9:, -]+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) return "unknown";
            var selected = new HashSet<int>();
            foreach (var part in match.Groups[1].Value.Split(',')) {
                var range=part.Split('-'); var a=Array.IndexOf(days,range[0]);var b=Array.IndexOf(days,range[^1]);
                for(var d=a;;d=(d+1)%7){selected.Add(d);if(d==b)break;}
            }
            foreach (var span in match.Groups[2].Value.Split(',')) {
                var endpoints=span.Trim().Split('-');
                int? Minutes(string text) {
                    if(text=="24:00")return 1440;
                    return TimeOnly.TryParseExact(text,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var t)?t.Hour*60+t.Minute:null;
                }
                if(endpoints.Length!=2 || Minutes(endpoints[0]) is not int from || Minutes(endpoints[1]) is not int to || from==to || from==1440)return "unknown";
                rules.Add((selected,from,to));
            }
        }
        TimeZoneInfo zone;
        try {zone=TimeZoneInfo.FindSystemTimeZoneById(zoneId);} catch(TimeZoneNotFoundException){return "unknown";}
        bool Open(DateTimeOffset instant) {
            var local=TimeZoneInfo.ConvertTime(instant,zone);var day=((int)local.DayOfWeek+6)%7;var minute=local.Hour*60+local.Minute;
            return rules.Any(r=>r.End>r.Start ? r.Days.Contains(day)&&minute>=r.Start&&minute<r.End : (r.Days.Contains(day)&&minute>=r.Start)||(r.Days.Contains((day+6)%7)&&minute<r.End));
        }
        for(var at=start;at<end;at=at.AddMinutes(1))if(!Open(at))return "listed-closed";
        return Open(end.AddTicks(-1))?"listed-open":"listed-closed";
    }
}
