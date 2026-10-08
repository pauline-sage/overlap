using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Books;
using Overlap.Api.Cooking;
using Overlap.Api.Drawing;
using Overlap.Api.Outings;
using Overlap.Api.Quiz;
using Overlap.Api.Schedule;
using Overlap.Api.Watch;
using Xunit;

namespace Overlap.Api.Tests;

public sealed partial class TogetherTests
{
    private sealed class RetentionClock(DateTimeOffset now):TimeProvider
    {
        public DateTimeOffset Now=now;
        public override DateTimeOffset GetUtcNow()=>Now;
    }

    [Fact]
    public async Task RetentionPurgesExpiredContentAndCascadesWithoutTouchingCreatorActiveOrPersonalData()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();using var third=factory.Client();
        var creator=await SignIn(factory,first,"retention-creator");var owner=await SignIn(factory,second,"retention-owner");var partner=await SignIn(factory,third,"retention-partner");
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();var config=factory.Services.GetRequiredService<IConfiguration>();
        var clock=new RetentionClock(new DateTimeOffset(2030,1,1,12,0,0,TimeSpan.Zero));var now=clock.Now.UtcDateTime;
        var expired=Guid.NewGuid().ToString("N");var kept=Guid.NewGuid().ToString("N");var future=Guid.NewGuid().ToString("N");var active=Guid.NewGuid().ToString("N");var legacy=Guid.NewGuid().ToString("N");
        db.AddRange(new ConnectionHistory{PairId=expired,Closed=true,ExportExpiresUtc=now},new ConnectionHistory{PairId=kept,Closed=true,ExportExpiresUtc=now.AddDays(-1)},new ConnectionHistory{PairId=future,Closed=true,ExportExpiresUtc=now.AddSeconds(1)},new ConnectionHistory{PairId=active,Closed=true,ExportExpiresUtc=now.AddDays(-1)},new ConnectionHistory{PairId=legacy,Closed=true});
        db.AddRange(new AlbumAccess{PairId=expired,AccountId=owner},new AlbumAccess{PairId=expired,AccountId=partner},new AlbumAccess{PairId=kept,AccountId=creator},new AlbumAccess{PairId=kept,AccountId=partner},new PairMember{PairId=active,AccountId=partner});
        var drawing=new DrawingRound{PairId=expired,CreatedBy=owner,Prompt="A waffle",Seconds=60};
        var keptDrawing=new DrawingRound{PairId=kept,CreatedBy=creator,Prompt="A cactus",Seconds=60};
        var book=new ReadingEntry{PairId=expired,AddedBy=owner,BookId="book",Content="{}"};
        var recipe=new SavedRecipe{PairId=expired,AddedBy=owner,SourceId=1,Content="{}"};
        var contest=new CookingContest{PairId=expired,ProposedBy=owner,TimeZone="UTC",Minutes=60};
        var quiz=new QuizRound{PairId=expired,ProposedBy=owner,QuestionsJson="[]"};
        db.AddRange(drawing,keptDrawing,book,recipe,contest,quiz,
            new SharedPick{PairId=expired,CollectionKey=expired,ProposedBy=owner,TitleId="old",Content="{}"},new SharedPick{ProposedBy=owner,TitleId="private",Content="{}"},
            new OutingUpdate{PairId=expired,SenderId=owner},new WatchedTitle{PairId=expired,AddedBy=owner,TitleId="watched",Content="{}"},
            new CustomQuiz{PairId=expired,AuthorId=owner,Title="Quiz",QuestionsJson="[]"},new SavedPlace{PairId=expired,AuthorId=owner,Name="Place",Location="City"},
            new CalendarEntry{PairId=expired,AuthorId=owner,Title="Date",TimeZone="UTC"},new SharingGrant{PairId=expired,SenderId=owner,Category="presence"});
        await db.SaveChangesAsync();
        db.AddRange(new DrawingPhoto{RoundId=drawing.Id,AccountId=owner,Png=[1,2,3]},new DrawingPhoto{RoundId=keptDrawing.Id,AccountId=creator,Png=[1,2,3]},
            new BookRating{EntryId=book.Id,AccountId=owner,Stars=5},new RecipeExperience{RecipeId=recipe.Id,AccountId=owner,Stars=5},
            new CookingMemory{ContestId=contest.Id,RecipeContent="{}"},new ContestRecipeDraw{PairId=expired,SourceId=2,ContestId=contest.Id,Content="{}"},
            new QuizAnswer{RoundId=quiz.Id,AccountId=owner,QuestionIndex=0,OwnChoice=1});
        await db.SaveChangesAsync();db.Add(new CookingDishPhoto{ContestId=contest.Id,AccountId=owner,Png=[1,2,3]});await db.SaveChangesAsync();
        var retention=new ConnectionRetention(db,config,clock);
        config["ConnectionHistory:CreatorEmail"]=null;Assert.Equal(0,await retention.PurgeExpired(default));
        config["ConnectionHistory:CreatorEmail"]="retention-creator@example.test";
        Assert.Equal(1,await retention.PurgeExpired(default));
        Assert.False(await db.Set<ConnectionHistory>().AnyAsync(x=>x.PairId==expired));
        Assert.Equal(4,await db.Set<ConnectionHistory>().CountAsync());
        Assert.Equal(kept,Assert.Single(await db.Set<DrawingRound>().AsNoTracking().ToArrayAsync()).PairId);Assert.Single(await db.Set<DrawingPhoto>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await db.Set<BookRating>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<ReadingEntry>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await db.Set<RecipeExperience>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<SavedRecipe>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await db.Set<CookingContest>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<CookingMemory>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<CookingDishPhoto>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<ContestRecipeDraw>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await db.Set<QuizRound>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<QuizAnswer>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<CustomQuiz>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await db.Set<OutingUpdate>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<WatchedTitle>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<SavedPlace>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<CalendarEntry>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<SharingGrant>().AsNoTracking().ToArrayAsync());
        Assert.Equal("private",Assert.Single(await db.Set<SharedPick>().AsNoTracking().ToArrayAsync()).TitleId);Assert.Equal(3,await db.Users.CountAsync());Assert.Single(await db.PairMembers.AsNoTracking().ToArrayAsync());
        clock.Now=clock.Now.AddSeconds(1);Assert.Equal(1,await retention.PurgeExpired(default));Assert.Equal(0,await retention.PurgeExpired(default));
    }

    [Fact]
    public async Task RemovedCreatorArchiveWaitsForTheOtherParticipantsExportDeadline()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        var creator=await SignIn(factory,first,"retention-removal");var partner=await SignIn(factory,second,"retention-other");
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AuthDbContext>();var config=factory.Services.GetRequiredService<IConfiguration>();
        config["ConnectionHistory:CreatorEmail"]="retention-removal@example.test";
        var clock=new RetentionClock(new DateTimeOffset(2030,1,1,12,0,0,TimeSpan.Zero));var pair=Guid.NewGuid().ToString("N");
        db.AddRange(new ConnectionHistory{PairId=pair,Closed=true,ExportExpiresUtc=clock.Now.AddDays(1).UtcDateTime},new AlbumAccess{PairId=pair,AccountId=creator},new AlbumAccess{PairId=pair,AccountId=partner});await db.SaveChangesAsync();
        var retention=new ConnectionRetention(db,config,clock);
        Assert.Equal(0,await retention.PurgeExpired(default));
        (await Post(first,$"connection-history/{pair}/remove",new{confirm=true})).EnsureSuccessStatusCode();
        Assert.Equal(0,await retention.PurgeExpired(default));Assert.Single(await db.Set<AlbumAccess>().AsNoTracking().Where(x=>x.PairId==pair).ToArrayAsync());
        clock.Now=clock.Now.AddDays(1);Assert.Equal(1,await retention.PurgeExpired(default));
        Assert.Empty(await db.Set<AlbumAccess>().AsNoTracking().ToArrayAsync());Assert.Empty(await db.Set<ConnectionHistory>().AsNoTracking().ToArrayAsync());
    }
}
