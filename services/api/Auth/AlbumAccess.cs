namespace Overlap.Api.Auth;

/** Original participants authorize time-limited exports and the creator-only archive. */
public sealed class AlbumAccess
{
    public required string PairId { get; set; }
    public required string AccountId { get; set; }
}
