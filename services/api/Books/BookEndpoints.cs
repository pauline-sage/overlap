using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;

namespace Overlap.Api.Books;

public sealed class ReadingEntry
{
    public long Id { get; set; }
    public required string PairId { get; set; }
    public required string AddedBy { get; set; }
    public string Scope { get; set; } = "read";
    public required string BookId { get; set; }
    public required string Content { get; set; }
    public string Status { get; set; } = "planned";
    public DateTime UpdatedUtc { get; set; }
}
public sealed record SaveBook(BookTitle? Book, string Status);
public sealed record ReadingStatus(string Status);
public sealed record BookDetails(string? Id);
public sealed class BookRating
{
    public long EntryId { get; set; }
    public required string AccountId { get; set; }
    public int Stars { get; set; }
}
public sealed record RateBook(int? Stars);

public static class BookEndpoints
{
    private static IQueryable<ReadingEntry> Visible(AuthDbContext db, string? pair, string id) => db.Set<ReadingEntry>()
        .Where(x => pair != null && x.PairId == pair && (x.Status == "read" || x.AddedBy == id ||
            db.Set<SharingGrant>().Any(g => g.PairId == pair && g.SenderId == x.AddedBy && g.Category == "books" && g.Status == "accepted")));
    private static Task<string?> Pair(AuthDbContext db,ClaimsPrincipal user) => db.PairMembers
        .Where(x => x.AccountId == user.FindFirstValue(ClaimTypes.NameIdentifier)).Select(x => x.PairId).SingleOrDefaultAsync();
    public static void MapBooks(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/books").RequireAuthorization();
        group.MapPost("/details", async (BookDetails request, BookCatalog catalog, CancellationToken ct) =>
        {
            if (!BookCatalog.ValidWorkId(request.Id)) return Results.BadRequest();
            try { return Results.Ok(new { description = await catalog.Summary(request.Id!, ct) }); }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
            { return Results.Ok(new { description = (string?)null }); }
            catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
            { return Results.Problem(statusCode:503,title:"The book summary is unavailable right now. Please try again."); }
        }).RequireRateLimiting("picks");
        group.MapGet("/shelf",async (ClaimsPrincipal user,AuthDbContext db) =>
        {
            var pair = await Pair(db,user);
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var shelf = await Visible(db,pair,id).AsNoTracking().OrderByDescending(x => x.UpdatedUtc).ThenByDescending(x => x.Id).ToListAsync();
            var ratings = await db.Set<BookRating>().AsNoTracking().Where(r => db.Set<ReadingEntry>().Any(e => e.Id == r.EntryId && pair != null && e.PairId == pair)).ToListAsync();
            return Results.Ok(new {connected=pair is not null,sharingBooks=await PrivacyEndpoints.Allowed(db,pair,id,"books"),entries=shelf.Select(x => new {x.Id,x.Status,x.UpdatedUtc,mine=x.AddedBy == id,
                ownRating=ratings.Where(r => r.EntryId == x.Id && r.AccountId == id).Select(r => (int?)r.Stars).SingleOrDefault(),
                partnerRating=ratings.Where(r => r.EntryId == x.Id && r.AccountId != id).Select(r => (int?)r.Stars).SingleOrDefault(),
                book=JsonSerializer.Deserialize<BookTitle>(x.Content)})});
        }).RequireRateLimiting("picks");
        group.MapPost("/shelf/{entryId:long}/rating",async (long entryId,RateBook request,ClaimsPrincipal user,AuthDbContext db) =>
        {
            if (request.Stars is < 1 or > 5) return Results.Problem(statusCode:400,title:"Choose between one and five stars.");
            var pair=await Pair(db,user);
            if (!await db.Set<ReadingEntry>().AnyAsync(x => x.Id == entryId && pair != null && x.PairId == pair && x.Status == "read"))
                return Results.NotFound();
            var id=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var rows=db.Set<BookRating>().Where(x => x.EntryId == entryId && x.AccountId == id);
            if (request.Stars is null) { await rows.ExecuteDeleteAsync(); return Results.NoContent(); }
            if (await rows.ExecuteUpdateAsync(s => s.SetProperty(x => x.Stars,request.Stars.Value)) == 1) return Results.NoContent();
            db.Add(new BookRating {EntryId=entryId,AccountId=id,Stars=request.Stars.Value});
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                if (await rows.ExecuteUpdateAsync(s => s.SetProperty(x => x.Stars,request.Stars.Value)) != 1) throw;
            }
            return Results.NoContent();
        }).RequireRateLimiting("picks");
        group.MapPost("/search",async (BookSearch request,ClaimsPrincipal user,AuthDbContext db,BookCatalog catalog,CancellationToken ct) =>
        {
            if (!BookCatalog.Valid(request)) return Results.Problem(statusCode:400,title:"Choose valid book search filters.");
            var pair = await Pair(db,user);
            BookTitle? seed = null;
            if (request.BasedOn is not null)
            {
                var entry = await db.Set<ReadingEntry>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.BasedOn && pair != null && x.PairId == pair && x.Status == "read",ct);
                if (entry is null) return Results.Problem(statusCode:409,title:"Choose a book from your shared read history.");
                seed = JsonSerializer.Deserialize<BookTitle>(entry.Content);
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var results = await catalog.Search(request,seed,deadline.Token);
                var read = await db.Set<ReadingEntry>().Where(x => pair != null && x.PairId == pair && x.Status == "read").Select(x => x.BookId).ToListAsync(ct);
                return Results.Ok(results with {Books=results.Books.Where(x => !read.Contains(x.Id) || !string.IsNullOrWhiteSpace(request.Query)).ToArray()});
            }
            catch (ArgumentException e) { return Results.Problem(statusCode:400,title:e.Message); }
            catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
            { return Results.Problem(statusCode:503,title:"The book catalogue is unavailable right now. Please try again later."); }
        }).RequireRateLimiting("watch");
        group.MapPost("/recommended",async (ClaimsPrincipal user,AuthDbContext db,BookCatalog catalog,CancellationToken ct) =>
        {
            var pair=await Pair(db,user); var id=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var entries=await Visible(db,pair,id).AsNoTracking().ToListAsync(ct);
            var ratings=await db.Set<BookRating>().Where(x => x.AccountId == id && db.Set<ReadingEntry>().Any(e => e.Id == x.EntryId && e.PairId == pair)).ToDictionaryAsync(x => x.EntryId,x => x.Stars,ct);
            var seeds=BookRecommendations.Seeds(entries,ratings);
            if (seeds.Length == 0) return Results.Ok(new {books=Array.Empty<BookTitle>(),reasons=new Dictionary<string,string>(),explanation="Mark a book as read together or rate one four or five stars to get recommendations."});
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var candidates=new List<(BookTitle Book,string Reason)>();
                foreach(var seed in seeds)
                {
                    var results=await catalog.Search(new BookSearch(null),seed.Book,deadline.Token);
                    candidates.AddRange(results.Books.Select(b => (b,(seed.Stars is >= 4 ? $"You rated {seed.Book.Title} {seed.Stars}/5. " : "") + results.Explanation)));
                }
                var excluded=entries.Select(x => x.BookId).ToHashSet();
                var matches=candidates.Where(x => !excluded.Contains(x.Book.Id)).DistinctBy(x => x.Book.Id).Take(20).ToArray();
                return Results.Ok(new {books=matches.Select(x => x.Book),reasons=matches.ToDictionary(x => x.Book.Id,x => x.Reason),explanation="Inspired by your own ratings and books you've read together. Matches use catalogue genres or authors."});
            }
            catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
            { return Results.Problem(statusCode:503,title:"Book recommendations are unavailable right now. Please try again."); }
        }).RequireRateLimiting("watch");
        group.MapPost("/shelf",async (SaveBook request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock) =>
        {
            if (request.Book is null || !BookCatalog.Valid(request.Book) || request.Status is not ("planned" or "read")) return Results.BadRequest();
            var pair = await Pair(db,user);
            if (pair is null) return Results.Problem(statusCode:409,title:"Connect with your person to save books together.");
            var book = request.Book;
            var owner=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var scope=request.Status == "read" ? "read" : owner;
            if (await db.Set<ReadingEntry>().AnyAsync(x => x.PairId == pair && x.BookId == book.Id && x.Scope == scope))
                return Results.NoContent();
            if (await db.Set<ReadingEntry>().CountAsync(x => x.PairId == pair) >= 1000)
                return Results.Problem(statusCode:409,title:"Your shared bookshelf is full. Remove an entry first.");
            db.Add(new ReadingEntry {PairId=pair,AddedBy=owner,Scope=scope,BookId=book.Id,Content=JsonSerializer.Serialize(book),Status=request.Status,UpdatedUtc=clock.GetUtcNow().UtcDateTime});
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                if (await db.Set<ReadingEntry>().AsNoTracking().AnyAsync(x => x.PairId == pair && x.BookId == book.Id && x.Scope == scope)) return Results.NoContent();
                throw;
            }
            return Results.NoContent();
        }).RequireRateLimiting("picks");
        group.MapPost("/shelf/{entryId:long}/status",async (long entryId,ReadingStatus request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock) =>
        {
            if (request.Status is not ("planned" or "read")) return Results.BadRequest();
            var pair = await Pair(db,user);
            var id=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await using var transaction=await db.Database.BeginTransactionAsync();
            var entry=await Visible(db,pair,id).SingleOrDefaultAsync(x => x.Id == entryId);
            if(entry is null) return Results.NotFound();
            var scope=request.Status == "read" ? "read" : id;
            if(entry.Status == request.Status && (entry.Status == "read" || entry.AddedBy == id)) return Results.NoContent();
            var target=await db.Set<ReadingEntry>().SingleOrDefaultAsync(x => x.PairId == pair && x.BookId == entry.BookId && x.Scope == scope);
            if(target is null)
            {
                if(entry.Status == "planned" && entry.AddedBy != id && request.Status == "planned")
                    db.Add(new ReadingEntry {PairId=entry.PairId,AddedBy=id,Scope=id,BookId=entry.BookId,Content=entry.Content,Status="planned",UpdatedUtc=clock.GetUtcNow().UtcDateTime});
                else { entry.Status=request.Status; entry.Scope=scope; entry.AddedBy=id; entry.UpdatedUtc=clock.GetUtcNow().UtcDateTime; }
            }
            else if(entry.AddedBy == id || entry.Status == "read") db.Remove(entry);
            try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return Results.Problem(statusCode:409,title:"This book changed while you were updating it. Refresh and try again.");
            }
            return Results.NoContent();
        }).RequireRateLimiting("picks");
        group.MapPost("/shelf/{entryId:long}/remove",async (long entryId,ClaimsPrincipal user,AuthDbContext db) =>
        {
            var pair = await Pair(db,user);
            var id=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var count = await db.Set<ReadingEntry>().Where(x => x.Id == entryId && pair != null && x.PairId == pair && (x.Status == "read" || x.AddedBy == id)).ExecuteDeleteAsync();
            return count == 1 ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("picks");
    }
}
