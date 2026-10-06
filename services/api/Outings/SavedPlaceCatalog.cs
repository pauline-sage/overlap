using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
namespace Overlap.Api.Outings;
public sealed record PlaceIdentity(string Name, string Description, string SourceUrl, string? PhotoUrl, string? PhotoSourceUrl, string? Credit, string? License, string? LicenseUrl);
public sealed class SavedPlaceCatalog(HttpClient http, IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector=protection.CreateProtector("Overlap.SavedPlaceCatalog.v1");
    private sealed record Snapshot(PlaceIdentity Place, DateTimeOffset Expires);
    public PlaceIdentity Read(string token) {
        try { var snapshot=JsonSerializer.Deserialize<Snapshot>(protector.Unprotect(token));
            if(snapshot is null || snapshot.Expires<clock.GetUtcNow())throw new ArgumentException();return snapshot.Place;
        } catch(Exception e) when(e is System.Security.Cryptography.CryptographicException or JsonException or ArgumentException) {throw new ArgumentException("Search for the place again; this selection expired.");}
    }
    private async Task<JsonElement> Get(string host,string query,CancellationToken ct) {
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://"+host+"/w/api.php?format=json&formatversion=2&"+query);
        request.Headers.UserAgent.ParseAdd("Overlap/1.0 (https://overlap-t7l6.onrender.com; saved places)");
        using var response=await http.SendAsync(request,ct);response.EnsureSuccessStatusCode();
        using var json=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);return json.RootElement.Clone();
    }
    private static string Text(JsonElement value,string name)=>value.TryGetProperty(name,out var field) ? field.GetString() ?? "" : "";
    private static string Meta(JsonElement value,string name)=>value.TryGetProperty(name,out var field) ? WebUtility.HtmlDecode(Regex.Replace(Text(field,"value"),"<[^>]*>"," ")).Trim() : "";
    public async Task<object[]> Search(string query,string language,CancellationToken ct) {
        if(string.IsNullOrWhiteSpace(query)||query.Length>200||language is not ("en" or "fr" or "pt"))throw new ArgumentException("Enter a place name and choose a search language.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(20));ct=timeout.Token;
        var host=language+".wikipedia.org";
        var json=await Get(host,"action=query&generator=search&gsrnamespace=0&gsrlimit=6&gsrsearch="+Uri.EscapeDataString(query.Trim())+"&prop=coordinates|pageimages|pageterms&piprop=name&pilicense=free&wbptterms=description",ct);
        if(!json.TryGetProperty("query",out var results)||!results.TryGetProperty("pages",out var pages))return [];
        var matches=new List<object>();
        foreach(var page in pages.EnumerateArray().OrderBy(p=>p.TryGetProperty("index",out var index)?index.GetInt32():99)) {
            if(!page.TryGetProperty("coordinates",out _))continue;
            string? photo=null,photoSource=null,credit=null,license=null,licenseUrl=null;
            var filename=Text(page,"pageimage");
            if(filename.Length>0){
                var images=await Get("commons.wikimedia.org","action=query&prop=imageinfo&iiprop=url|extmetadata&iiurlwidth=640&titles="+Uri.EscapeDataString("File:"+filename),ct);
                if(images.TryGetProperty("query",out var iq)&&iq.TryGetProperty("pages",out var ip))foreach(var image in ip.EnumerateArray()) {
                    if(!image.TryGetProperty("imageinfo",out var infos)||infos.GetArrayLength()==0)continue;var info=infos[0];
                    if(!info.TryGetProperty("extmetadata",out var metadata))continue;
                    var candidate=Text(info,"thumburl");var artist=Meta(metadata,"Artist");var terms=Meta(metadata,"LicenseShortName");
                    if(!Uri.TryCreate(candidate,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host is not ("upload.wikimedia.org" or "thumb.wikimedia.org")||artist.Length==0||terms.Length==0)continue;
                    photo=candidate;photoSource="https://commons.wikimedia.org/wiki/"+Uri.EscapeDataString("File:"+filename);credit=artist;license=terms;
                    var url=Meta(metadata,"LicenseUrl");if(Uri.TryCreate(url,UriKind.Absolute,out var lu)&&lu.Scheme is "https" or "http")licenseUrl=url;
                }
            }
            var title=Text(page,"title");var description="";
            if(page.TryGetProperty("terms",out var t)&&t.TryGetProperty("description",out var d)&&d.GetArrayLength()>0)description=d[0].GetString()??"";
            var place=new PlaceIdentity(title,description,"https://"+host+"/wiki/"+Uri.EscapeDataString(title.Replace(' ','_')),photo,photoSource,credit,license,licenseUrl);
            matches.Add(new {place,token=protector.Protect(JsonSerializer.Serialize(new Snapshot(place,clock.GetUtcNow().AddHours(2))))});
        }
        return matches.ToArray();
    }
}
