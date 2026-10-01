using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Overlap.Api.Books;

public sealed record BookTitle(string Id, string Title, string[] Authors, string[] AuthorIds,
    string[] Subjects, int? CoverId, int? Year);
public sealed record BookSearch(string? Query, string Genre = "", string Language = "", int Page = 1, long? BasedOn = null);
public sealed record BookResults(BookTitle[] Books, bool HasMore, string? Explanation);

public sealed class BookCatalogCache : IDisposable
{
    public MemoryCache Values { get; } = new(new MemoryCacheOptions { SizeLimit = 200 });
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public DateTimeOffset NextRequest { get; set; }
    public void Dispose() { Values.Dispose(); Gate.Dispose(); }
}

public sealed class BookCatalog(HttpClient http, BookCatalogCache cache, TimeProvider clock)
{
    public static readonly IReadOnlyDictionary<string, string> Genres = new Dictionary<string, string>
    {
        ["fiction"]="Fiction", ["romance"]="Romance", ["fantasy"]="Fantasy",
        ["science fiction"]="Science fiction", ["mystery"]="Mystery", ["thriller"]="Thriller",
        ["horror"]="Horror", ["historical fiction"]="Historical fiction", ["biography"]="Biography", ["poetry"]="Poetry",
    };
    public static bool Valid(BookSearch request) => request.Query is null or {Length: <= 120}
        && request.Genre is not null && (request.Genre == "" || Genres.ContainsKey(request.Genre))
        && request.Language is "" or "en" or "fr" && request.Page is >= 1 and <= 50 && request.BasedOn is null or > 0;
    public static bool Valid(BookTitle book) => book.Id is {Length: <= 40} && Regex.IsMatch(book.Id, @"^/works/OL[0-9]+W$")
        && book.Title is {Length: > 0 and <= 300} && book.Authors is {Length: <= 8}
        && book.Authors.All(x => x is {Length: > 0 and <= 150})
        && book.AuthorIds is {Length: <= 8} && book.AuthorIds.All(x => x is {Length: <= 40} && Regex.IsMatch(x,@"^OL[0-9]+A$"))
        && book.Subjects is {Length: <= 20} && book.Subjects.All(x => x is {Length: > 0 and <= 150})
        && book.CoverId is null or > 0 && book.Year is null or >= 0 and <= 9999;

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    public static bool ValidWorkId(string? id) => id is {Length: <= 40} && Regex.IsMatch(id, @"^/works/OL[0-9]+W$");
    public async Task<string?> Summary(string id, CancellationToken ct)
    {
        var work = await Get(id.TrimStart('/') + ".json", ct);
        if (!work.TryGetProperty("description", out var description)) return null;
        var text = description.ValueKind == JsonValueKind.String ? description.GetString()
            : description.ValueKind == JsonValueKind.Object ? Text(description, "value") : null;
        return string.IsNullOrWhiteSpace(text) ? null : text[..Math.Min(text.Length, 8000)];
    }
    public static (string Query, string? Explanation) SearchTerms(BookSearch request, BookTitle? seed)
    {
        var parts = new List<string>();
        var text = request.Query?.Trim();
        if (!string.IsNullOrEmpty(text)) parts.Add("(title:" + Quote(text) + " OR author:" + Quote(text) + ")");
        var genre = request.Genre;
        string? explanation = null;
        if (seed is not null)
        {
            if (genre == "") genre = Genres.Keys.Where(x => x != "fiction").FirstOrDefault(x => seed.Subjects.Contains(x,StringComparer.OrdinalIgnoreCase)) ?? "";
            if (genre != "") explanation = Genres[genre] + " books, inspired by " + seed.Title + ".";
            else if (seed.AuthorIds.FirstOrDefault() is { } author)
            {
                parts.Add("author_key:" + Quote(author));
                explanation = "More by the author of " + seed.Title + "; genre metadata is unavailable.";
            }
            else throw new ArgumentException("This book has no supported genre or author metadata. Choose a genre instead.");
        }
        if (genre != "") parts.Add("subject:" + Quote(genre));
        if (parts.Count == 0) parts.Add("subject:fiction");
        if (request.Language != "") parts.Add("language:" + (request.Language == "fr" ? "fre" : "eng"));
        return (string.Join(" AND ",parts),explanation);
    }

    public async Task<BookResults> Search(BookSearch request, BookTitle? seed, CancellationToken ct)
    {
        var (query, explanation) = SearchTerms(request,seed);
        var path = "search.json?q=" + Uri.EscapeDataString(query) + "&limit=20&page=" + request.Page
            + "&fields=key,title,author_name,author_key,subject,cover_i,first_publish_year&lang=" + (request.Language == "" ? "en" : request.Language);
        var data = await Get(path,ct);
        var books = new List<BookTitle>();
        foreach (var item in data.GetProperty("docs").EnumerateArray())
        {
            var id = Text(item,"key");
            if (!id.StartsWith('/')) id = "/works/" + id;
            var book = new BookTitle(id,Text(item,"title"),Strings(item,"author_name",8),
                Strings(item,"author_key",8),Strings(item,"subject",20),Number(item,"cover_i"),Number(item,"first_publish_year"));
            if (Valid(book) && book.Id != seed?.Id) books.Add(book);
        }
        var total = Number(data,"numFound") ?? Number(data,"num_found") ?? 0;
        return new(books.DistinctBy(x => x.Id).ToArray(),request.Page * 20 < total,explanation);
    }
    private async Task<JsonElement> Get(string path, CancellationToken ct)
    {
        if (cache.Values.TryGetValue<JsonElement>(path,out var saved)) return saved;
        await cache.Gate.WaitAsync(ct);
        try
        {
            if (cache.Values.TryGetValue<JsonElement>(path,out saved)) return saved;
            var delay = cache.NextRequest - clock.GetUtcNow();
            if (delay > TimeSpan.Zero) await Task.Delay(delay,ct);
            cache.NextRequest = clock.GetUtcNow().AddSeconds(1);
            using var message = new HttpRequestMessage(HttpMethod.Get,"https://openlibrary.org/" + path);
            message.Headers.UserAgent.ParseAdd("Overlap/1.0");
            using var response = await http.SendAsync(message,ct);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024);
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
            saved = doc.RootElement.Clone();
            cache.Values.Set(path,saved,new MemoryCacheEntryOptions {Size=1,AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(10)});
            return saved;
        }
        finally { cache.Gate.Release(); }
    }
    private static string Text(JsonElement item,string key) => item.TryGetProperty(key,out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static int? Number(JsonElement item,string key) => item.TryGetProperty(key,out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static string[] Strings(JsonElement item,string key,int limit) => item.TryGetProperty(key,out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length is > 0 and <= 150).Take(limit).ToArray() : [];
}
