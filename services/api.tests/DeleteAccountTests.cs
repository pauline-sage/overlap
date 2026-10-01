using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Outings;
using Overlap.Api.Watch;
using Xunit;
namespace Overlap.Api.Tests;
public sealed partial class TogetherTests
{
    [Fact]
    public async Task DeletionRequiresConfirmationAndPasswordAndPreservesPartner()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);
        using var first=factory.Client();using var second=factory.Client();using var otherSession=factory.Client();
        var id=await SignIn(factory,first,"delete-me");var partner=await SignIn(factory,second,"keep-me");
        (await Post(otherSession,"login",new {email="delete-me@example.test",password="abcdefghij"})).EnsureSuccessStatusCode();
        (await Post(second,"together/accept",new {token=await Invite(first)})).EnsureSuccessStatusCode();
        using(var scope=factory.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var pair=await db.PairMembers.Where(x=>x.AccountId==id).Select(x=>x.PairId).SingleAsync();
            db.Set<SharedPick>().AddRange(
                new SharedPick{ProposedBy=id,PairId=pair,IsShared=true,TitleId="mine",Content="{}"},
                new SharedPick{ProposedBy=partner,PairId=pair,IsShared=true,TitleId="theirs",Content="{}"});
            db.Set<OutingUpdate>().Add(new OutingUpdate{PairId=pair,SenderId=partner});
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await first.PostAsJsonAsync("/api/auth/delete-account",new {password="abcdefghij",confirm=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"delete-account",new {password="abcdefghij",confirm=false})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"delete-account",new {password="wrong",confirm=true})).StatusCode);
        (await Post(first,"delete-account",new {password="abcdefghij",confirm=true})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized,(await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await otherSession.GetAsync("/api/auth/me")).StatusCode);
        var state=await second.GetFromJsonAsync<JsonElement>("/api/auth/together/");
        Assert.Equal(JsonValueKind.Null,state.GetProperty("partner").ValueKind);
        using var finalScope=factory.Services.CreateScope();
        var finalDb=finalScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.False(await finalDb.Users.AnyAsync(x=>x.Id==id));
        Assert.True(await finalDb.Users.AnyAsync(x=>x.Id==partner));
        Assert.Empty(await finalDb.PairMembers.ToListAsync());
        Assert.Empty(await finalDb.Set<OutingUpdate>().ToListAsync());
        var kept=await finalDb.Set<SharedPick>().SingleAsync();
        Assert.Equal(partner,kept.ProposedBy);Assert.False(kept.IsShared);Assert.Null(kept.PairId);
    }
}
