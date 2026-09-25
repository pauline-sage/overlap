using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Caching.Memory;
namespace Overlap.Api.Watch;
public sealed record YouTubeRequest(string? Query, string Theme, string Origin, string Format, int Minutes, string OwnCountry, string PartnerCountry, bool CaptionsOnly = false, string Genre = "");
public sealed record YouTubeVideo(string Id, string Title, string Channel, string Url, string Thumbnail, int Minutes, bool Captions);
public sealed class YouTubeSearch(HttpClient http, IConfiguration config, TmdbCache cache)
{
    public bool Configured => !string.IsNullOrWhiteSpace(config["Streaming:YouTubeApiKey"]);
    public static bool Valid(YouTubeRequest r) => (r.Query?.Length ?? 0) <= 120 &&
        (r.Theme == "" || r.Theme != null && WatchRules.Themes.ContainsKey(r.Theme)) && r.Origin is "" or "TH" or "CN" or "TW" or "JP" or "KR" &&
        r.Format is "Movie" or "Series" or "Both" && r.Minutes is >= 0 and <= 600 && r.OwnCountry is "FR" or "US" && r.PartnerCountry is "FR" or "US" &&
        (r.Genre == "" || r.Genre != null && WatchRules.Genres.ContainsKey(r.Genre));
    private async Task<JsonElement> Get(string path, CancellationToken ct)
    {
        var key = "youtube:" + path;
        if (cache.Values.TryGetValue<JsonElement>(key, out var found)) return found;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/" + path);
        request.Headers.Add("X-Goog-Api-Key", config["Streaming:YouTubeApiKey"]);
        using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var data = json.RootElement.Clone();
        cache.Values.Set(key, data, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        return data;
    }
    public static bool Available(JsonElement details, string country)
    {
        if (!details.TryGetProperty("regionRestriction", out var restrictions)) return true;
        if (restrictions.TryGetProperty("allowed", out var allowed) && !allowed.EnumerateArray().Any(v => v.GetString() == country)) return false;
        return !restrictions.TryGetProperty("blocked", out var blocked) || !blocked.EnumerateArray().Any(v => v.GetString() == country);
    }
    public static YouTubeVideo? Read(JsonElement video, YouTubeRequest request)
    {
        var id = video.GetProperty("id").GetString() ?? "";
        if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$")) return null;
        var snippet = video.GetProperty("snippet"); var details = video.GetProperty("contentDetails");
        if (video.GetProperty("status").GetProperty("privacyStatus").GetString() != "public" ||
            snippet.TryGetProperty("liveBroadcastContent", out var live) && live.GetString() != "none") return null;
        if (!Available(details, request.OwnCountry) || !Available(details, request.PartnerCountry)) return null;
        var title = snippet.GetProperty("title").GetString() ?? "";
        if (Regex.IsMatch(title, @"\b(trailer|teaser|reaction|fmv|ost|highlights?|shorts?|clips?|compilations?|edits?)\b|behind the scenes|ตัวอย่าง", RegexOptions.IgnoreCase)) return null;
        // Search relevance alone returns fan edits and short clips. Require an episode/film marker.
        var marker = request.Format == "Movie" ? @"full movie|full film|complete movie" : @"\bep(?:isode)?\.?\s*\d+|full episode|\u0e15\u0e2d\u0e19\u0e17\u0e35\u0e48|\u7b2c.{1,6}\u96c6|\u7b2c.{1,6}\u8a71";
        if (request.Format == "Both") marker += "|full movie|full film|complete movie";
        if (!Regex.IsMatch(title, marker, RegexOptions.IgnoreCase)) return null;
        double minutes;
        try { minutes = XmlConvert.ToTimeSpan(details.GetProperty("duration").GetString()!).TotalMinutes; }
        catch (FormatException) { return null; }
        if (minutes < 4 || request.Minutes > 0 && minutes > request.Minutes) return null;
        var captions = details.TryGetProperty("caption", out var caption) && caption.GetString() == "true";
        if (request.CaptionsOnly && !captions) return null;
        return new(id, title, snippet.GetProperty("channelTitle").GetString() ?? "", "https://www.youtube.com/watch?v=" + id,
            "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg", (int)Math.Ceiling(minutes), captions);
    }
    public async Task<YouTubeVideo[]> Search(YouTubeRequest request, CancellationToken ct)
    {
        var theme = request.Theme switch { "bl" => "boys love", "lgbtq" => "LGBTQ", _ => "" };
        var origin = request.Origin switch { "TH" => "Thai", "CN" => "Chinese", "TW" => "Taiwanese", "JP" => "Japanese", "KR" => "Korean", _ => "" };
        var query = string.Join(' ', new[] { request.Query?.Trim(), theme, request.Genre, origin, request.Format == "Movie" ? "full movie" : request.Format == "Series" ? "full episode" : "full movie|full episode" }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var data = await Get("search?part=snippet&type=video&maxResults=20&safeSearch=moderate&regionCode=" + request.OwnCountry +
            "&videoCaption=" + (request.CaptionsOnly ? "closedCaption" : "any") + "&q=" + Uri.EscapeDataString(query), ct);
        var ids = data.GetProperty("items").EnumerateArray().Select(v => v.GetProperty("id").GetProperty("videoId").GetString()).Where(id => id != null && Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$")).ToArray();
        if (ids.Length == 0) return [];
        var videos = await Get("videos?part=snippet,contentDetails,status&id=" + string.Join(',', ids), ct);
        return videos.GetProperty("items").EnumerateArray().Select(v => Read(v, request)).OfType<YouTubeVideo>().ToArray();
    }
}
