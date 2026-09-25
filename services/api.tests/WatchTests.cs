using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Overlap.Api.Watch;
using Xunit;
namespace Overlap.Api.Tests;
public class WatchTests
{
    private static WatchSearch Request(int minutes = 90) => new("Movie", "test", "title", ["Comedy"], minutes, "FR", "US", ["Netflix"], ["Prime Video"]);
    [Fact] public void ValidatesFiltersAndUnknownDuration()
    {
        Assert.True(WatchRules.Valid(Request())); Assert.False(WatchRules.Valid(Request() with { OwnPlatforms = [] }));
        Assert.False(WatchRules.Valid(Request() with { OwnCountry = "XX" })); Assert.False(WatchRules.Valid(Request() with { Page = 51 }));
        Assert.False(WatchRules.Fits(null, 90)); Assert.True(WatchRules.Fits(90, 90)); Assert.False(WatchRules.Fits(91, 90));
        Assert.Null(WatchRules.Platform("Amazon Video")); Assert.Null(WatchRules.Platform("Disney+ Amazon Channel"));
    }
    private sealed class Handler(string detail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.DoesNotContain("test-token", request.RequestUri!.ToString());
            var json = request.RequestUri.AbsolutePath.Contains("search/") ? "{\"results\":[{\"id\":1}],\"total_pages\":2}" : detail;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    private const string Detail = """
    {"id":1,"title":"Test","runtime":90,"poster_path":"/poster.jpg","overview":"Summary","genres":[{"id":35,"name":"Comedy"}],"watch/providers":{"results":{"FR":{"flatrate":[{"provider_name":"Netflix"}]},"US":{"flatrate":[{"provider_name":"Amazon Prime Video"}]}}}}
    """;
    private static async Task<WatchResults> Search(string detail, WatchSearch request)
    {
        using var http = new HttpClient(new Handler(detail)); using var cache = new TmdbCache();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Streaming:TmdbReadAccessToken"] = "test-token" }).Build();
        return await new TmdbCatalog(http,config,cache).Search(request,CancellationToken.None);
    }
    [Fact] public async Task MatchesDifferentSubscriptionsAndIncludesRealMetadata()
    {
        var result = await Search(Detail, Request()); var title = Assert.Single(result.Titles);
        Assert.Equal("Summary", title.Description); Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", title.Poster);
        Assert.Equal(["Netflix"],title.OwnServices); Assert.Equal(["Prime Video"],title.PartnerServices); Assert.True(result.HasMore);
    }
    [Fact] public async Task ExcludesRentalOnlyAndLongOrUnknownRuntime()
    {
        Assert.Empty((await Search(Detail.Replace("flatrate", "rent"), Request())).Titles);
        Assert.Empty((await Search(Detail, Request(89))).Titles);
        Assert.Empty((await Search(Detail.Replace("\"runtime\":90", "\"runtime\":null"), Request())).Titles);
        Assert.Empty((await Search(Detail, Request() with { PartnerPlatforms = ["Disney+"] })).Titles);
    }
    [Fact] public async Task ChecksThemesAndOriginsEvenForTitleSearch()
    {
        var themed = Detail.Replace("\"genres\":", "\"origin_country\":[\"TH\"],\"keywords\":{\"keywords\":[{\"id\":289844}]},\"genres\":");
        Assert.Single((await Search(themed, Request() with { Theme = "bl", Origin = "TH" })).Titles);
        Assert.True(WatchRules.MatchesTheme("bl", [353318]));
        Assert.False(WatchRules.Valid(Request() with { Theme = "yaoi" }));
        Assert.Empty((await Search(themed, Request() with { Origin = "JP" })).Titles);
        Assert.Empty((await Search(Detail, Request() with { Theme = "lgbtq" })).Titles);
        Assert.False(WatchRules.Valid(Request() with { Theme = "unsupported" }));
    }
    [Fact] public void YouTubeOnlyIncludesFreeOffersAndDoesNotImplySubtitles()
    {
        using var region = System.Text.Json.JsonDocument.Parse("""
        {"rent":[{"provider_name":"YouTube"}],"buy":[{"provider_name":"YouTube"}],"flatrate":[{"provider_name":"YouTube Premium"},{"provider_name":"Netflix"}],"ads":[{"provider_name":"YouTube Free"}],"free":[{"provider_name":"YouTube"}]}
        """);
        Assert.Equal(["Netflix", "YouTube"], WatchRules.IncludedServices(region.RootElement, ["Netflix", "YouTube"]));
        using var rental = System.Text.Json.JsonDocument.Parse("""{"rent":[{"provider_name":"YouTube"}]}""");
        Assert.Empty(WatchRules.IncludedServices(rental.RootElement, ["YouTube"]));
    }
    [Fact] public async Task RequiresAuthentication()
    {
        await using var factory = new AuthFactory(); using var client = factory.Client();
        var response = await client.PostAsync("/api/auth/watch/search", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
