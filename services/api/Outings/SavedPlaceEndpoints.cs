using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Outings;

public sealed class SavedPlace
{
    public long Id { get; set; }
    public required string PairId { get; set; }
    public required string AuthorId { get; set; }
    public required string Name { get; set; }
    public required string Location { get; set; }
    public string? Link { get; set; }
    public string? Note { get; set; }
    public string? IdentityJson { get; set; }
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; }
}
public sealed record SavePlaceRequest(string? Name, string? Location, string? Link, string? Note, string? Revision, string? SelectionToken = null, bool ClearIdentity = false);
public sealed record RemovePlaceRequest(string? Revision);
public static class SavedPlaceEndpoints
{
    public static void Validate(SavePlaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 160 || string.IsNullOrWhiteSpace(request.Location) || request.Location.Length > 200)
            throw new ArgumentException("Add a place name and its city or area.");
        if (request.SelectionToken?.Length > 30000) throw new ArgumentException("Search for the place again.");
        if (request.Note?.Length > 1000 || request.Link?.Length > 2000)
            throw new ArgumentException("Use a note up to 1,000 characters and a link up to 2,000 characters.");
        if (!string.IsNullOrWhiteSpace(request.Link) && (!Uri.TryCreate(request.Link.Trim(), UriKind.Absolute, out var link) || link.Scheme is not ("https" or "http") || link.UserInfo.Length != 0))
            throw new ArgumentException("Use a full http or https link without embedded credentials.");
    }
    private static IQueryable<SavedPlace> Visible(AuthDbContext db, string account) => db.Set<SavedPlace>().Where(x => db.PairMembers.Any(m => m.AccountId == account && m.PairId == x.PairId));
    public static void MapSavedPlaces(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/saved-places").RequireAuthorization().RequireRateLimiting("picks");
        group.MapGet("/search", async (string query, string language, SavedPlaceCatalog catalog, CancellationToken ct) => {
            try { return Results.Ok(await catalog.Search(query,language,ct)); }
            catch(ArgumentException e){return Results.Problem(statusCode:400,title:e.Message);}
            catch(Exception e) when(e is HttpRequestException or JsonException or TaskCanceledException){return Results.Problem(statusCode:503,title:"Place search is unavailable. Try again or add the place manually.");}
        });
        group.MapGet("", async (ClaimsPrincipal user, AuthDbContext db, CancellationToken ct) => {
            var account = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var connected = await db.PairMembers.AnyAsync(x => x.AccountId == account, ct);
            var items = await Visible(db, account).AsNoTracking().OrderByDescending(x => x.CreatedUtc).Take(500).ToArrayAsync(ct);
            return Results.Ok(new { connected, items = items.Select(x => new { x.Id, x.Name, x.Location, x.Link, x.Note, x.Revision, identity = x.IdentityJson is null ? null : JsonSerializer.Deserialize<PlaceIdentity>(x.IdentityJson), mine = x.AuthorId == account }) });
        });
        async Task<IResult> Save(long? id, SavePlaceRequest request, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock, SavedPlaceCatalog catalog, CancellationToken ct)
        {
            try { Validate(request); } catch (ArgumentException e) { return Results.Problem(statusCode:400, title:e.Message); }
            string? identity=null;
            if(request.SelectionToken is not null){try{identity=JsonSerializer.Serialize(catalog.Read(request.SelectionToken));}catch(ArgumentException e){return Results.Problem(statusCode:400,title:e.Message);}}
            var account = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair = await db.PairMembers.Where(x => x.AccountId == account).Select(x => x.PairId).SingleOrDefaultAsync(ct);
            if (pair is null) return Results.Problem(statusCode:409, title:"Connect with your person to save places together.");
            var name = request.Name!.Trim(); var location = request.Location!.Trim();
            var link = string.IsNullOrWhiteSpace(request.Link) ? null : request.Link.Trim();
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (id is {} existing) {
                if (!await Visible(db, account).AnyAsync(x => x.Id == existing, ct)) return Results.NotFound();
                var previous=await Visible(db,account).Where(x=>x.Id==existing).Select(x=>x.IdentityJson).SingleAsync(ct);
                var snapshot=request.ClearIdentity ? null : identity ?? previous;
                var revision = Guid.NewGuid().ToString("N");
                var changed = await Visible(db, account).Where(x => x.Id == existing && x.Revision == request.Revision)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, name).SetProperty(x => x.Location, location).SetProperty(x => x.Link, link).SetProperty(x => x.Note, note).SetProperty(x=>x.IdentityJson,snapshot).SetProperty(x => x.Revision, revision), ct);
                return changed == 1 ? Results.NoContent() : Results.Problem(statusCode:409, title:"This place changed. Refresh before saving.");
            }
            if (await db.Set<SavedPlace>().CountAsync(x => x.PairId == pair, ct) >= 500) return Results.Problem(statusCode:409, title:"Your list is full. Remove a place before adding another.");
            var place = new SavedPlace { PairId=pair, AuthorId=account, Name=name, Location=location, Link=link, Note=note, IdentityJson=identity, CreatedUtc=clock.GetUtcNow().UtcDateTime };
            db.Add(place); await db.SaveChangesAsync(ct); return Results.Ok(new { place.Id });
        }
        group.MapPost("", (SavePlaceRequest request, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock, SavedPlaceCatalog catalog, CancellationToken ct) => Save(null, request, user, db, clock, catalog, ct));
        group.MapPost("/{id:long}", (long id, SavePlaceRequest request, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock, SavedPlaceCatalog catalog, CancellationToken ct) => Save(id, request, user, db, clock, catalog, ct));
        group.MapPost("/{id:long}/remove", async (long id, RemovePlaceRequest request, ClaimsPrincipal user, AuthDbContext db, CancellationToken ct) => {
            var account = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await Visible(db, account).AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            return await Visible(db, account).Where(x => x.Id == id && x.Revision == request.Revision).ExecuteDeleteAsync(ct) == 1
                ? Results.NoContent() : Results.Problem(statusCode:409, title:"This place changed. Refresh before removing it.");
        });
    }
}
