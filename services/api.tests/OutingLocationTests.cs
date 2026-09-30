using System.Text.Json;
using Overlap.Api.Outings;
using Xunit;
namespace Overlap.Api.Tests;
public sealed class OutingLocationTests
{
    [Fact]
    public void ResponseContainsOnlyPersonalResults()
    {
        var window = OutingTime.Resolve(new(null, "Europe/Paris", "America/New_York", 60), DateTimeOffset.UtcNow);
        var result = new NearbyResult(window.OwnStart, window.OwnEnd, [], 6, DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("Places", json);
        Assert.DoesNotContain("PartnerPlaces", json);
    }
    [Fact]
    public void CountsRepresentDistinctEligibleVenuesNotVenueCombinations()
    {
        Venue Make(string id, string status) => new(id, id, "Area", 1, null, status, "https://example.test", null);
        var venues = new[] { Make("a", "listed-open"), Make("a", "listed-open"), Make("b", "unknown"), Make("c", "listed-closed") };
        Assert.Single(OutingMatch.Eligible(venues, false));
        Assert.Equal(2, OutingMatch.Eligible(venues, true).Length);
    }
}
