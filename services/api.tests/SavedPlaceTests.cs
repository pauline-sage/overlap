using System.Net;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Outings;
using Xunit;
namespace Overlap.Api.Tests;
public sealed partial class TogetherTests
{
    [Fact]
    public async Task SavedPlacesAreSharedOnlyWithMembersAndRejectStaleWrites()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();using var outsider=factory.Client();
        await SignIn(factory,first,"places-first");await SignIn(factory,second,"places-second");await SignIn(factory,outsider,"places-outsider");
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,"saved-places",new{name="Museum",location="New York",link=(string?)null,note="Dinosaurs"})).StatusCode);
        (await Post(second,"together/accept",new{token=await Invite(first)})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"saved-places",new{name="Museum",location="New York",link="javascript:alert(1)",note=""})).StatusCode);
        using var catalogueScope=factory.Services.CreateScope();
        var catalog=new SavedPlaceCatalog(new HttpClient(new SavedPlaceCatalogTests.Handler()),catalogueScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),TimeProvider.System);
        var selectionToken=JsonSerializer.SerializeToElement(await catalog.Search("Museum New York","en",default))[0].GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"saved-places",new{name="Museum",location="New York",selectionToken="tampered"})).StatusCode);
        var created=await Post(first,"saved-places",new{name="Museum",location="New York, US",link="https://example.test/museum",note="Dinosaurs",selectionToken});created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var shared=await second.GetFromJsonAsync<JsonElement>("/api/auth/saved-places");Assert.True(shared.GetProperty("connected").GetBoolean());var item=Assert.Single(shared.GetProperty("items").EnumerateArray());Assert.Equal("Museum",item.GetProperty("name").GetString());Assert.False(item.GetProperty("mine").GetBoolean());Assert.Equal("Photographer",item.GetProperty("identity").GetProperty("credit").GetString());var revision=item.GetProperty("revision").GetString();
        Assert.Empty((await outsider.GetFromJsonAsync<JsonElement>("/api/auth/saved-places")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(outsider,$"saved-places/{id}/remove",new{revision})).StatusCode);
        (await Post(second,$"saved-places/{id}",new{name="Natural history museum",location="New York, US",link="https://example.test/museum",note="Let's go",revision})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"saved-places/{id}/remove",new{revision})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,$"saved-places/{id}",new{name="Old edit",location="New York",link=(string?)null,note="",revision})).StatusCode);
        var updated=(await first.GetFromJsonAsync<JsonElement>("/api/auth/saved-places")).GetProperty("items")[0];Assert.Equal("Natural history museum",updated.GetProperty("name").GetString());Assert.Equal("Natural History Museum",updated.GetProperty("identity").GetProperty("name").GetString());
        (await Post(second,$"saved-places/{id}/remove",new{revision=updated.GetProperty("revision").GetString()})).EnsureSuccessStatusCode();
        (await Post(first,"saved-places",new{name="Jazz club",location="Paris, France",link=(string?)null,note=""})).EnsureSuccessStatusCode();
        (await Post(second,"together/leave",new {acceptTerms=true})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();Assert.Single(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<SavedPlace>().ToArrayAsync());
    }
}
