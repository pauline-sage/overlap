namespace Overlap.Api.Watch;

public sealed record WatchSearch(string Format, string? Query, string Mode, string[] Genres,
    int Minutes, string OwnCountry, string PartnerCountry, string[] OwnPlatforms, string[] PartnerPlatforms, int Page = 1, string Theme = "", string Origin = "");
public sealed record WatchTitle(string Id, string Title, string Format, int? Minutes, string[] Genres,
    string Description, string? Poster, string? Year, string[] OwnServices, string[] PartnerServices,
    string OwnLink, string PartnerLink);
public sealed record WatchResults(WatchTitle[] Titles, bool HasMore);

public static class WatchRules
{
    public static readonly string[] Platforms = ["Netflix", "Prime Video", "Disney+", "YouTube"];
    // Exact TMDB keyword IDs verified against the keyword API on 2026-09-25.
    public static readonly Dictionary<string, int[]> Themes = new()
    {
        ["lgbtq"] = [158718, 380747, 264386, 290527, 240305, 365317, 289844, 353318],
        ["bl"] = [365317, 289844, 353318]
    };
    public static bool MatchesTheme(string theme, IEnumerable<int> keywords) => theme == "" || Themes[theme].Intersect(keywords).Any();
    public static string[] IncludedServices(System.Text.Json.JsonElement region, string[] selected)
    {
        var matches = new List<string>();
        foreach (var kind in new[] { "flatrate", "free", "ads" })
        {
            if (!region.TryGetProperty(kind, out var offers) || offers.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
            foreach (var offer in offers.EnumerateArray())
            {
                var name = offer.TryGetProperty("provider_name", out var field) ? field.GetString() : null;
                var platform = Platform(name ?? "");
                // YouTube here means free uploads, not Premium, TV, rentals or purchases.
                if (platform != null && selected.Contains(platform) &&
                    (platform == "YouTube" ? kind is "free" or "ads" : kind == "flatrate")) matches.Add(platform);
            }
        }
        return matches.Distinct().ToArray();
    }
    public static readonly Dictionary<string, (string Movie, string Tv)> Genres = new()
    {
        ["Comedy"] = ("35", "35"), ["Drama"] = ("18", "18"), ["Crime"] = ("80", "80"),
        ["Mystery"] = ("9648", "9648"), ["Documentary"] = ("99", "99"), ["Family"] = ("10751", "10751"),
        ["Animation"] = ("16", "16"), ["Action & Adventure"] = ("28|12", "10759"), ["Sci-fi & Fantasy"] = ("878|14", "10765")
    };
    public static bool Valid(WatchSearch r) => r.Format is "Both" or "Movie" or "Series" &&
        r.Mode is "title" or "keyword" && (r.Query?.Length ?? 0) <= 120 && r.Minutes is >= 0 and <= 600 &&
        r.Page is >= 1 and <= 50 && r.OwnCountry is "FR" or "US" && r.PartnerCountry is "FR" or "US" &&
        (r.Theme == "" || r.Theme != null && Themes.ContainsKey(r.Theme)) &&
        r.Origin is "" or "TH" or "CN" or "TW" or "JP" or "KR" &&
        r.Genres is { Length: <= 9 } && r.Genres.All(Genres.ContainsKey) &&
        ValidPlatforms(r.OwnPlatforms) && ValidPlatforms(r.PartnerPlatforms);
    private static bool ValidPlatforms(string[]? values) => values is { Length: > 0 and <= 4 } && values.All(Platforms.Contains);
    public static bool Fits(int? runtime, int available) => available == 0 || runtime is > 0 && runtime <= available;
    public static string? Platform(string name) => name switch
    {
        "Netflix" or "Netflix Standard with Ads" => "Netflix",
        "Amazon Prime Video" or "Amazon Prime Video with Ads" => "Prime Video",
        "Disney Plus" => "Disney+", "YouTube" or "YouTube Free" => "YouTube", _ => null
    };
}
