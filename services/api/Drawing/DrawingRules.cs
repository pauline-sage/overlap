using System.Security.Cryptography;

namespace Overlap.Api.Drawing;

public static class DrawingRules
{
    public static readonly string[] Prompts = [
        "An apple", "A banana", "A strawberry", "A lemon", "A pear", "A pineapple",
        "A flower", "A rose", "A sunflower", "A tulip", "A cactus", "A tree", "A leaf",
        "A cat", "A dog", "A rabbit", "A fox", "A horse", "A lion", "A tiger", "A bear",
        "A panda", "A penguin", "An owl", "A duck", "A butterfly", "A bee", "A turtle",
        "A frog", "A fish", "A dolphin", "An elephant", "A giraffe", "A snail",
        "A coffee cup", "A teapot", "A shoe", "A book", "A bicycle", "A house",
        "A candle", "An umbrella", "A slice of pizza", "An ice cream", "A mushroom",
        "Levi (SNK)", "Mikasa (SNK)", "Eren (SNK)", "Armin (SNK)", "Hange (SNK)",
        "Erwin (SNK)", "Sasha (SNK)", "Jean (SNK)", "Connie (SNK)", "Historia (SNK)",
        "Reiner (SNK)", "Annie (SNK)", "Pieck (SNK)", "The Cart Titan (SNK)",
        "The Colossal Titan (SNK)", "The Armored Titan (SNK)", "The Female Titan (SNK)",
        "The Beast Titan (SNK)", "The Attack Titan (SNK)", "The Scout Regiment emblem (SNK)"
    ];
    public static bool ValidSeconds(int seconds) => seconds is >= 1 and <= 3600;
    public static string Shuffle(string? previous = null)
    {
        var options = Prompts.Where(x => x != previous).ToArray();
        return options[RandomNumberGenerator.GetInt32(options.Length)];
    }
    public static string Status(DrawingRound round, DateTime now) => round.Cancelled ? "cancelled"
        : round.EndUtc is null ? "waiting" : now < round.EndUtc ? "drawing" : "finished";
}

public sealed class DrawingRound
{
    public long Id { get; set; }
    public required string PairId { get; set; }
    public required string CreatedBy { get; set; }
    public required string Prompt { get; set; }
    public int Seconds { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
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
