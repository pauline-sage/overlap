using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Cooking;
using Xunit;
namespace Overlap.Api.Tests;
public class CookingCatalogTests
{
    private sealed class DietarySearchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var query=Uri.UnescapeDataString(request.RequestUri!.Query);
            string body;
            if(query.Contains("list=search")) body="""
                {"query":{"search":[{"pageid":1,"title":"Cookbook:Feijoada"},{"pageid":2,"title":"Cookbook:Vegetarian Feijoada"}]}}
                """;
            else
            {
                var vegetarian=query.Contains("pageid=2");
                body=JsonSerializer.Serialize(new{parse=new{title=vegetarian ? "Cookbook:Vegetarian Feijoada" : "Cookbook:Feijoada",revid=1,text="<h2>Ingredients</h2><ul><li>"+(vegetarian ? "Beans, carrots" : "Beans, pork")+"</li></ul><h2>Method</h2><ol><li>Simmer</li></ol>"}});
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
        }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchOffersRealAlternativeWhenEitherPersonExcludesMeat(bool ownerExcludes)
    {
        using var http=new HttpClient(new DietarySearchHandler());
        var restricted=new ContestPreferences(true,[]);var unrestricted=new ContestPreferences(false,[]);
        var results=await new RecipeCatalog(http).Search("feijoada",ownerExcludes ? [restricted,unrestricted] : [unrestricted,restricted],CancellationToken.None);
        Assert.Equal(2,Assert.Single(results).Id);
        var unavailable=await new RecipeCatalog(http).Search("feijoada",[new ContestPreferences(false,["beans"])],CancellationToken.None);
        Assert.Empty(unavailable);
    }

