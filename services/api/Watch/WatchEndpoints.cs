using System.Text.Json;
namespace Overlap.Api.Watch;
public static class WatchEndpoints
{
    public static void MapWatch(this WebApplication app)
    {
        app.MapPost("/api/auth/watch/search", async (WatchSearch request, TmdbCatalog catalog, CancellationToken ct) =>
    {
        if (!WatchRules.Valid(request)) return Results.Problem(statusCode: 400, title: "Choose a valid format, country, service and duration.");
        if (!catalog.Configured) return Results.Problem(statusCode: 503, title: "Movie search is not connected yet. Configure the TMDB access token on the server.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        try { return Results.Ok(await catalog.Search(request, deadline.Token)); }
        catch (HttpRequestException) { return Results.Problem(statusCode: 502, title: "The movie catalogue is unavailable. Please try again shortly."); }
        catch (System.Text.Json.JsonException) { return Results.Problem(statusCode: 502, title: "The movie catalogue returned an unexpected response. Please try again."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem(statusCode: 504, title: "The movie search took too long. Please try again."); }
    }).RequireAuthorization().RequireRateLimiting("watch");
        app.MapPost("/api/auth/watch/youtube", async (YouTubeRequest request, YouTubeSearch search, CancellationToken ct) =>
        {
            if (!YouTubeSearch.Valid(request)) return Results.Problem(statusCode: 400, title: "Choose valid search filters.");
            if (!search.Configured) return Results.Problem(statusCode: 503, title: "Direct YouTube search needs a YouTube Data API key on the server.");
            try { return Results.Ok(await search.Search(request, ct)); }
            catch (HttpRequestException) { return Results.Problem(statusCode: 502, title: "YouTube search is unavailable or its quota has been reached. Try again later."); }
            catch (JsonException) { return Results.Problem(statusCode: 502, title: "YouTube returned an unexpected response. Try again later."); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem(statusCode: 504, title: "YouTube search took too long. Try again."); }
        }).RequireAuthorization().RequireRateLimiting("watch");
    }
}
