using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Overlap.Api.Outings;
using Overlap.Api.Watch;
namespace Overlap.Api.Auth;

public sealed record DeleteAccountRequest(string? Password, bool Confirm);
public static class DeleteAccountEndpoints
{
    public static void MapAccountDeletion(this WebApplication app)
    {
        app.MapPost("/api/auth/delete-account", async (DeleteAccountRequest request, ClaimsPrincipal principal,
            UserManager<Account> users, SignInManager<Account> signIn, AuthDbContext db, OnlinePresence presence) =>
        {
            if (!request.Confirm || request.Password is not { Length: > 0 and <= 128 })
                return Results.Problem(statusCode:400,title:"Confirm deletion and enter your current password.");
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var check = await signIn.CheckPasswordSignInAsync(user,request.Password,lockoutOnFailure:true);
            if (!check.Succeeded) return Results.Problem(statusCode:400,title:"Unable to delete your account. Check your password or try again later.");
            await using var transaction = await db.Database.BeginTransactionAsync();
            var pair = await db.PairMembers.Where(x=>x.AccountId==user.Id).Select(x=>x.PairId).SingleOrDefaultAsync();
            if(pair is not null) {
                await db.Set<SharedPick>().Where(x=>x.PairId==pair).ExecuteUpdateAsync(s=>s
                    .SetProperty(x=>x.PairId,(string?)null).SetProperty(x=>x.IsShared,false)
                    .SetProperty(x=>x.Status,"pending").SetProperty(x=>x.SenderHasNews,false).SetProperty(x=>x.RecipientHasNews,false));
                await db.Set<OutingUpdate>().Where(x=>x.PairId==pair).ExecuteDeleteAsync();
                await db.Set<WatchedTitle>().Where(x=>x.PairId==pair).ExecuteDeleteAsync();
                await db.Set<Overlap.Api.Books.ReadingEntry>().Where(x=>x.PairId==pair).ExecuteDeleteAsync();
                await db.Set<SharingGrant>().Where(x=>x.PairId==pair).ExecuteDeleteAsync();
                await db.PairMembers.Where(x=>x.PairId==pair).ExecuteDeleteAsync();
            }
            var deleted = await users.DeleteAsync(user);
            if(!deleted.Succeeded) return Results.Problem(statusCode:409,title:"Your account changed. Refresh and try again.");
            await transaction.CommitAsync();
            presence.Leave(user.Id);
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("auth");
    }
}
