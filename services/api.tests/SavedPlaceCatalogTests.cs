using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Overlap.Api.Outings;
using Xunit;
namespace Overlap.Api.Tests;
public sealed class SavedPlaceCatalogTests
{
    internal sealed class Handler:HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            Assert.Contains(request.RequestUri!.Host,new[]{"en.wikipedia.org","commons.wikimedia.org"});
            var json=request.RequestUri.Host=="en.wikipedia.org" ? """
            {"query":{"pages":[{"title":"Natural History Museum","index":1,"coordinates":[{}],"pageimage":"Museum.jpg","terms":{"description":["Museum in New York"]}},{"title":"Some person","index":2}]}}
            """ : """
            {"query":{"pages":[{"imageinfo":[{"thumburl":"https://upload.wikimedia.org/museum.jpg","extmetadata":{"Artist":{"value":"<a>Photographer</a>"},"LicenseShortName":{"value":"CC BY-SA 4.0"},"LicenseUrl":{"value":"https://creativecommons.org/licenses/by-sa/4.0/"}}}]}]}}
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
    [Fact]
    public async Task SearchReturnsPlacesWithCreditedPhotosAndProtectedSelections(){
        var catalog=new SavedPlaceCatalog(new HttpClient(new Handler()),new EphemeralDataProtectionProvider(),TimeProvider.System);
        var matches=await catalog.Search("Museum New York","en",default);Assert.Single(matches);
        var item=JsonSerializer.SerializeToElement(matches)[0];var token=item.GetProperty("token").GetString()!;var place=catalog.Read(token);
        Assert.Equal("Natural History Museum",place.Name);Assert.Equal("Photographer",place.Credit);Assert.Equal("CC BY-SA 4.0",place.License);Assert.StartsWith("https://upload.wikimedia.org/",place.PhotoUrl);
        Assert.Throws<ArgumentException>(()=>catalog.Read(token+"tampered"));
        await Assert.ThrowsAsync<ArgumentException>(()=>catalog.Search("Museum","invalid",default));
    }
}
