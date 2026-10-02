using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Auth;
namespace Overlap.Api.Cooking;
public sealed class FoodPreference
{
    public required string AccountId { get; set; }
    public required string Content { get; set; }
    public bool ShareWithPartner { get; set; }
    public string? SharedPairId { get; set; }
    public string Revision { get; set; }=Guid.NewGuid().ToString("N");
}
public sealed record SaveFoodPreference(bool AvoidRedMeat,string[] ExcludedIngredients,bool ShareWithPartner=false);
public static class FoodPreferencesEndpoints
{
    public static async Task<FoodPreference[]> ForPair(AuthDbContext db,string pair,CancellationToken ct)=>await db.Set<FoodPreference>().AsNoTracking().Where(x=>db.PairMembers.Any(m=>m.PairId==pair && m.AccountId==x.AccountId)).ToArrayAsync(ct);
    public static ContestPreferences? Read(FoodPreference? preference)=>preference is null ? null : JsonSerializer.Deserialize<ContestPreferences>(preference.Content);
    public static void MapFoodPreferences(this WebApplication app)
    {
        var group=app.MapGroup("/api/auth/cooking/preferences").RequireAuthorization().RequireRateLimiting("picks");
        group.MapGet("",async(ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair=await db.PairMembers.Where(x=>x.AccountId==account).Select(x=>x.PairId).SingleOrDefaultAsync(ct);
            var own=await db.Set<FoodPreference>().AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account,ct);
            var partner=pair is null ? null : (await ForPair(db,pair,ct)).SingleOrDefault(x=>x.AccountId!=account);
            return Results.Ok(new{ownPreferences=Read(own),shareWithPartner=pair!=null && own?.ShareWithPartner==true && own.SharedPairId==pair,partnerConfirmed=partner!=null,partnerShared=partner?.ShareWithPartner==true && partner.SharedPairId==pair,partnerPreferences=partner?.ShareWithPartner==true && partner.SharedPairId==pair ? Read(partner) : null});
        });
        group.MapPost("",async(SaveFoodPreference request,ClaimsPrincipal user,AuthDbContext db,CancellationToken ct)=>{
            if(request.ExcludedIngredients is null || request.ExcludedIngredients.Length>12 || request.ExcludedIngredients.Any(x=>string.IsNullOrWhiteSpace(x) || x.Length>50))return Results.Problem(statusCode:400,title:"Enter up to twelve ingredients, each up to fifty characters.");
            var account=user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var pair=await db.PairMembers.Where(x=>x.AccountId==account).Select(x=>x.PairId).SingleOrDefaultAsync(ct);
            if(request.ShareWithPartner && pair is null)return Results.Problem(statusCode:409,title:"Connect with your person before sharing your list.");
            var sharedPair=request.ShareWithPartner ? pair : null;
            var content=JsonSerializer.Serialize(new ContestPreferences(request.AvoidRedMeat,request.ExcludedIngredients.Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            if(content.Length>2000)return Results.Problem(statusCode:400,title:"Use shorter ingredient names.");
            var revision=Guid.NewGuid().ToString("N");var rows=db.Set<FoodPreference>().Where(x=>x.AccountId==account);
            if(await rows.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Content,content).SetProperty(x=>x.ShareWithPartner,request.ShareWithPartner).SetProperty(x=>x.SharedPairId,sharedPair).SetProperty(x=>x.Revision,revision),ct)==0){
                db.Add(new FoodPreference{AccountId=account,Content=content,ShareWithPartner=request.ShareWithPartner,SharedPairId=sharedPair,Revision=revision});
                try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){if(await rows.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Content,content).SetProperty(x=>x.ShareWithPartner,request.ShareWithPartner).SetProperty(x=>x.Revision,revision),ct)!=1)throw;}
            }
            return Results.NoContent();
        });
    }
}
