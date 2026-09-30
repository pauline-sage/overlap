using System.Globalization;
using System.Text.Json;
namespace Overlap.Api.Outings;

public sealed record OutingArea(string Name, double Lat, double Lon, string TimeZone);
public sealed record Venue(string Id,string Name,string Address,double DistanceKm,string? Hours,string HoursStatus,string MapUrl,string? Website);

public sealed record VenuePair(Venue Own,Venue Partner,string Reason);
public sealed record SharedVenue(string Name, string Address, string MapUrl, string? Website = null, string? Activity = null, DateTimeOffset? StartUtc = null, string? TimeZone = null);
public sealed record VenueResult(Venue Place, string ShareToken);
public sealed record NearbyResult(string LocalStart, string LocalEnd, VenueResult[] Places, int Examined, DateTimeOffset CheckedUtc);
public sealed class GeoapifyPlaces(HttpClient http,IConfiguration config)
{
    public bool Configured => !string.IsNullOrWhiteSpace(config["Outings:GeoapifyApiKey"]);
    private async Task<JsonDocument> Get(string path,CancellationToken ct) {
        using var response=await http.GetAsync("https://api.geoapify.com/"+path+"&apiKey="+Uri.EscapeDataString(config["Outings:GeoapifyApiKey"]!),ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
    }
    private static string? Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    public async Task<OutingArea[]> Areas(string text,CancellationToken ct) {
        using var doc=await Get("v1/geocode/search?text="+Uri.EscapeDataString(text)+"&limit=5",ct);
        var result=new List<OutingArea>();
        foreach(var feature in doc.RootElement.GetProperty("features").EnumerateArray()) {
            var p=feature.GetProperty("properties");
            if(!p.TryGetProperty("timezone",out var zone)||Text(zone,"name") is not string id)continue;
            result.Add(new(Text(p,"formatted")??text,p.GetProperty("lat").GetDouble(),p.GetProperty("lon").GetDouble(),id));
        }
        return result.ToArray();
    }
    public async Task<OutingArea?> Locate(double lat, double lon, CancellationToken ct) {
        using var doc = await Get(FormattableString.Invariant($"v1/geocode/reverse?lat={lat}&lon={lon}&type=city&limit=1"), ct);
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray()) {
            var p = feature.GetProperty("properties");
            if (p.TryGetProperty("timezone", out var zone) && Text(zone, "name") is string id)
                return new OutingArea("Current location", lat, lon, id);
        }
        return null;
    }
    public async Task<Venue[]> Nearby(OutingArea area,int radius,string category,OutingWindow window,CancellationToken ct) {
        var position=FormattableString.Invariant($"{area.Lon},{area.Lat}");
        using var doc=await Get($"v2/places?categories={category}&filter=circle:{position},{radius*1000}&bias=proximity:{position}&limit=20",ct);
        var result=new System.Collections.Concurrent.ConcurrentBag<Venue>();
        await Parallel.ForEachAsync(doc.RootElement.GetProperty("features").EnumerateArray().ToArray(), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (feature, token) => {
            var p=feature.GetProperty("properties");var id=Text(p,"place_id");if(id is null)return;
            var lat=p.GetProperty("lat").GetDouble();var lon=p.GetProperty("lon").GetDouble();
            var distance=OutingMatch.Distance(area.Lat,area.Lon,lat,lon);if(distance>radius)return;
            string? hours=null,website=null,venueZone=null;
            try {
                using var detail=await Get("v2/place-details?id="+Uri.EscapeDataString(id)+"&features=details",token);
                var d=detail.RootElement.GetProperty("features").EnumerateArray().Select(x=>x.GetProperty("properties")).FirstOrDefault();
                if(d.ValueKind==JsonValueKind.Object){hours=Text(d,"opening_hours");website=Text(d,"website"); if(d.TryGetProperty("timezone",out var zone))venueZone=Text(zone,"name");}
            } catch(HttpRequestException) { /* Keep the real listing, with explicitly unknown hours. */ }
            if(!Uri.TryCreate(website,UriKind.Absolute,out var site)||site.Scheme is not ("https" or "http"))website=null;
            result.Add(new(id,Text(p,"name")??"Unnamed venue",Text(p,"formatted")??area.Name,Math.Round(distance,2),hours,venueZone is null ? "unknown" : VenueHours.Assess(hours,venueZone,window.StartUtc,window.EndUtc),FormattableString.Invariant($"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=18/{lat}/{lon}"),website));
        });
        return result.OrderBy(x => x.DistanceKm).ToArray();
    }
}
