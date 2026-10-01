using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Overlap.Api.Auth;

public sealed class Account : IdentityUser
{
    public string? DisplayName { get; set; }
    public bool ShareDisplayName { get; set; }
    public bool SharePhoto { get; set; }
    public bool SharePresence { get; set; }
    public byte[]? Photo { get; set; }
    public string? InvitationHash { get; set; }
    public DateTime? InvitationExpiresUtc { get; set; }
    public DateTime? LastLinkEmailUtc { get; set; }
}

public sealed class PendingEmail
{
    public long Id { get; set; }
    public required string ProtectedContent { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public int Attempts { get; set; }
}

public sealed class PairMember
{
    public required string AccountId { get; set; }
    public required string PairId { get; set; }
}

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<Account>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<PendingEmail> PendingEmails => Set<PendingEmail>();
    public DbSet<PairMember> PairMembers => Set<PairMember>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Overlap.Api.Outings.OutingUpdate>(update => {
            update.HasKey(x => new { x.PairId, x.SenderId });
            update.Property(x => x.PairId).HasMaxLength(32);
            update.Property(x => x.SenderId).HasMaxLength(255);
            update.Property(x => x.SenderDismissedRevision).HasMaxLength(32);
            update.Property(x => x.RecipientDismissedRevision).HasMaxLength(32);
            update.Property(x => x.Revision).HasMaxLength(32);
            update.Property(x => x.CancelledBy).HasMaxLength(255);
            update.Property(x => x.CancellationReason).HasMaxLength(500);
            update.Property(x => x.ReplyKind).HasMaxLength(16);
            update.Property(x => x.ReplyRevision).HasMaxLength(32);
            update.Property(x => x.ReplyVenueJson).HasMaxLength(4000);
            update.Property(x => x.VenueJson).HasMaxLength(4000);
            update.HasOne<Account>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<SharingGrant>(grant => {
            grant.HasKey(x => new { x.PairId, x.SenderId, x.Category });
            grant.Property(x => x.PairId).HasMaxLength(32);
            grant.Property(x => x.SenderId).HasMaxLength(255);
            grant.Property(x => x.Category).HasMaxLength(16);
            grant.Property(x => x.Status).HasMaxLength(16);
            grant.HasOne<Account>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Watch.SharedPick>(pick => {
            pick.Property(x => x.PairId).HasMaxLength(32);
            pick.Property(x => x.ProposedBy).HasMaxLength(255);
            pick.Property(x => x.TitleId).HasMaxLength(80);
            pick.Property(x => x.Revision).HasMaxLength(32);
            pick.Property(x => x.Status).HasMaxLength(16);
            pick.Property(x => x.Content).HasMaxLength(16000).HasColumnType("longtext");
            pick.HasIndex(x => new { x.ProposedBy, x.TitleId }).IsUnique();
            pick.HasOne<Account>().WithMany().HasForeignKey(x => x.ProposedBy).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Watch.WatchedTitle>(watched => {
            watched.Property(x => x.PairId).HasMaxLength(32);
            watched.Property(x => x.AddedBy).HasMaxLength(255);
            watched.Property(x => x.TitleId).HasMaxLength(80);
            watched.Property(x => x.Content).HasMaxLength(16000).HasColumnType("longtext");
            watched.HasIndex(x => new { x.PairId, x.TitleId }).IsUnique();
            watched.HasOne<Account>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Books.ReadingEntry>(entry => {
            entry.Property(x => x.PairId).HasMaxLength(32);
            entry.Property(x => x.AddedBy).HasMaxLength(255);
            entry.Property(x => x.BookId).HasMaxLength(40);
            entry.Property(x => x.Content).HasColumnType("longtext");
            entry.Property(x => x.Status).HasMaxLength(16);
            entry.Property(x => x.Scope).HasMaxLength(255);
            entry.HasIndex(x => new { x.PairId, x.BookId, x.Scope }).IsUnique();
            entry.HasOne<Account>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Books.BookRating>(rating => {
            rating.HasKey(x => new { x.EntryId, x.AccountId });
            rating.Property(x => x.AccountId).HasMaxLength(255);
            rating.HasOne<Overlap.Api.Books.ReadingEntry>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Cascade);
            rating.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Account>().Property(x => x.DisplayName).HasMaxLength(50);
        builder.Entity<Account>().Property(x => x.InvitationHash).HasMaxLength(64);
        builder.Entity<Account>().HasIndex(x => x.InvitationHash).IsUnique();
        builder.Entity<PairMember>().HasKey(x => x.AccountId);
        builder.Entity<PairMember>().Property(x => x.PairId).HasMaxLength(32);
        builder.Entity<PairMember>().HasIndex(x => x.PairId);
        builder.Entity<PairMember>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Account>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<PendingEmail>().HasIndex(x => x.NextAttemptUtc);
    }
}
