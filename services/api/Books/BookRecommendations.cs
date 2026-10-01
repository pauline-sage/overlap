using System.Text.Json;

namespace Overlap.Api.Books;

public static class BookRecommendations
{
    public sealed record Seed(BookTitle Book, int? Stars);
    public static Seed[] Seeds(IEnumerable<ReadingEntry> entries, IReadOnlyDictionary<long,int> ratings) => entries
        .Where(x => ratings.TryGetValue(x.Id,out var stars) ? stars >= 4 : x.Status == "read")
        .OrderByDescending(x => ratings.GetValueOrDefault(x.Id)).ThenByDescending(x => x.UpdatedUtc)
        .Select(x => new Seed(JsonSerializer.Deserialize<BookTitle>(x.Content)!,ratings.TryGetValue(x.Id,out var stars) ? stars : null))
        .Where(x => x.Book.Subjects.Any(s => BookCatalog.Genres.Keys.Any(g => g != "fiction" && g.Equals(s,StringComparison.OrdinalIgnoreCase))) || x.Book.AuthorIds.Length > 0)
        .DistinctBy(x => BookCatalog.SearchTerms(new BookSearch(null),x.Book).Query)
        .Take(3).ToArray();
}
