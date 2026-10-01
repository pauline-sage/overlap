using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Overlap.Api.Auth;
using Overlap.Api.Books;
using Xunit;

namespace Overlap.Api.Tests;
public class BookCatalogTests
{
    [Fact]
    public void RecommendationsPrioritizeMyLikesAndExcludeDislikedBooksAndDuplicateFilters()
    {
        ReadingEntry Entry(long id, string genre) => new() {Id=id,PairId="pair",AddedBy="owner",BookId=$"/works/OL{id}W",Content=JsonSerializer.Serialize(Book with {Id=$"/works/OL{id}W",Subjects=[genre]}),Status="read"};
        var seeds=BookRecommendations.Seeds([Entry(1,"fantasy"),Entry(2,"horror"),Entry(3,"romance"),Entry(4,"fantasy")],new Dictionary<long,int> {[1]=5,[2]=2,[3]=4});
        Assert.Equal(new[] {"/works/OL1W","/works/OL3W"},seeds.Select(x => x.Book.Id));
        Assert.Equal(5,seeds[0].Stars);
    }
    private sealed class SummaryHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal("/works/OL1W.json",request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(json,Encoding.UTF8,"application/json")});
        }
    }
    [Theory]
    [InlineData("{\"description\":\"Real summary\"}","Real summary")]
    [InlineData("{\"description\":{\"value\":\"Real summary\"}}","Real summary")]
    [InlineData("{}",null)]
    public async Task BookSummaryHandlesBothProviderFormatsAndMissingDescriptions(string json,string? expected)
    {
        using var http=new HttpClient(new SummaryHandler(json)); using var cache=new BookCatalogCache();
        Assert.Equal(expected,await new BookCatalog(http,cache,TimeProvider.System).Summary("/works/OL1W",CancellationToken.None));
        Assert.False(BookCatalog.ValidWorkId("/works/../../example"));
    }
    public static BookTitle Book => new("/works/OL893415W","A shared story",["An author"],["OL1A"],["science fiction"],123,2020);
    [Fact]
    public void ValidatesFiltersAndUsesGenresOrAuthorsFromPreviousReads()
    {
        Assert.True(BookCatalog.Valid(new BookSearch("Dune","science fiction","en")));
        Assert.False(BookCatalog.Valid(new BookSearch("Dune","unknown","en")));
        Assert.False(BookCatalog.Valid(new BookSearch("Dune",Language:"xx")));
        Assert.False(BookCatalog.Valid(new BookSearch("Dune",Page:0)));
        Assert.False(BookCatalog.Valid(Book with {Id="https://example.test"}));
        var (query,why) = BookCatalog.SearchTerms(new BookSearch(null,Language:"fr"),Book);
        Assert.Contains("subject:\"science fiction\"",query);
        Assert.Contains("language:fre",query); Assert.Contains(Book.Title,why);
        Assert.Contains("author_key:\"OL1A\"",BookCatalog.SearchTerms(new BookSearch(null),Book with {Subjects=[]}).Query);
        Assert.Throws<ArgumentException>(() => BookCatalog.SearchTerms(new BookSearch(null),Book with {Subjects=[],AuthorIds=[]}));
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++; Assert.Equal("openlibrary.org",request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization); Assert.Contains("Overlap",request.Headers.UserAgent.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent("""
                {"numFound":21,"docs":[{"key":"/works/OL10W","title":"A real book","author_name":["An author"],"author_key":["OL1A"],"subject":["fantasy"],"cover_i":123,"first_publish_year":2020},{"key":"https://bad.test","title":"Skip"}]}
                """,Encoding.UTF8,"application/json")});
        }
    }
    [Fact]
    public async Task NormalizesRealMetadataAndCachesIdenticalQueries()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler); using var cache = new BookCatalogCache();
        var catalog = new BookCatalog(http,cache,TimeProvider.System);
        var result = await catalog.Search(new BookSearch("test"),null,CancellationToken.None);
        Assert.Equal("A real book",Assert.Single(result.Books).Title); Assert.True(result.HasMore);
        await catalog.Search(new BookSearch("test"),null,CancellationToken.None);
        Assert.Equal(1,handler.Calls);
    }
}
public sealed partial class TogetherTests
{
    [Fact]
    public async Task BookInterestsAreOwnerControlledAndStopBeingVisibleWhenSharingIsOff()
    {
        using var factory=new AuthFactory(unlimitedAuthRequests:true);using var first=factory.Client();using var second=factory.Client();
        await SignIn(factory,first,"first");await SignIn(factory,second,"second");
        var token=await Invite(first);(await Post(second,"together/accept",new {token})).EnsureSuccessStatusCode();
        var book=BookCatalogTests.Book;
        (await Post(first,"books/shelf",new {book,status="planned"})).EnsureSuccessStatusCode();
        var own=(await first.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf")).GetProperty("entries")[0];var id=own.GetProperty("id").GetInt64();
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf")).GetProperty("entries").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(second,$"books/shelf/{id}/status",new {status="read"})).StatusCode);
        await EnableSharing(first,"books");
        Assert.False((await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf")).GetProperty("entries")[0].GetProperty("mine").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(second,$"books/shelf/{id}/remove",new {})).StatusCode);
        (await Post(first,"privacy/cancel",new {category="books"})).EnsureSuccessStatusCode();
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf")).GetProperty("entries").EnumerateArray());
        (await Post(second,"books/shelf",new {book,status="planned"})).EnsureSuccessStatusCode();
        Assert.Single((await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf")).GetProperty("entries").EnumerateArray());
        (await Post(first,$"books/shelf/{id}/status",new {status="read"})).EnsureSuccessStatusCode();
        Assert.Contains((await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf")).GetProperty("entries").EnumerateArray(),x => x.GetProperty("status").GetString() == "read");
    }
    [Fact]
    public async Task BookRatingsAreIndependentMemberOnlyAndCascadeWithHistory()
    {
        using var factory=new AuthFactory();using var first=factory.Client();using var second=factory.Client();using var stranger=factory.Client();
        await SignIn(factory,first,"first");await SignIn(factory,second,"second");await SignIn(factory,stranger,"stranger");
        var token=await Invite(first);(await Post(second,"together/accept",new {token})).EnsureSuccessStatusCode();
        (await Post(first,"books/shelf",new {book=BookCatalogTests.Book,status="planned"})).EnsureSuccessStatusCode();
        var list=await first.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf");var id=list.GetProperty("entries")[0].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.NotFound,(await Post(first,$"books/shelf/{id}/rating",new {stars=5})).StatusCode);
        (await Post(first,$"books/shelf/{id}/status",new {status="read"})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(first,$"books/shelf/{id}/rating",new {stars=6})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(stranger,$"books/shelf/{id}/rating",new {stars=5})).StatusCode);
        (await Post(first,$"books/shelf/{id}/rating",new {stars=5})).EnsureSuccessStatusCode();
        (await Post(second,$"books/shelf/{id}/rating",new {stars=3})).EnsureSuccessStatusCode();
        list=await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf");
        Assert.Equal(3,list.GetProperty("entries")[0].GetProperty("ownRating").GetInt32());
        Assert.Equal(5,list.GetProperty("entries")[0].GetProperty("partnerRating").GetInt32());
        (await Post(first,$"books/shelf/{id}/rating",new {stars=4})).EnsureSuccessStatusCode();
        (await Post(first,$"books/shelf/{id}/rating",new {stars=(int?)null})).EnsureSuccessStatusCode();
        list=await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf");
        Assert.Equal(JsonValueKind.Null,list.GetProperty("entries")[0].GetProperty("partnerRating").ValueKind);
        Assert.Equal(3,list.GetProperty("entries")[0].GetProperty("ownRating").GetInt32());
        (await Post(first,$"books/shelf/{id}/remove",new {})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<BookRating>().ToListAsync());
    }
    [Fact]
    public async Task BookShelfIsSharedWithOnlyTheCurrentConnectionAndCleanedOnDisconnect()
    {
        using var factory = new AuthFactory(); using var first = factory.Client(); using var second = factory.Client(); using var stranger = factory.Client();
        await SignIn(factory,first,"first"); await SignIn(factory,second,"second"); await SignIn(factory,stranger,"stranger");
        var book=BookCatalogTests.Book;
        Assert.Equal(HttpStatusCode.Conflict,(await Post(first,"books/shelf",new {book,status="read"})).StatusCode);
        var token=await Invite(first); (await Post(second,"together/accept",new {token})).EnsureSuccessStatusCode();
        (await Post(first,"books/shelf",new {book,status="planned"})).EnsureSuccessStatusCode();
        Assert.Empty((await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf")).GetProperty("entries").EnumerateArray());
        await EnableSharing(first,"books");
        var list=await second.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf");
        var entry=Assert.Single(list.GetProperty("entries").EnumerateArray()); var id=entry.GetProperty("id").GetInt64();
        Assert.Equal("planned",entry.GetProperty("status").GetString());
        (await Post(second,$"books/shelf/{id}/status",new {status="read"})).EnsureSuccessStatusCode();
        list=await first.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf");
        Assert.Equal("read",list.GetProperty("entries")[0].GetProperty("status").GetString());
        Assert.Empty((await stranger.GetFromJsonAsync<JsonElement>("/api/auth/books/shelf")).GetProperty("entries").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(stranger,$"books/shelf/{id}/remove",new {})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(stranger,"books/search",new BookSearch(null,BasedOn:id))).StatusCode);
        (await Post(second,$"books/shelf/{id}/remove",new {})).EnsureSuccessStatusCode();
        (await Post(first,"books/shelf",new {book,status="read"})).EnsureSuccessStatusCode();
        (await Post(second,"together/leave",new {})).EnsureSuccessStatusCode();
        using var scope=factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<ReadingEntry>().ToListAsync());
    }
}
