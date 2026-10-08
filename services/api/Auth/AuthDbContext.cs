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
        builder.Entity<Overlap.Api.Notifications.PhoneSubscription>(subscription=>{
            subscription.Property(x=>x.AccountId).HasMaxLength(255);
            subscription.Property(x=>x.EndpointHash).HasMaxLength(64);
            subscription.HasIndex(x=>x.EndpointHash).IsUnique();
            subscription.Property(x=>x.ProtectedSubscription).HasColumnType("longtext");
            subscription.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Notifications.ContestReminderDelivery>(delivery=>{
            delivery.HasKey(x=>new{x.ContestId,x.SubscriptionId});
            delivery.HasOne<Overlap.Api.Cooking.CookingContest>().WithMany().HasForeignKey(x=>x.ContestId).OnDelete(DeleteBehavior.Cascade);
            delivery.HasOne<Overlap.Api.Notifications.PhoneSubscription>().WithMany().HasForeignKey(x=>x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });
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
            pick.Property(x=>x.CollectionKey).HasMaxLength(32);
            pick.HasIndex(x => new { x.ProposedBy, x.TitleId, x.CollectionKey }).IsUnique();
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
        builder.Entity<Overlap.Api.Cooking.SavedRecipe>(entry => {
            entry.Property(x => x.PairId).HasMaxLength(32);
            entry.Property(x => x.AddedBy).HasMaxLength(255);
            entry.Property(x => x.Content).HasColumnType("longtext");
            entry.Property(x => x.SharedBy).HasMaxLength(255);
            entry.Property(x => x.ShareRevision).HasMaxLength(32);
            entry.HasIndex(x => new { x.PairId, x.SourceId }).IsUnique();
            entry.HasOne<Account>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Cooking.RecipeExperience>(experience => {
            experience.HasKey(x => new {x.RecipeId,x.AccountId});
            experience.Property(x => x.AccountId).HasMaxLength(255);
            experience.HasOne<Overlap.Api.Cooking.SavedRecipe>().WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
            experience.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Cooking.CookingContest>(contest => {
            contest.Property(x=>x.RedrawRequestedBy).HasMaxLength(255);
            contest.Property(x=>x.RecipeContent).HasColumnType("longtext");
            contest.Property(x=>x.SenderPreferences).HasMaxLength(2000);
            contest.Property(x=>x.RecipientPreferences).HasMaxLength(2000);
            contest.Property(x=>x.PairId).HasMaxLength(32);
            contest.Property(x=>x.ProposedBy).HasMaxLength(255);
            contest.Property(x=>x.TimeZone).HasMaxLength(100);
            contest.Property(x=>x.Status).HasMaxLength(16);
            contest.Property(x=>x.Revision).HasMaxLength(32);
            contest.Property(x=>x.SenderDismissedRevision).HasMaxLength(32);
            contest.Property(x=>x.RecipientDismissedRevision).HasMaxLength(32);
            contest.HasIndex(x=>new{x.PairId,x.StartUtc});
            contest.HasOne<Account>().WithMany().HasForeignKey(x=>x.ProposedBy).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Cooking.ContestRecipeDraw>(draw=>{
            draw.HasKey(x=>new{x.PairId,x.SourceId});
            draw.Property(x=>x.PairId).HasMaxLength(32);
            draw.Property(x=>x.Content).HasColumnType("longtext");
            draw.HasOne<Overlap.Api.Cooking.CookingContest>().WithMany().HasForeignKey(x=>x.ContestId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Cooking.CookingMemory>(memory=>{
            memory.HasKey(x=>x.ContestId);
            memory.Property(x=>x.RecipeContent).HasColumnType("longtext");
            memory.HasOne<Overlap.Api.Cooking.CookingContest>().WithOne().HasForeignKey<Overlap.Api.Cooking.CookingMemory>(x=>x.ContestId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Cooking.CookingDishPhoto>(photo=>{
            photo.HasKey(x=>new{x.ContestId,x.AccountId});
            photo.Property(x=>x.AccountId).HasMaxLength(255);
            photo.Property(x=>x.Revision).HasMaxLength(32);
            photo.Property(x=>x.Png).HasColumnType("mediumblob");
            photo.HasOne<Overlap.Api.Cooking.CookingMemory>().WithMany().HasForeignKey(x=>x.ContestId).OnDelete(DeleteBehavior.Cascade);
            photo.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Drawing.DrawingRound>(round => {
            round.HasKey(x => x.Id);
            round.Property(x => x.SessionId).HasMaxLength(32);
            round.Property(x => x.CategoriesJson).HasMaxLength(256);
            round.Property(x => x.DrawingCount).HasDefaultValue(1);
            round.Property(x => x.DrawingNumber).HasDefaultValue(1);
            round.Property(x => x.PairId).HasMaxLength(32);
            round.Property(x => x.CreatedBy).HasMaxLength(255);
            round.Property(x => x.Prompt).HasMaxLength(160);
            round.Property(x => x.Revision).HasMaxLength(32);
            round.HasIndex(x => new { x.PairId, x.CreatedUtc });
            round.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ConnectionHistory>(history=>{
            history.HasKey(x=>x.PairId);
            history.Property(x=>x.PairId).HasMaxLength(32);
        });
        builder.Entity<AlbumAccess>(access=>{
            access.HasKey(x=>new{x.PairId,x.AccountId});
            access.Property(x=>x.PairId).HasMaxLength(32);
            access.Property(x=>x.AccountId).HasMaxLength(255);
            access.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Drawing.DrawingPhoto>(photo => {
            photo.HasKey(x => new { x.RoundId, x.AccountId });
            photo.Property(x => x.AccountId).HasMaxLength(255);
            photo.Property(x => x.Revision).HasMaxLength(32);
            photo.Property(x => x.Png).HasColumnType("mediumblob");
            photo.HasOne<Overlap.Api.Drawing.DrawingRound>().WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Cascade);
            photo.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Cooking.FoodPreference>(preference=>{
            preference.HasKey(x=>x.AccountId);
            preference.Property(x=>x.AccountId).HasMaxLength(255);
            preference.Property(x=>x.SharedPairId).HasMaxLength(32);
            preference.Property(x=>x.Content).HasMaxLength(2000);
            preference.Property(x=>x.Revision).HasMaxLength(32);
            preference.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Quiz.QuizRound>(round=>{
            round.Property(x=>x.Title).HasMaxLength(80);
            round.Property(x=>x.PairId).HasMaxLength(32);round.Property(x=>x.ProposedBy).HasMaxLength(255);
            round.Property(x=>x.QuestionsJson).HasColumnType("longtext");round.Property(x=>x.Status).HasMaxLength(16);round.Property(x=>x.Revision).HasMaxLength(32);
            round.HasIndex(x=>new{x.PairId,x.CreatedUtc});round.HasOne<Account>().WithMany().HasForeignKey(x=>x.ProposedBy).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Quiz.CustomQuiz>(quiz=>{
            quiz.Property(x=>x.PairId).HasMaxLength(32);quiz.Property(x=>x.AuthorId).HasMaxLength(255);quiz.Property(x=>x.Title).HasMaxLength(80);
            quiz.Property(x=>x.QuestionsJson).HasColumnType("longtext");quiz.Property(x=>x.Revision).HasMaxLength(32);
            quiz.HasIndex(x=>new{x.AuthorId,x.PairId});quiz.HasOne<Account>().WithMany().HasForeignKey(x=>x.AuthorId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Quiz.QuizAnswer>(answer=>{
            answer.HasKey(x=>new{x.RoundId,x.QuestionIndex,x.AccountId});answer.Property(x=>x.AccountId).HasMaxLength(255);
            answer.HasOne<Overlap.Api.Quiz.QuizRound>().WithMany().HasForeignKey(x=>x.RoundId).OnDelete(DeleteBehavior.Cascade);
            answer.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Outings.SavedPlace>(place => {
            place.Property(x=>x.PairId).HasMaxLength(32); place.Property(x=>x.AuthorId).HasMaxLength(255);
            place.Property(x=>x.Name).HasMaxLength(160); place.Property(x=>x.Location).HasMaxLength(200);
            place.Property(x=>x.IdentityJson).HasColumnType("longtext");
            place.Property(x=>x.Link).HasMaxLength(2000); place.Property(x=>x.Note).HasMaxLength(1000); place.Property(x=>x.Revision).HasMaxLength(32);
            place.HasIndex(x=>new{x.PairId,x.CreatedUtc}); place.HasOne<Account>().WithMany().HasForeignKey(x=>x.AuthorId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Account>().Property(x => x.DisplayName).HasMaxLength(50);
        builder.Entity<Overlap.Api.Schedule.CalendarEntry>(entry=>{
            entry.Property(x=>x.PairId).HasMaxLength(32);entry.Property(x=>x.AuthorId).HasMaxLength(255);entry.Property(x=>x.Title).HasMaxLength(160);entry.Property(x=>x.Note).HasMaxLength(1000);entry.Property(x=>x.TimeZone).HasMaxLength(100);entry.Property(x=>x.Revision).HasMaxLength(32);
            entry.HasIndex(x=>new{x.PairId,x.LocalStart});entry.HasOne<Account>().WithMany().HasForeignKey(x=>x.AuthorId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Schedule.CalendarPreference>(preference=>{
            preference.HasKey(x=>x.AccountId);preference.Property(x=>x.AccountId).HasMaxLength(255);preference.Property(x=>x.TimeZone).HasMaxLength(100);preference.Property(x=>x.NotificationTimeZone).HasMaxLength(100);preference.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Overlap.Api.Schedule.CalendarDailyDelivery>(delivery=>{
            delivery.HasKey(x=>new{x.SubscriptionId,x.LocalDate});delivery.Property(x=>x.LocalDate).HasMaxLength(10);delivery.HasOne<Overlap.Api.Notifications.PhoneSubscription>().WithMany().HasForeignKey(x=>x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });
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
