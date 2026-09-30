namespace Overlap.Api.Outings;

public static class OutingMatch
{
    public static readonly Dictionary<string,string> Categories=new() { ["Coffee"]="catering.cafe",["Beer"]="catering.pub",["Drinks"]="catering.bar,catering.pub",["Breakfast"]="catering.cafe,catering.restaurant",["Lunch"]="catering.restaurant",["Dinner"]="catering.restaurant" };
    public static bool Valid(OutingArea? a)=>a is not null && !string.IsNullOrWhiteSpace(a.Name)&&a.Name.Length<=200&&double.IsFinite(a.Lat)&&double.IsFinite(a.Lon)&&Math.Abs(a.Lat)<=90&&Math.Abs(a.Lon)<=180&&!string.IsNullOrWhiteSpace(a.TimeZone);
    public static double Distance(double lat1,double lon1,double lat2,double lon2) {
        double Rad(double n)=>n*Math.PI/180;var a=Math.Pow(Math.Sin(Rad(lat2-lat1)/2),2)+Math.Cos(Rad(lat1))*Math.Cos(Rad(lat2))*Math.Pow(Math.Sin(Rad(lon2-lon1)/2),2);
        return 6371*2*Math.Asin(Math.Sqrt(Math.Clamp(a,0,1)));
    }
    public static Venue[] Eligible(Venue[] places, bool includeUnknownHours) =>
        places.Where(x => x.HoursStatus == "listed-open" || includeUnknownHours && x.HoursStatus == "unknown")
            .DistinctBy(x => x.Id).OrderBy(x => x.HoursStatus == "listed-open" ? 0 : 1).ThenBy(x => x.DistanceKm).ToArray();
    public static VenuePair[] Pair(Venue[] own,Venue[] partner, bool includeUnknownHours = false)=>
        (from a in own where (a.HoursStatus=="listed-open" || includeUnknownHours && a.HoursStatus=="unknown") from b in partner where (b.HoursStatus=="listed-open" || includeUnknownHours && b.HoursStatus=="unknown") select new VenuePair(a,b,a.HoursStatus=="listed-open"&&b.HoursStatus=="listed-open"?"Both list opening hours covering your whole time. Confirm before going.":"Nearby options; opening hours need confirmation."))
        .OrderBy(x=>(x.Own.HoursStatus=="listed-open"?0:1)+(x.Partner.HoursStatus=="listed-open"?0:1)).ThenBy(x=>x.Own.DistanceKm+x.Partner.DistanceKm).Take(8).ToArray();
}
