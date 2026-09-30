using Overlap.Api.Outings;
using Xunit;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Overlap.Api.Tests;

public sealed class OutingTests
{
    [Fact]
    public void SameInstantUsesDifferentDatesAndOffsets()
    {
        var window = OutingTime.Resolve(new("2026-10-01T00:30", "Europe/Paris", "America/New_York", 60), DateTimeOffset.Parse("2026-09-28T12:00Z"));
        Assert.Equal(DateTimeOffset.Parse("2026-09-30T22:30Z"), window.StartUtc);
        Assert.Equal("2026-09-30 18:30 -04:00", window.PartnerStart);
        Assert.Equal(TimeSpan.FromHours(1), window.EndUtc - window.StartUtc);
    }

    [Theory]
    [InlineData("2026-03-29T02:30", "2026-03-28T00:00Z")]
    [InlineData("2026-10-25T02:30", "2026-10-24T00:00Z")]
    public void RejectsNonexistentAndAmbiguousLocalStarts(string local, string now) =>
        Assert.Throws<ArgumentException>(() => OutingTime.Resolve(new(local, "Europe/Paris", "America/New_York", 60), DateTimeOffset.Parse(now)));

    [Fact]
    public void SharedDurationCrossesDstAsElapsedTime()
    {
        var window = OutingTime.Resolve(new("2026-03-29T01:30", "Europe/Paris", "America/New_York", 120), DateTimeOffset.Parse("2026-03-28T00:00Z"));
        Assert.Equal("2026-03-29 04:30 +02:00", window.OwnEnd);
        Assert.Equal(TimeSpan.FromHours(2), window.EndUtc - window.StartUtc);
    }

    [Theory]
    [InlineData("Mo-Fr 09:00-17:00", "2026-09-28T14:00Z", "2026-09-28T15:00Z", "listed-open")]
    [InlineData("Mo-Fr 09:00-17:00", "2026-09-28T14:00Z", "2026-09-28T15:01Z", "listed-closed")]
    [InlineData("Mo 22:00-02:00", "2026-09-28T23:00Z", "2026-09-29T00:00Z", "listed-open")]
    [InlineData("Mo-Fr 09:00-12:00,13:00-17:00", "2026-09-28T09:30Z", "2026-09-28T11:30Z", "listed-closed")]
    [InlineData("Mo-Fr 09:00-17:00; PH off", "2026-09-28T09:00Z", "2026-09-28T10:00Z", "unknown")]
    [InlineData(null, "2026-09-28T09:00Z", "2026-09-28T10:00Z", "unknown")]
    [InlineData("Su 01:00-04:00", "2026-10-24T23:30Z", "2026-10-25T02:30Z", "listed-open")]
    public void OpeningHoursCoverWholeVisit(string? hours, string start, string end, string expected) =>
        Assert.Equal(expected, VenueHours.Assess(hours, "Europe/Paris", DateTimeOffset.Parse(start), DateTimeOffset.Parse(end)));

    private static Venue Place(string id, string state, double distance) => new(id,id,"Area",distance,null,state,"https://www.openstreetmap.org",null);
    [Fact]
    public void PairingExcludesClosedAndRequiresExplicitOptInForUnknown()
    {
        var own = new[] {Place("unknown","unknown",1),Place("open","listed-open",3),Place("closed","listed-closed",0)};
        var partner = new[] {Place("partner","listed-open",2)};
        Assert.Equal("open", Assert.Single(OutingMatch.Pair(own,partner)).Own.Id);
        var withUnknown = OutingMatch.Pair(own,partner,true);
        Assert.Equal(2,withUnknown.Length);
        Assert.Equal("open",withUnknown[0].Own.Id);
        Assert.DoesNotContain(withUnknown,x=>x.Own.Id=="closed");
    }
    [Fact]
    public void DistanceIsInKilometres() => Assert.InRange(OutingMatch.Distance(48.85,2.35,48.86,2.35),1.1,1.12);
    [Fact]
    public async Task SearchRequiresLoginAndRejectsUntrustedLocations()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        async Task<HttpResponseMessage> Post(string path, object body)
        {
            var csrf = await client.GetAsync("/api/auth/csrf");
            var cookie = csrf.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN="));
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/" + path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]));
            return await client.SendAsync(request);
        }
        var query = new OutingSearch("tampered", 3, "Coffee", null, 60);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("outings/search", query)).StatusCode);
        const string email = "coffee@example.test";
        const string password = "coffee-test-password";
        (await Post("register", new { email, password })).EnsureSuccessStatusCode();
        (await Post("confirm-email", new { email, token = await factory.LinkToken("confirm-email") })).EnsureSuccessStatusCode();
        (await Post("login", new { email, password })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("outings/search", query)).StatusCode);
        factory.Services.GetRequiredService<IConfiguration>()["Outings:GeoapifyApiKey"] = "";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post("outings/areas", new { text = "Lyon" })).StatusCode);
    }}
