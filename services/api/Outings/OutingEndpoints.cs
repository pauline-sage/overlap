using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using System.Security.Claims;
using Overlap.Api.Auth;

namespace Overlap.Api.Outings;

public sealed record AreaSearch(string? Text);
public sealed record AreaChoice(string Name, string TimeZone, string Token);
public sealed record DeviceLocation(double Latitude, double Longitude);
public sealed record OutingSearch(string AreaToken, int Radius,
    string Activity, string? LocalStart, int Minutes, bool IncludeUnknownHours = false, int? StartInMinutes = null, string? ProposalRevision = null);

public static class OutingEndpoints
{
    private static AreaChoice Choice(OutingArea area, IDataProtectionProvider protection) =>
        new(area.Name, area.TimeZone, protection.CreateProtector("Overlap.OutingAreas.v1").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(area), TimeSpan.FromHours(2)));
    public static void MapOutings(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/outings").RequireAuthorization().RequireRateLimiting("watch");
        group.MapPost("/areas", async (AreaSearch request, GeoapifyPlaces places, IDataProtectionProvider protection, CancellationToken ct) =>
        {
            if (request.Text?.Trim() is not { Length: >= 2 and <= 200 } text)
                return Results.Problem(statusCode: 400, title: "Enter a city, neighbourhood or address.");
            if (!places.Configured) return Unavailable();
            try { return Results.Ok((await places.Areas(text, ct)).Where(OutingMatch.Valid).Select(a => Choice(a, protection))); }
            catch (Exception e) when (ProviderFailure(e, ct)) { return Unavailable(); }
        });
        group.MapPost("/locate", async (DeviceLocation request, GeoapifyPlaces places, IDataProtectionProvider protection, CancellationToken ct) =>
        {
            if (!double.IsFinite(request.Latitude) || !double.IsFinite(request.Longitude) ||
                Math.Abs(request.Latitude) > 90 || Math.Abs(request.Longitude) > 180) return Results.BadRequest();
            if (!places.Configured) return Unavailable();
            try
            {
                var area = await places.Locate(request.Latitude, request.Longitude, ct);
                return OutingMatch.Valid(area) ? Results.Ok(Choice(area!, protection))
                    : Results.Problem(statusCode:400, title:"Unable to locate this area. Try an address instead.");
            }
            catch (Exception e) when (ProviderFailure(e, ct)) { return Unavailable(); }
        });
        group.MapPost("/search", async (OutingSearch request, ClaimsPrincipal user, AuthDbContext db, GeoapifyPlaces places, IDataProtectionProvider protection, TimeProvider clock, CancellationToken ct) =>
        {
            if (request.Radius is < 1 or > 10 ||
                request.Activity is null || !OutingMatch.Categories.TryGetValue(request.Activity, out var category))
                return Results.Problem(statusCode:400, title:"Choose an activity and a radius between 1 and 10 km.");
            OutingArea own;
            try
            {
                var protector = protection.CreateProtector("Overlap.OutingAreas.v1").ToTimeLimitedDataProtector();
                own = Decode(request.AreaToken, protector);
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException or ArgumentException)
            { return Results.Problem(statusCode:400, title:"Choose your location again; your selection may have expired."); }
            if (request.StartInMinutes is not (null or 60) || request.StartInMinutes != null && request.LocalStart != null)
                return Results.BadRequest();
            SharedVenue? proposalVenue = null;
            if (request.ProposalRevision is not null)
            {
                var proposal = await OutingReplyEndpoints.FindProposal(db,user,request.ProposalRevision);
                proposalVenue = OutingReplyEndpoints.Details(proposal,clock.GetUtcNow());
                if (proposalVenue is null) return Results.Problem(statusCode:409,title:"This date changed or is no longer available. Refresh Dates.");
                category = OutingMatch.Categories[proposalVenue.Activity!];
            }
            OutingWindow window;
            try {
                window = proposalVenue is null
                    ? OutingTime.Resolve(new(request.LocalStart, own.TimeZone, own.TimeZone, request.Minutes), clock.GetUtcNow().AddMinutes(request.StartInMinutes ?? 0))
                    : OutingTime.Resolve(new(null, own.TimeZone, own.TimeZone, request.Minutes), proposalVenue.StartUtc!.Value);
            }
            catch (ArgumentException e) { return Results.Problem(statusCode:400, title:e.Message); }
            if (!places.Configured) return Unavailable();
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(40));
                var venues = await places.Nearby(own, request.Radius, category, window, deadline.Token);
                var shareProtector = protection.CreateProtector("Overlap.OutingVenue.v1").ToTimeLimitedDataProtector();
                var matches = OutingMatch.Eligible(venues, request.IncludeUnknownHours).Select(venue =>
                    new VenueResult(venue, shareProtector.Protect(JsonSerializer.Serialize(
                        new SharedVenue(venue.Name, venue.Address, venue.MapUrl, venue.Website, proposalVenue?.Activity ?? request.Activity, window.StartUtc, own.TimeZone)), TimeSpan.FromHours(2)))).ToArray();
                return Results.Ok(new NearbyResult(window.OwnStart, window.OwnEnd, matches, venues.Length, clock.GetUtcNow()));
            }
            catch (Exception e) when (ProviderFailure(e, ct)) { return Unavailable(); }
        });
    }
    private static OutingArea Decode(string? token, ITimeLimitedDataProtector protector)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException();
        var area = JsonSerializer.Deserialize<OutingArea>(protector.Unprotect(token));
        return OutingMatch.Valid(area) ? area! : throw new ArgumentException();
    }
    private static bool ProviderFailure(Exception e, CancellationToken ct) =>
        e is HttpRequestException or JsonException || (e is OperationCanceledException && !ct.IsCancellationRequested);
    private static IResult Unavailable() => Results.Problem(statusCode:503, title:"Place search is unavailable right now. Please try again later.");
}
