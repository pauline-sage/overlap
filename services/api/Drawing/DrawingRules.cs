
namespace Overlap.Api.Drawing;

public static class DrawingRules
{
    public static readonly string[] Prompts = DrawingCatalog.Categories.SelectMany(x=>x.Prompts).ToArray();
    public static bool ValidSeconds(int seconds) => seconds is >= 1 and <= 3600;
    public static string Shuffle(string? previous = null) => DrawingCatalog.Shuffle(DrawingCatalog.DefaultCategories,previous);
    public static string Status(DrawingRound round, DateTime now) => round.Cancelled ? "cancelled"
        : round.PausedSeconds is not null ? "paused" : round.EndUtc is null ? "waiting" : now < round.EndUtc ? "drawing" : "finished";
}

public sealed class DrawingRound
{
    public long Id { get; set; }
    public required string PairId { get; set; }
    public required string CreatedBy { get; set; }
    public required string Prompt { get; set; }
    public int Seconds { get; set; }
    public string? CategoriesJson { get; set; }
    public string? SessionId { get; set; }
    public int DrawingCount { get; set; } = 1;
    public int DrawingNumber { get; set; } = 1;
    public bool NextCreated { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public int? PausedSeconds { get; set; }
    public bool Cancelled { get; set; }
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class DrawingPhoto
{
    public long RoundId { get; set; }
    public required string AccountId { get; set; }
    public required byte[] Png { get; set; }
    public DateTime UploadedUtc { get; set; }
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
    public int? OwnerRating { get; set; }
    public int? PartnerRating { get; set; }
}
