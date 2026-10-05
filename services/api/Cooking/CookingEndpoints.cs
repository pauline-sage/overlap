using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Cooking;

public sealed class SavedRecipe
{
    public long Id { get; set; }
    public required string PairId { get; set; }
    public required string AddedBy { get; set; }
    public long SourceId { get; set; }
    public required string Content { get; set; }
    public DateTime AddedUtc { get; set; }
    public string SharedBy { get; set; } = "";
    public string ShareRevision { get; set; } = "";
    public bool ShareSeen { get; set; }
    public bool ShareDismissed { get; set; }
}
public sealed class RecipeExperience
{
    public long RecipeId { get; set; }
    public required string AccountId { get; set; }
    public DateTime MadeUtc { get; set; }
    public int? Stars { get; set; }
}
public sealed record SetRecipeExperience(bool Made, int? Stars);
public sealed record RecipeQuery(string? Query);
public sealed record RecipeLookup(long Id);
public sealed record RecipeSave(string? Token);
public sealed record RecipeSnapshot(string AccountId, string PairId, Recipe Recipe);
public static class CookingEndpoints
{
    private static Task<string?> Pair(AuthDbContext db, ClaimsPrincipal user, CancellationToken ct) => db.PairMembers
        .Where(x => x.AccountId == user.FindFirstValue(ClaimTypes.NameIdentifier)).Select(x => x.PairId).SingleOrDefaultAsync(ct);
    public static void MapCooking(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/cooking").RequireAuthorization();
        group.MapPost("/search", async (RecipeQuery request, ClaimsPrincipal user, AuthDbContext db, RecipeCatalog catalog, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 120) return Results.Problem(statusCode:400,title:"Enter a dish name (up to 120 characters).");
            try
            {
                var pair = await Pair(db,user,ct);
                var account = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var rows = pair is null
                    ? await db.Set<FoodPreference>().AsNoTracking().Where(x=>x.AccountId==account).ToArrayAsync(ct)
                    : await FoodPreferencesEndpoints.ForPair(db,pair,ct);
                var preferences = rows.Select(FoodPreferencesEndpoints.Read).OfType<ContestPreferences>().ToArray();
                return Results.Ok(await catalog.Search(request.Query.Trim(),preferences,ct));
            }
            catch (Exception e) when (Unavailable(e,ct)) { return Results.Problem(statusCode:503,title:"Recipe search is unavailable. Please try again."); }
        }).RequireRateLimiting("watch");
        group.MapPost("/details", async (RecipeLookup request, ClaimsPrincipal user, AuthDbContext db, RecipeCatalog catalog, IDataProtectionProvider protection, CancellationToken ct) =>
        {
            if (request.Id <= 0) return Results.BadRequest();
            try
            {
                var recipe = await catalog.Details(request.Id,ct);
                if (recipe is null) return Results.Problem(statusCode:422,title:"This page does not have a supported ingredients and instructions section. Choose another recipe.");
                var pair = await Pair(db,user,ct);
                var token = pair is null ? null : protection.CreateProtector("Cooking.RecipeSnapshot.v1").ToTimeLimitedDataProtector().Protect(
                    JsonSerializer.Serialize(new RecipeSnapshot(user.FindFirstValue(ClaimTypes.NameIdentifier)!,pair,recipe)),TimeSpan.FromMinutes(30));
                return Results.Ok(new { recipe, token });
            }
            catch (Exception e) when (Unavailable(e,ct)) { return Results.Problem(statusCode:503,title:"This recipe is unavailable. Please try again."); }
        }).RequireRateLimiting("watch");
        group.MapGet("/collection", async (ClaimsPrincipal user, AuthDbContext db, CancellationToken ct) =>
        {
            var pair = await Pair(db,user,ct);
            var rows = await db.Set<SavedRecipe>().AsNoTracking().Where(x => pair != null && x.PairId == pair).OrderByDescending(x => x.AddedUtc).ToListAsync(ct);
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var partner=await db.PairMembers.Where(x=>pair!=null && x.PairId==pair && x.AccountId!=account).Select(x=>x.AccountId).SingleOrDefaultAsync(ct);
            var partnerCookingShared=partner!=null && await PrivacyEndpoints.Allowed(db,pair,partner,"cooking");
            var experiences=await db.Set<RecipeExperience>().AsNoTracking().Where(x => db.Set<SavedRecipe>().Any(r => r.Id==x.RecipeId && pair!=null && r.PairId==pair) && (x.AccountId==account || db.Set<SharingGrant>().Any(g=>g.PairId==pair && g.SenderId==x.AccountId && g.Category=="cooking" && g.Status=="accepted"))).ToListAsync(ct);
            return Results.Ok(new { connected=pair is not null, partnerCookingShared, entries=rows.Select(x => new {x.Id,x.AddedUtc,
                ownMade=experiences.Any(e=>e.RecipeId==x.Id && e.AccountId==account),
                ownMadeUtc=experiences.FirstOrDefault(e=>e.RecipeId==x.Id && e.AccountId==account) is {} own ? DateTime.SpecifyKind(own.MadeUtc,DateTimeKind.Utc) : (DateTime?)null,
                partnerMadeUtc=experiences.FirstOrDefault(e=>e.RecipeId==x.Id && e.AccountId!=account) is {} partner ? DateTime.SpecifyKind(partner.MadeUtc,DateTimeKind.Utc) : (DateTime?)null,
                partnerMade=experiences.Any(e=>e.RecipeId==x.Id && e.AccountId!=account),
                ownRating=experiences.FirstOrDefault(e=>e.RecipeId==x.Id && e.AccountId==account)?.Stars,
                partnerRating=experiences.FirstOrDefault(e=>e.RecipeId==x.Id && e.AccountId!=account)?.Stars,
                recipe=JsonSerializer.Deserialize<Recipe>(x.Content)}) });
        }).RequireRateLimiting("picks");
        group.MapPost("/collection", async (RecipeSave request, ClaimsPrincipal user, AuthDbContext db, IDataProtectionProvider protection, TimeProvider clock, CancellationToken ct) =>
        {
            var pair = await Pair(db,user,ct);
            if (pair is null) return Results.Problem(statusCode:409,title:"Connect with your person to save recipes together.");
            if (request.Token is null || request.Token.Length > 300_000) return Results.BadRequest();
            RecipeSnapshot? snapshot;
            try { snapshot=JsonSerializer.Deserialize<RecipeSnapshot>(protection.CreateProtector("Cooking.RecipeSnapshot.v1").ToTimeLimitedDataProtector().Unprotect(request.Token)); }
            catch (Exception e) when (e is CryptographicException or JsonException) { return Results.Problem(statusCode:400,title:"This recipe preview expired. Open it again before saving."); }
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (snapshot is null || snapshot.AccountId != account || snapshot.PairId != pair) return Results.BadRequest();
            if (await db.Set<SavedRecipe>().AnyAsync(x => x.PairId==pair && x.SourceId==snapshot.Recipe.Id,ct)) return Results.NoContent();
            if (await db.Set<SavedRecipe>().CountAsync(x => x.PairId==pair,ct)>=500) return Results.Problem(statusCode:409,title:"Your collection is full. Remove a recipe first.");
            db.Add(new SavedRecipe {PairId=pair,AddedBy=account,SourceId=snapshot.Recipe.Id,Content=JsonSerializer.Serialize(snapshot.Recipe),AddedUtc=clock.GetUtcNow().UtcDateTime});
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { if (!await db.Set<SavedRecipe>().AsNoTracking().AnyAsync(x => x.PairId==pair && x.SourceId==snapshot.Recipe.Id,ct)) throw; }
            return Results.NoContent();
        }).RequireRateLimiting("picks");
        group.MapPost("/collection/{id:long}/share", async (long id,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct) =>
        {
            var pair=await Pair(db,user,ct);var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var revision=Guid.NewGuid().ToString("N");
            var changed=await db.Set<SavedRecipe>().Where(x=>x.Id==id && pair!=null && x.PairId==pair)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SharedBy,account).SetProperty(x=>x.ShareRevision,revision).SetProperty(x=>x.ShareSeen,false).SetProperty(x=>x.ShareDismissed,false),ct);
            return changed==1 ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("picks");
        group.MapPost("/collection/{id:long}/experience", async (long id,SetRecipeExperience request,ClaimsPrincipal user,AuthDbContext db,TimeProvider clock,CancellationToken ct) =>
        {
            if(request.Stars is <1 or >5 || !request.Made && request.Stars!=null) return Results.BadRequest();
            var pair=await Pair(db,user,ct);var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if(!await db.Set<SavedRecipe>().AnyAsync(x=>x.Id==id && pair!=null && x.PairId==pair,ct)) return Results.NotFound();
            var rows=db.Set<RecipeExperience>().Where(x=>x.RecipeId==id && x.AccountId==account);
            if(!request.Made){await rows.ExecuteDeleteAsync(ct);return Results.NoContent();}
            if(await rows.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Stars,request.Stars),ct)==1) return Results.Ok(new { madeUtc=DateTime.SpecifyKind(await rows.Select(x=>x.MadeUtc).SingleAsync(ct),DateTimeKind.Utc) });
            db.Add(new RecipeExperience{RecipeId=id,AccountId=account,MadeUtc=clock.GetUtcNow().UtcDateTime,Stars=request.Stars});
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){if(await rows.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Stars,request.Stars),ct)!=1)throw;}
            return Results.Ok(new { madeUtc=DateTime.SpecifyKind(await rows.Select(x=>x.MadeUtc).SingleAsync(ct),DateTimeKind.Utc) });
        }).RequireRateLimiting("picks");
        group.MapPost("/collection/{id:long}/remove", async (long id, ClaimsPrincipal user, AuthDbContext db, CancellationToken ct) =>
        {
            var pair=await Pair(db,user,ct);
            return await db.Set<SavedRecipe>().Where(x => x.Id==id && pair!=null && x.PairId==pair).ExecuteDeleteAsync(ct)==1 ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("picks");
    }
    private static bool Unavailable(Exception e,CancellationToken ct) => e is HttpRequestException or JsonException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException || e is OperationCanceledException && !ct.IsCancellationRequested;
}
