using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Overlap.Api.Cooking;

public sealed record RecipeHit(long Id, string Title);
public sealed record RecipePhoto(string Url, string Author, string License, string LicenseUrl, string SourceUrl);
public sealed record Recipe(long Id, long Revision, string Title, string[] Ingredients, string[] Instructions, string SourceUrl, RecipePhoto? Photo);
public sealed class RecipeCatalog(HttpClient http)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static string Replace(string value, string pattern, string replacement) => Regex.Replace(value, pattern, replacement, RegexOptions.Singleline | RegexOptions.IgnoreCase, RegexTimeout);
    private static string Text(string value) => WebUtility.HtmlDecode(Replace(value, "<[^>]*>", " ")).Trim();
    private async Task<JsonElement> Get(string host, string query, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/w/api.php?format=json&formatversion=2&{query}");
        request.Headers.UserAgent.ParseAdd("Overlap/1.0 (https://overlap-t7l6.onrender.com; recipe catalogue)");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_000_000, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.Clone();
    }
    public async Task<RecipeHit[]> Search(string query, CancellationToken ct)
    {
        var json = await Get("en.wikibooks.org", "action=query&list=search&srnamespace=102&srlimit=20&srsearch=" + Uri.EscapeDataString(query), ct);
        return json.GetProperty("query").GetProperty("search").EnumerateArray()
            .Where(x => x.GetProperty("title").GetString()!.StartsWith("Cookbook:", StringComparison.Ordinal))
            .Select(x => new RecipeHit(x.GetProperty("pageid").GetInt64(), x.GetProperty("title").GetString()![9..])).ToArray();
    }
    public async Task<RecipeHit[]> Search(string query, ContestPreferences[] preferences, CancellationToken ct)
    {
        if (!preferences.Any(x => x.AvoidRedMeat || x.ExcludedIngredients.Length > 0)) return await Search(query, ct);
        // Prefer real catalogue alternatives; never rewrite a recipe's ingredients.
        var vegetarian = await Search(query + " vegetarian", ct);
        var vegan = await Search(query + " vegan", ct);
        var original = await Search(query, ct);
        var candidates = vegetarian.Concat(vegan).Concat(original).DistinctBy(x => x.Id).Take(20).ToArray();
        var matches = new List<RecipeHit>();
        foreach (var batch in candidates.Chunk(4))
        {
            var checkedRecipes = await Task.WhenAll(batch.Select(async hit =>
            {
                var recipe = await Details(hit.Id, ct, includePhoto: false);
                return recipe is not null && ContestRecipeFilter.Allows(recipe, preferences) ? hit : null;
            }));
            matches.AddRange(checkedRecipes.OfType<RecipeHit>());
        }
        return matches.ToArray();
    }
    public static Recipe? Extract(long id, long revision, string title, string html)
    {
        if (!title.StartsWith("Cookbook:", StringComparison.Ordinal) || html.Length > 800_000) return null;
        var ingredients = Section(html, "Ingredients");
        var instructions = Section(html, "Procedure|Preparation|Instructions|Directions|Method");
        if (ingredients.Length == 0 || instructions.Length == 0) return null;
        return new Recipe(id, revision, title[9..], ingredients, instructions,
            $"https://en.wikibooks.org/w/index.php?oldid={revision}", null);
    }
    private static string[] Section(string html, string headings)
    {
        var headers = Regex.Matches(html, @"<h([2-6])\b[^>]*>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase, RegexTimeout);
        foreach (Match header in headers)
        {
            var heading = Text(header.Groups[2].Value).Replace("[edit]", "").Trim();
            if (!Regex.IsMatch(heading, "^(" + headings + ")$", RegexOptions.IgnoreCase, RegexTimeout)) continue;
            var level = int.Parse(header.Groups[1].Value);
            var end = headers.Cast<Match>().FirstOrDefault(x => x.Index > header.Index && int.Parse(x.Groups[1].Value) <= level)?.Index ?? html.Length;
            var body = html[(header.Index + header.Length)..end];
            var listItems = Regex.IsMatch(body, @"<li\b", RegexOptions.IgnoreCase, RegexTimeout);
            var pattern = listItems ? @"<h[3-6]\b[^>]*>(.*?)</h[3-6]>|<li\b[^>]*>(.*?)</li>" : @"<h[3-6]\b[^>]*>(.*?)</h[3-6]>|<p\b[^>]*>(.*?)</p>";
            var items = new List<string>(); var subsection = "";
            foreach (Match item in Regex.Matches(body, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase, RegexTimeout))
            {
                if (item.Groups[1].Success) { subsection = Text(item.Groups[1].Value).Replace("[edit]", "").Trim(); continue; }
                var text = Text(item.Groups[2].Value);
                if (text.Length > 0) items.Add(subsection.Length == 0 ? text : subsection + ": " + text);
            }
            if (items.Count is > 0 and <= 100 && items.All(x => x.Length <= 4000)) return items.ToArray();
        }
        return [];
    }
    public async Task<Recipe?> Details(long id, CancellationToken ct, bool includePhoto = true)
    {
        var json = await Get("en.wikibooks.org", $"action=parse&pageid={id}&prop=text|revid|images", ct);
        if (!json.TryGetProperty("parse", out var page)) return null;
        var recipe = Extract(id, page.GetProperty("revid").GetInt64(), page.GetProperty("title").GetString()!, page.GetProperty("text").GetString()!);
        if (recipe is null) return null;
        if (!includePhoto) return recipe;
        // Only display an explicitly reusable Commons photo; image bytes are never downloaded or persisted.
        try
        {
        foreach (var image in page.GetProperty("images").EnumerateArray().Take(5))
        {
            var filename = image.GetString()!;
            if (filename.Contains("logo", StringComparison.OrdinalIgnoreCase) || filename.Contains("Wikibooks", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Regex.IsMatch(filename, @"\.(jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase, RegexTimeout)) continue;
            var metadata = await Get("commons.wikimedia.org", "action=query&prop=imageinfo&iiprop=url|extmetadata&iiurlwidth=600&titles=" + Uri.EscapeDataString("File:" + filename), ct);
            foreach (var file in metadata.GetProperty("query").GetProperty("pages").EnumerateArray())
            {
                if (!file.TryGetProperty("imageinfo", out var infos)) continue;
                var info = infos[0]; var ext = info.GetProperty("extmetadata");
                string Value(string key) => ext.TryGetProperty(key, out var v) ? Text(v.GetProperty("value").GetString() ?? "") : "";
                var license = Value("LicenseShortName");
                if (!(license.StartsWith("CC BY", StringComparison.Ordinal) || license is "CC0" or "Public domain")) continue;
                var url = info.TryGetProperty("thumburl", out var thumb) ? thumb.GetString()! : info.GetProperty("url").GetString()!;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host is not ("upload.wikimedia.org" or "thumb.wikimedia.org")) continue;
                return recipe with { Photo = new RecipePhoto(url, Value("Artist"), license, Value("LicenseUrl"), info.GetProperty("descriptionurl").GetString()!) };
            }
        }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            // A missing remote photograph does not make the recipe text unavailable.
        }
        return recipe;
    }
}
