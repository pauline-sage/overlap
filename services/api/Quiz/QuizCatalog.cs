using System.Text.Json;
using System.Security.Cryptography;
namespace Overlap.Api.Quiz;
public sealed record QuizQuestion(string Id,string CategoryId,string Prompt,string[] Choices);
public sealed record QuizCategory(string Id,string Title,bool Personal,QuizQuestion[] Questions);
public static class QuizCatalog
{
    public static readonly QuizCategory[] Categories=Load();
    private static QuizCategory[] Load(){
        using var stream=typeof(QuizCatalog).Assembly.GetManifestResourceStream("Overlap.Api.Quiz.questions.json")!;
        return JsonSerializer.Deserialize<QuizCategory[]>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    public static QuizQuestion[] Select(string[] categoryIds,IEnumerable<string> seen){
        if(categoryIds.Length==0 || categoryIds.Length>Categories.Length || categoryIds.Distinct().Count()!=categoryIds.Length || categoryIds.Any(id=>!Categories.Any(x=>x.Id==id)))throw new ArgumentException("Choose at least one valid category.");
        var previous=seen.ToHashSet();var candidates=Categories.Where(x=>categoryIds.Contains(x.Id)).SelectMany(x=>x.Questions).ToArray();
        RandomNumberGenerator.Shuffle(candidates.AsSpan());
        return candidates.OrderBy(x=>previous.Contains(x.Id)).Take(5).ToArray();
    }
}
