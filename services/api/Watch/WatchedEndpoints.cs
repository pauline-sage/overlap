using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;

namespace Overlap.Api.Watch;

public sealed class WatchedTitle
{
    public long Id { get; set; }
    public required string PairId { get; set; }
    public required string AddedBy { get; set; }
    public required string TitleId { get; set; }
    public required string Content { get; set; }
    // When added to history, not an invented viewing date.
    public DateTime AddedUtc { get; set; }
}

public static class WatchedEndpoints
{
    public static void MapWatched(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/watch/watched").RequireAuthorization().RequireRateLimiting("picks");
        group.MapPost("", async (JsonElement title, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock) =>
        {
            if (!PickEndpoints.Valid(title) || title.GetProperty("format").GetString() is not ("Movie" or "Series"))
                return Results.Problem(statusCode:400, title:"Choose a movie or series to mark as watched together.");
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair = await db.PairMembers.Where(x => x.AccountId == id).Select(x => x.PairId).SingleOrDefaultAsync();
            if (pair is null) return Results.Problem(statusCode:409, title:"Connect with your person to keep a shared watch history.");
            var titleId = title.GetProperty("id").GetString()!;
            if (await db.Set<WatchedTitle>().AnyAsync(x => x.PairId == pair && x.TitleId == titleId)) return Results.NoContent();
            if (await db.Set<WatchedTitle>().CountAsync(x => x.PairId == pair) >= 1000)
                return Results.Problem(statusCode:409, title:"Your shared watch history is full. Remove an entry first.");
            db.Add(new WatchedTitle { PairId=pair, AddedBy=id, TitleId=titleId, Content=title.GetRawText(), AddedUtc=clock.GetUtcNow().UtcDateTime });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                if (await db.Set<WatchedTitle>().AsNoTracking().AnyAsync(x => x.PairId == pair && x.TitleId == titleId)) return Results.NoContent();
                throw;
            }
            return Results.NoContent();
        });
        group.MapPost("/{entryId:long}/remove", async (long entryId, ClaimsPrincipal user, AuthDbContext db) =>
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair = await db.PairMembers.Where(x => x.AccountId == id).Select(x => x.PairId).SingleOrDefaultAsync();
            var count = await db.Set<WatchedTitle>().Where(x => x.Id == entryId && pair != null && x.PairId == pair).ExecuteDeleteAsync();
            return count == 1 ? Results.NoContent() : Results.NotFound();
        });
    }
}
