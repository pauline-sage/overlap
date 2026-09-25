using System.Text.Json;
using Overlap.Api.Watch;
using Xunit;
namespace Overlap.Api.Tests;
public class YouTubeTests
{
    private static YouTubeRequest Request() => new("Bad Buddy", "bl", "TH", "Series", 30, "FR", "US", true);
    private const string Video = """
    {"id":"abcdefghijk","snippet":{"title":"Episode 1 [1/4]","channelTitle":"Channel","liveBroadcastContent":"none"},"contentDetails":{"duration":"PT25M","caption":"true"},"status":{"privacyStatus":"public"}}
    """;
    [Fact] public void AllowsDiscoveryAndValidatesMoodAndCountries()
    {
        Assert.True(YouTubeSearch.Valid(Request()));
        Assert.True(YouTubeSearch.Valid(Request() with { Query = "", Theme = "" }));
        Assert.False(YouTubeSearch.Valid(Request() with { Genre = "invalid" }));
        Assert.False(YouTubeSearch.Valid(Request() with { OwnCountry = "XX" }));
    }
    [Fact] public void RetainsActualVideoDurationAndDoesNotInventSubtitleLanguages()
    {
        using var json = JsonDocument.Parse(Video);
        var result = YouTubeSearch.Read(json.RootElement, Request());
        Assert.NotNull(result); Assert.Equal(25, result.Minutes); Assert.True(result.Captions);
        Assert.Contains("[1/4]", result.Title); Assert.EndsWith("abcdefghijk", result.Url);
        Assert.Null(YouTubeSearch.Read(json.RootElement, Request() with { Minutes = 24 }));
    }
    [Fact] public void ExcludesTrailersAndMissingRequestedCaptionTracks()
    {
        using var trailer = JsonDocument.Parse(Video.Replace("Episode 1 [1/4]", "Official trailer"));
        Assert.Null(YouTubeSearch.Read(trailer.RootElement, Request()));
        using var noCaption = JsonDocument.Parse(Video.Replace("\"caption\":\"true\"", "\"caption\":\"false\""));
        Assert.Null(YouTubeSearch.Read(noCaption.RootElement, Request()));
        Assert.NotNull(YouTubeSearch.Read(noCaption.RootElement, Request() with { CaptionsOnly = false }));
    }
    [Fact] public void ExcludesShortClipsAndFanEditsWithoutEpisodeMarkers()
    {
        using var clip = JsonDocument.Parse(Video.Replace("PT25M", "PT1M"));
        Assert.Null(YouTubeSearch.Read(clip.RootElement, Request()));
        using var edit = JsonDocument.Parse(Video.Replace("Episode 1 [1/4]", "Pran and Pat Story"));
        Assert.Null(YouTubeSearch.Read(edit.RootElement, Request()));
    }
    [Fact] public void ChecksBothAllowedAndBlockedCountries()
    {
        using var blocked = JsonDocument.Parse("""{"regionRestriction":{"blocked":["US"]}}""");
        Assert.True(YouTubeSearch.Available(blocked.RootElement, "FR")); Assert.False(YouTubeSearch.Available(blocked.RootElement, "US"));
        using var allowed = JsonDocument.Parse("""{"regionRestriction":{"allowed":["FR"]}}""");
        Assert.False(YouTubeSearch.Available(allowed.RootElement, "US"));
        using var emptyAllowed = JsonDocument.Parse("""{"regionRestriction":{"allowed":[]}}""");
        Assert.False(YouTubeSearch.Available(emptyAllowed.RootElement, "FR"));
    }
}
