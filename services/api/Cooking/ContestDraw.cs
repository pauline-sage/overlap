using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Cooking;
public sealed class ContestRecipeDraw
{
    public required string PairId { get; set; }
    public long SourceId { get; set; }
    public long ContestId { get; set; }
    public required string Content { get; set; }
}
public static class ContestDraw
{
    public static async Task<IResult> Pick(CookingContest contest,string account,bool redraw,AuthDbContext db,TimeProvider clock,CancellationToken ct)
    {
        if(await db.Set<CookingMemory>().AnyAsync(x=>x.ContestId==contest.Id,ct))return Results.Problem(statusCode:409,title:"This recipe is already in your album. Start a new contest for another recipe.");
        var preferences=await FoodPreferencesEndpoints.ForPair(db,contest.PairId,ct);
        var senderRow=preferences.SingleOrDefault(x=>x.AccountId==contest.ProposedBy);var recipientRow=preferences.SingleOrDefault(x=>x.AccountId!=contest.ProposedBy);
        var sender=FoodPreferencesEndpoints.Read(senderRow);var recipient=FoodPreferencesEndpoints.Read(recipientRow);
        if(sender is null || recipient is null)return Results.Problem(statusCode:409,title:"Both of you need to save food preferences before shuffling.");
        var recipes=await db.Set<SavedRecipe>().AsNoTracking().Where(x=>x.PairId==contest.PairId
            && !db.Set<ContestRecipeDraw>().Any(d=>d.PairId==x.PairId && d.SourceId==x.SourceId)
            && !db.Set<RecipeExperience>().Any(e=>e.RecipeId==x.Id)).ToListAsync(ct);
        recipes=recipes.Where(x=>ContestRecipeFilter.Allows(JsonSerializer.Deserialize<Recipe>(x.Content)!,sender,recipient)).ToList();
        if(recipes.Count==0)return Results.Problem(statusCode:409,title:"No new recipes match your preferences. Add suitable recipes to your shared collection.");
        var selected=recipes[System.Security.Cryptography.RandomNumberGenerator.GetInt32(recipes.Count)];var revision=Guid.NewGuid().ToString("N");var now=clock.GetUtcNow().UtcDateTime;
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var changed=await db.Set<CookingContest>().Where(x=>x.Id==contest.Id && x.PairId==contest.PairId && x.Status=="accepted" && x.Revision==contest.Revision
            && !db.Set<CookingMemory>().Any(m=>m.ContestId==x.Id)
            && (redraw ? x.RedrawRequestedBy!=null && x.RedrawRequestedBy!=account : x.RevealedUtc==null)
            && db.Set<FoodPreference>().Any(p=>p.AccountId==senderRow!.AccountId && p.Revision==senderRow.Revision)
            && db.Set<FoodPreference>().Any(p=>p.AccountId==recipientRow!.AccountId && p.Revision==recipientRow.Revision))
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SenderPreferences,senderRow!.Content).SetProperty(x=>x.RecipientPreferences,recipientRow!.Content)
                .SetProperty(x=>x.RecipeContent,selected.Content).SetProperty(x=>x.RevealedUtc,now).SetProperty(x=>x.Revision,revision).SetProperty(x=>x.RedrawRequestedBy,(string?)null)
                .SetProperty(x=>x.SenderUnread,contest.ProposedBy!=account).SetProperty(x=>x.RecipientUnread,contest.ProposedBy==account),ct);
        if(changed==0){await transaction.RollbackAsync(ct);return !redraw && await db.Set<CookingContest>().AnyAsync(x=>x.Id==contest.Id && x.RevealedUtc!=null,ct) ? Results.NoContent() : Results.Problem(statusCode:409,title:"This contest has changed. Refresh and try again.");}
        db.Add(new ContestRecipeDraw{PairId=contest.PairId,SourceId=selected.SourceId,ContestId=contest.Id,Content=selected.Content});
        try{await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);}
        catch(DbUpdateException){await transaction.RollbackAsync(ct);if(!await db.Set<ContestRecipeDraw>().AsNoTracking().AnyAsync(x=>x.PairId==contest.PairId && x.SourceId==selected.SourceId,ct))throw;return Results.Problem(statusCode:409,title:"That recipe was just drawn in another contest. Try again for a new one.");}
        return Results.NoContent();
    }
}
