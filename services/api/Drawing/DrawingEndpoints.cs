using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;

namespace Overlap.Api.Drawing;

public sealed record NewDrawing(int Seconds);
public sealed record DrawingAction(string? Revision);
public sealed record DrawingUpload(string? Revision, string? Pixels);
public sealed record DrawingScore(string? Revision, string? PhotoAccount, string? PhotoRevision, int? Score);

public static class DrawingEndpoints
{
    private static string Account(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static IQueryable<DrawingRound> Visible(AuthDbContext db, string account) => db.Set<DrawingRound>()
        .Where(x => db.PairMembers.Any(m => m.AccountId == account && m.PairId == x.PairId));
    private static IResult Conflict(string message) => Results.Problem(statusCode: 409, title: message);
    private static DateTime? Utc(DateTime? date) => date is null ? null : DateTime.SpecifyKind(date.Value, DateTimeKind.Utc);
    public static void MapDrawing(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/drawing").RequireAuthorization().RequireRateLimiting("picks");
        group.MapGet("", async (ClaimsPrincipal user, AuthDbContext db, TimeProvider clock, CancellationToken ct) => {
            var account = Account(user);
            var rounds = await Visible(db, account).AsNoTracking().OrderByDescending(x => x.Id).Take(100).ToArrayAsync(ct);
            var ids = rounds.Select(x => x.Id).ToArray();
            var photos = await db.Set<DrawingPhoto>().AsNoTracking().Where(x => ids.Contains(x.RoundId))
                .Select(x => new { x.RoundId, x.AccountId, x.Revision, x.UploadedUtc, x.OwnerRating, x.PartnerRating }).ToArrayAsync(ct);
            return Results.Ok(new {
                connected = await db.PairMembers.AnyAsync(x => x.AccountId == account, ct),
                serverUtc = clock.GetUtcNow(),
                rounds = rounds.Select(x => new { x.Id, x.Prompt, x.Seconds, x.Revision, mine = x.CreatedBy == account,
                    status = DrawingRules.Status(x, clock.GetUtcNow().UtcDateTime), createdUtc = Utc(x.CreatedUtc), startUtc = Utc(x.StartUtc), endUtc = Utc(x.EndUtc),
                    photos = photos.Where(p => p.RoundId == x.Id).Select(p => new { p.AccountId, p.Revision, uploadedUtc = Utc(p.UploadedUtc), mine = p.AccountId == account,
                        myRating = p.AccountId == account ? p.OwnerRating : p.PartnerRating,
                        partnerRating = p.AccountId == account ? p.PartnerRating : p.OwnerRating }) }) });
        });
        group.MapPost("", async (NewDrawing request, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock, CancellationToken ct) => {
            if (!DrawingRules.ValidSeconds(request.Seconds)) return Results.Problem(statusCode: 400, title: "Choose a timer between 1 second and 60 minutes.");
            var account = Account(user);
            var pair = await db.PairMembers.Where(x => x.AccountId == account).Select(x => x.PairId).SingleOrDefaultAsync(ct);
            if (pair is null) return Conflict("Connect with your person to draw together.");
            // Serializable prevents two concurrent creations from producing two active rounds.
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            if (await db.Set<DrawingRound>().AnyAsync(x => x.PairId == pair && !x.Cancelled && (x.EndUtc == null || x.EndUtc > now), ct))
                return Conflict("You already have a drawing round. Join it or cancel it first.");
            if (await db.Set<DrawingRound>().CountAsync(x => x.PairId == pair, ct) >= 100)
                return Conflict("Your drawing album has 100 rounds. Remove an old round first.");
            var round = new DrawingRound { PairId = pair, CreatedBy = account, Prompt = DrawingRules.Shuffle(), Seconds = request.Seconds, CreatedUtc = now };
            db.Add(round); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { round.Id });
        });
        group.MapPost("/{id:long}/{action}", async (long id, string action, DrawingAction request, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock, CancellationToken ct) => {
            if (action is not ("shuffle" or "join" or "cancel" or "remove")) return Results.NotFound();
            var account = Account(user);
            var round = await Visible(db, account).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (round is null) return Results.NotFound();
            var status = DrawingRules.Status(round, clock.GetUtcNow().UtcDateTime);
            var current = Visible(db, account).Where(x => x.Id == id && x.Revision == request.Revision);
            if (action == "remove") {
                if (status is "waiting" or "drawing") return Conflict("Cancel this round before removing it.");
                return await current.ExecuteDeleteAsync(ct) == 1 ? Results.NoContent() : Conflict("This round changed. Refresh and try again.");
            }
            if (action == "cancel") {
                if (status is "finished" or "cancelled") return Conflict("This round has already ended.");
                var cancelledRevision = Guid.NewGuid().ToString("N");
                return await current.ExecuteUpdateAsync(s => s.SetProperty(x => x.Cancelled, true).SetProperty(x => x.Revision, cancelledRevision), ct) == 1
                    ? Results.NoContent() : Conflict("This round changed. Refresh and try again.");
            }
            if (status != "waiting") return Conflict("This round has already started.");
            if (action == "shuffle") {
                if (round.CreatedBy != account) return Results.Problem(statusCode: 403, title: "Only the person who proposed this round can shuffle before it starts.");
                var prompt = DrawingRules.Shuffle(round.Prompt); var revision = Guid.NewGuid().ToString("N");
                return await current.ExecuteUpdateAsync(s => s.SetProperty(x => x.Prompt, prompt).SetProperty(x => x.Revision, revision), ct) == 1
                    ? Results.NoContent() : Conflict("This round changed. Refresh and try again.");
            }
            if (round.CreatedBy == account) return Conflict("Your person starts the timer by joining.");
            var start = clock.GetUtcNow().UtcDateTime; var end = start.AddSeconds(round.Seconds); var next = Guid.NewGuid().ToString("N");
            return await current.ExecuteUpdateAsync(s => s.SetProperty(x => x.StartUtc, start).SetProperty(x => x.EndUtc, end).SetProperty(x => x.Revision, next), ct) == 1
                ? Results.NoContent() : Conflict("This round changed. Refresh and try again.");
        });
        group.MapGet("/{id:long}/photos/{photoAccount}", async (long id, string photoAccount, ClaimsPrincipal user, AuthDbContext db, CancellationToken ct) => {
            if (!await Visible(db, Account(user)).AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var png = await db.Set<DrawingPhoto>().Where(x => x.RoundId == id && x.AccountId == photoAccount).Select(x => x.Png).SingleOrDefaultAsync(ct);
            return png is null ? Results.NotFound() : Results.File(png, "image/png");
        });
        group.MapPost("/{id:long}/photo", async (long id, DrawingUpload request, ClaimsPrincipal user, AuthDbContext db, TimeProvider clock, CancellationToken ct) => {
            var account = Account(user);
            var round = await Visible(db, account).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (round is null) return Results.NotFound();
            if (DrawingRules.Status(round, clock.GetUtcNow().UtcDateTime) != "finished") return Conflict("Wait for the timer to finish before sharing your drawing.");
            byte[]? png = null;
            if (request.Pixels is not null) {
                if (request.Pixels.Length != 512 * 512 * 4) return Results.BadRequest();
                try { png = AvatarImage.EncodeSquare(Convert.FromBase64String(request.Pixels), 512); }
                catch (Exception e) when (e is ArgumentException or FormatException) { return Results.BadRequest(); }
            }
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var revision = Guid.NewGuid().ToString("N");
            if (await Visible(db, account).Where(x => x.Id == id && x.Revision == request.Revision).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revision, revision), ct) != 1)
                return Conflict("This round changed. Refresh and try again.");
            var photo = await db.Set<DrawingPhoto>().SingleOrDefaultAsync(x => x.RoundId == id && x.AccountId == account, ct);
            if (photo is not null) db.Remove(photo);
            // Replacing the photo clears old scores; they belong to that particular drawing.
            await db.SaveChangesAsync(ct);
            if (png is not null) db.Add(new DrawingPhoto { RoundId = id, AccountId = account, Png = png, UploadedUtc = clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return Results.NoContent();
        });
        group.MapPost("/{id:long}/rating", async (long id, DrawingScore request, ClaimsPrincipal user, AuthDbContext db, CancellationToken ct) => {
            if (request.Score is < 1 or > 5) return Results.BadRequest();
            var account = Account(user);
            if (!await Visible(db, account).AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var revision = Guid.NewGuid().ToString("N");
            if (await Visible(db, account).Where(x => x.Id == id && x.Revision == request.Revision).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revision, revision), ct) != 1)
                return Conflict("This round changed. Refresh and try again.");
            var photo = db.Set<DrawingPhoto>().Where(x => x.RoundId == id && x.AccountId == request.PhotoAccount && x.Revision == request.PhotoRevision);
            var changed = request.PhotoAccount == account
                ? await photo.ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerRating, request.Score), ct)
                : await photo.ExecuteUpdateAsync(s => s.SetProperty(x => x.PartnerRating, request.Score), ct);
            if (changed != 1) return Conflict("This photo changed. Refresh before rating it.");
            await transaction.CommitAsync(ct); return Results.NoContent();
        });
    }
}
