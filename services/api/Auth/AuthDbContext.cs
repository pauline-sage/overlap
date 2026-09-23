using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Overlap.Api.Auth;

public sealed class Account : IdentityUser
{
    public string? DisplayName { get; set; }
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
    : IdentityDbContext<Account>(options)
{
    public DbSet<PendingEmail> PendingEmails => Set<PendingEmail>();
    public DbSet<PairMember> PairMembers => Set<PairMember>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
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
