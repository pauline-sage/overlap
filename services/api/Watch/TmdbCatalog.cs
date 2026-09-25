using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Overlap.Api.Watch;

public sealed class TmdbCache : IDisposable
{
    public MemoryCache Values { get; } = new(new MemoryCacheOptions { SizeLimit = 1000 });
    public void Dispose() => Values.Dispose();
}

public sealed class TmdbCatalog(HttpClient http, IConfiguration config, TmdbCache storage)
{
    private async Task<JsonElement> Get(string path, CancellationToken ct)
    {
        if (storage.Values.TryGetValue<JsonElement>(path, out var cached)) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["Streaming:TmdbReadAccessToken"]);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var value = json.RootElement.Clone();
        // Bounded cache; never include credentials in its keys.
        storage.Values.Set(path, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return value;
    }
    public bool Configured => !string.IsNullOrWhiteSpace(config["Streaming:TmdbReadAccessToken"]);
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
    private static int? Number(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var number) && number > 0 ? number : null;
    private static IEnumerable<JsonElement> Items(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Array ? field.EnumerateArray() : [];
    public async Task<WatchResults> Search(WatchSearch request, CancellationToken ct)
    {
        var types = request.Format == "Both" ? new[] { "movie", "tv" } : [request.Format == "Movie" ? "movie" : "tv"];
        var titles = new List<WatchTitle>(); var more = false;
        foreach (var type in types)
        {
            var genreIds = request.Genres.SelectMany(g => (type == "movie" ? WatchRules.Genres[g].Movie : WatchRules.Genres[g].Tv).Split('|')).ToArray();
            var query = request.Query?.Trim() ?? "";
            var path = "";
            if (query.Length > 0 && request.Mode == "title")
                path = "search/" + type + "?query=" + Uri.EscapeDataString(query);
            else
            {
                var providers = await Get("watch/providers/" + type + "?watch_region=" + request.OwnCountry, ct);
                var ids = Items(providers, "results").Where(p => request.OwnPlatforms.Contains(WatchRules.Platform(Text(p, "provider_name")))).Select(p => Number(p, "provider_id")).Where(p => p.HasValue);
                if (!ids.Any()) continue;
                path = "discover/" + type + "?sort_by=popularity.desc&watch_region=" + request.OwnCountry + "&with_watch_monetization_types=flatrate|free|ads&with_watch_providers=" + string.Join('|', ids);
                if (request.Origin != "") path += "&with_origin_country=" + request.Origin;
                if (genreIds.Length > 0) path += "&with_genres=" + string.Join('|', genreIds);
                if (query.Length > 0)
                {
                    var keywords = await Get("search/keyword?query=" + Uri.EscapeDataString(query), ct);
                    var keywordIds = Items(keywords, "results").Take(5).Select(k => Number(k, "id")).Where(id => id.HasValue).ToArray();
                    if (keywordIds.Length == 0) continue;
                    path += "&with_keywords=" + string.Join('|', keywordIds);
                }
            }
            if (!(query.Length > 0 && request.Mode == "title") && request.Theme != "" && query.Length == 0)
                path += "&with_keywords=" + string.Join('|', WatchRules.Themes[request.Theme]);
            var candidates = await Get(path + "&include_adult=false&language=en-US&page=" + request.Page, ct);
            more |= (Number(candidates, "total_pages") ?? 0) > request.Page && request.Page < 50;
            using var limit = new SemaphoreSlim(4);
            var results = await Task.WhenAll(Items(candidates, "results").Take(20).Select(async candidate =>
            {
                await limit.WaitAsync(ct);
                try
                {
                    var id = Number(candidate, "id");
                    if (id is null) return null;
                    var detail = await Get(type + "/" + id + "?append_to_response=watch/providers,keywords&language=en-US", ct);
                    if (detail.TryGetProperty("adult", out var adult) && adult.ValueKind == JsonValueKind.True) return null;
                    var keywords = detail.TryGetProperty("keywords", out var tags)
                        ? Items(tags, type == "movie" ? "keywords" : "results").Select(k => Number(k, "id") ?? 0) : [];
                    if (!WatchRules.MatchesTheme(request.Theme, keywords)) return null;
                    if (request.Origin != "")
                    {
                        var origins = Items(detail, "origin_country").Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString());
                        if (!origins.Contains(request.Origin)) return null;
                    }
                    var genres = Items(detail, "genres").ToArray();
                    if (genreIds.Length > 0 && !genres.Any(g => genreIds.Contains(Number(g, "id")?.ToString()))) return null;
                    if (!detail.TryGetProperty("watch/providers", out var watch) || !watch.TryGetProperty("results", out var availability)) return null;
                    (string[] Services, string Link) Available(string country, string[] selected)
                    {
                        if (!availability.TryGetProperty(country, out var region)) return ([], "");
                        var services = WatchRules.IncludedServices(region, selected);
                        var link = Text(region, "link");
                        return (services, link.StartsWith("https://www.themoviedb.org/", StringComparison.Ordinal) ? link : "https://www.themoviedb.org/" + type + "/" + id + "/watch?locale=" + country);
                    }
                    var own = Available(request.OwnCountry, request.OwnPlatforms); var partner = Available(request.PartnerCountry, request.PartnerPlatforms);
                    if (own.Services.Length == 0 || partner.Services.Length == 0) return null;
                    int? runtime = Number(detail, "runtime");
                    if (type == "tv")
                    {
                        try { runtime = Number(await Get("tv/" + id + "/season/1/episode/1?language=en-US", ct), "runtime"); }
                        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { runtime = null; }
                    }
                    if (!WatchRules.Fits(runtime, request.Minutes)) return null;
                    var poster = Text(detail, "poster_path");
                    var date = Text(detail, type == "movie" ? "release_date" : "first_air_date");
                    return new WatchTitle(type + "-" + id, Text(detail, type == "movie" ? "title" : "name"), type == "movie" ? "Movie" : "Series", runtime,
                        genres.Select(g => Text(g, "name")).ToArray(), Text(detail, "overview"), poster.StartsWith('/') ? "https://image.tmdb.org/t/p/w500" + poster : null,
                        date.Length >= 4 ? date[..4] : null, own.Services, partner.Services, own.Link, partner.Link);
                }
                catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
                finally { limit.Release(); }
            }));
            titles.AddRange(results.OfType<WatchTitle>());
        }
        return new(titles.ToArray(), more);
    }
}