    [Fact]
    public void ExtractsQuantitiesAndInstructionsWithoutRenderingHtml()
    {
        var recipe=RecipeCatalog.Extract(12,34,"Cookbook:Flan","<h2>Ingredients</h2><ul><li>2 &amp; � cups <a href='/'>milk</a></li></ul><h2>Instructions</h2><ol><li>Mix.</li><li>Bake.</li></ol><h2>Notes</h2><p>Not a step</p>")!;
        Assert.Equal("2 & � cups  milk",Assert.Single(recipe.Ingredients));
        Assert.Equal(new[]{"Mix.","Bake."},recipe.Instructions);
        Assert.Equal("https://en.wikibooks.org/w/index.php?oldid=34",recipe.SourceUrl);
        Assert.Null(recipe.Photo);
    }
    [Fact]
    public void PreservesIngredientAndInstructionSubsections()
    {
        var recipe=RecipeCatalog.Extract(1,2,"Cookbook:Crepes","<h2>Ingredients</h2><h3>Batter</h3><ul><li>Milk</li></ul><h3>Filling</h3><ul><li>Chocolate</li></ul><h2>Procedure</h2><h3>Batter</h3><ol><li>Mix</li></ol><h3>Assembly</h3><ol><li>Fill</li></ol><h2>Notes</h2><ul><li>Do not import</li></ul>")!;
        Assert.Equal(new[]{"Batter: Milk","Filling: Chocolate"},recipe.Ingredients);
        Assert.Equal(new[]{"Batter: Mix","Assembly: Fill"},recipe.Instructions);
    }
    private sealed class RecipeHandler : HttpMessageHandler
    {
        public int Calls {get; private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;
            Assert.Equal("/w/api.php",request.RequestUri!.AbsolutePath);
            Assert.Contains("Overlap",request.Headers.UserAgent.ToString());
            var body=request.RequestUri.Host=="en.wikibooks.org" ? """
            {"parse":{"title":"Cookbook:Flan","revid":34,"text":"<h2>Ingredients</h2><ul><li>Milk</li></ul><h2>Method</h2><ol><li>Mix</li></ol>","images":["Flan.jpg"]}}
            """ : """
            {"query":{"pages":[{"imageinfo":[{"thumburl":"https://upload.wikimedia.org/flan.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Flan.jpg","extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"},"Artist":{"value":"<a>An author</a>"},"LicenseUrl":{"value":"https://creativecommons.org/licenses/by-sa/4.0/"}}}]}]}}
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
        }
    }
    [Fact]
    public async Task RecipePhotoRemainsExternalAndRetainsAttributionWithoutFetchingImageBytes()
    {
        using var handler=new RecipeHandler(); using var http=new HttpClient(handler);
        var recipe=await new RecipeCatalog(http).Details(12,CancellationToken.None);
        Assert.NotNull(recipe!.Photo);Assert.Equal("An author",recipe.Photo.Author);Assert.Equal("CC BY-SA 4.0",recipe.Photo.License);
        Assert.Equal(2,handler.Calls);
    }
    [Theory]
    [InlineData("Cookbook:Cr�pes","<h2>Technique</h2><p>A guide.</p>")]
    [InlineData("Other:Flan","<h2>Ingredients</h2><ul><li>Milk</li></ul><h2>Method</h2><p>Mix</p>")]
    [InlineData("Cookbook:Empty","<h2>Ingredients</h2><h2>Method</h2>")]
    public void RejectsNonRecipesAndIncompletePages(string title,string html) => Assert.Null(RecipeCatalog.Extract(1,2,title,html));
}
public sealed partial class TogetherTests
{
    [Fact]
    public async Task CookingSnapshotsAreAccountBoundSharedAndRevokedOnDisconnect()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true); using var first=factory.Client(); using var second=factory.Client(); using var stranger=factory.Client();
        var owner=await SignIn(factory,first,"cook-first"); await SignIn(factory,second,"cook-second"); await SignIn(factory,stranger,"cook-stranger");
        (await Post(second,"together/accept",new { token=await Invite(first) })).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var pair=(await db.PairMembers.SingleAsync(x=>x.AccountId==owner)).PairId;
        var recipe=new Recipe(12,34,"Flan",["Milk"],["Mix"],"https://en.wikibooks.org/w/index.php?oldid=34",null);
        var protector=scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Cooking.RecipeSnapshot.v1").ToTimeLimitedDataProtector();
        var token=protector.Protect(JsonSerializer.Serialize(new RecipeSnapshot(owner,pair,recipe)),TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(second,"cooking/collection",new {token})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"cooking/collection",new {token="tampered"})).StatusCode);
        var expired=protector.Protect(JsonSerializer.Serialize(new RecipeSnapshot(owner,pair,recipe)),DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,"cooking/collection",new {token=expired})).StatusCode);
        (await Post(first,"cooking/collection",new {token})).EnsureSuccessStatusCode();
        (await Post(first,"cooking/collection",new {token})).EnsureSuccessStatusCode();
        var entry=Assert.Single((await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/collection")).GetProperty("entries").EnumerateArray());
        Assert.Equal("Milk",entry.GetProperty("recipe").GetProperty("ingredients")[0].GetString());
        var id=entry.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,$"cooking/collection/{id}/experience",new {made=true,stars=6})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(stranger,$"cooking/collection/{id}/experience",new {made=true,stars=5})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(stranger,$"cooking/collection/{id}/share",new {})).StatusCode);
        (await Post(first,$"cooking/collection/{id}/experience",new {made=true,stars=5})).EnsureSuccessStatusCode();
        (await Post(second,$"cooking/collection/{id}/experience",new {made=true,stars=3})).EnsureSuccessStatusCode();
        var privateList=await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/collection");
        Assert.False(privateList.GetProperty("partnerCookingShared").GetBoolean());
        Assert.False(privateList.GetProperty("entries")[0].GetProperty("partnerMade").GetBoolean());
        Assert.Equal(JsonValueKind.Null,privateList.GetProperty("entries")[0].GetProperty("partnerRating").ValueKind);
        Assert.Equal(JsonValueKind.Null,privateList.GetProperty("entries")[0].GetProperty("partnerMadeUtc").ValueKind);
        Assert.Equal(DateTimeKind.Utc,privateList.GetProperty("entries")[0].GetProperty("ownMadeUtc").GetDateTime().Kind);
        await EnableSharing(first,"cooking");
        var rated=(await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/collection")).GetProperty("entries")[0];
        Assert.True(rated.GetProperty("ownMade").GetBoolean());Assert.Equal(3,rated.GetProperty("ownRating").GetInt32());Assert.Equal(5,rated.GetProperty("partnerRating").GetInt32());
        Assert.Equal(DateTimeKind.Utc,rated.GetProperty("partnerMadeUtc").GetDateTime().Kind);
        (await Post(first,"privacy/cancel",new{category="cooking"})).EnsureSuccessStatusCode();
        privateList=await second.GetFromJsonAsync<JsonElement>("/api/auth/cooking/collection");
        Assert.False(privateList.GetProperty("partnerCookingShared").GetBoolean());Assert.Equal(JsonValueKind.Null,privateList.GetProperty("entries")[0].GetProperty("partnerRating").ValueKind);
        Assert.Equal(3,privateList.GetProperty("entries")[0].GetProperty("ownRating").GetInt32());
        using(var retained=factory.Services.CreateScope()) Assert.Equal(2,await retained.ServiceProvider.GetRequiredService<AuthDbContext>().Set<RecipeExperience>().CountAsync());
        (await Post(first,$"cooking/collection/{id}/experience",new {made=true,stars=(int?)null})).EnsureSuccessStatusCode();
        (await Post(first,$"cooking/collection/{id}/share",new {})).EnsureSuccessStatusCode();
        var news=await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy");Assert.Equal(1,news.GetProperty("unreadRecipes").GetInt32());
        var revision=news.GetProperty("recipeShares")[0].GetProperty("revision").GetString();
        (await Post(first,$"cooking/collection/{id}/share",new {})).EnsureSuccessStatusCode();
        (await Post(second,"privacy/read",new{kind="recipe",revision,id,dismiss=true})).EnsureSuccessStatusCode();
        news=await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy");Assert.Equal(1,news.GetProperty("unreadRecipes").GetInt32());Assert.False(news.GetProperty("recipeShares")[0].GetProperty("dismissed").GetBoolean());
        revision=news.GetProperty("recipeShares")[0].GetProperty("revision").GetString();
        (await Post(second,"privacy/read",new{kind="recipe",revision,id,dismiss=true})).EnsureSuccessStatusCode();
        news=await second.GetFromJsonAsync<JsonElement>("/api/auth/privacy");Assert.Equal(0,news.GetProperty("unreadRecipes").GetInt32());Assert.True(news.GetProperty("recipeShares")[0].GetProperty("dismissed").GetBoolean());
        Assert.Empty((await stranger.GetFromJsonAsync<JsonElement>("/api/auth/cooking/collection")).GetProperty("entries").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(stranger,$"cooking/collection/{id}/remove",new {})).StatusCode);
        (await Post(second,$"cooking/collection/{id}/remove",new {})).EnsureSuccessStatusCode();
        (await Post(first,"cooking/collection",new {token})).EnsureSuccessStatusCode();
        (await Post(second,"together/leave",new {acceptTerms=true})).EnsureSuccessStatusCode();
        Assert.Single(await db.Set<SavedRecipe>().AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<RecipeExperience>().AsNoTracking().ToListAsync());
    }
}
