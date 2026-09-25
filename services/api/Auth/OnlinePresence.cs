using Microsoft.Extensions.Caching.Memory;
namespace Overlap.Api.Auth;

// Transient recent presence for the current single-instance deployment. No history is retained.
public sealed class OnlinePresence(TimeProvider clock) : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    public void Seen(string id) => cache.Set(id, clock.GetUtcNow(), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(90) });
    public bool IsOnline(string id) => cache.TryGetValue<DateTimeOffset>(id, out var seen) && clock.GetUtcNow() - seen < TimeSpan.FromSeconds(90);
    public void Leave(string id) => cache.Remove(id);
    public void Dispose() => cache.Dispose();
}
