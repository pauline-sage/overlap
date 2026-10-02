using System.Text.RegularExpressions;
namespace Overlap.Api.Cooking;

public sealed record ContestPreferences(bool AvoidRedMeat, string[] ExcludedIngredients);
public static class ContestRecipeFilter
{
    private static readonly string[] RedMeat=["beef","veal","pork","lamb","mutton","venison","bison","buffalo","boar","goat","horse","elk","moose","bacon","ham","prosciutto","chorizo","salami","pepperoni","steak","mince","sausage","meat"];
    public static bool Allows(Recipe recipe, params ContestPreferences[] preferences)
    {
        var text=string.Join(" ",recipe.Ingredients.Concat(recipe.Instructions).Prepend(recipe.Title));
        foreach(var preference in preferences)
        {
            var exclusions=preference.ExcludedIngredients.AsEnumerable();
            if(preference.AvoidRedMeat)exclusions=exclusions.Concat(RedMeat);
            foreach(var ingredient in exclusions)
            {
                var term=ingredient.Trim();
                var variants=new List<string>{term};
                if(term.Length>3 && term.EndsWith('s') && !term.EndsWith("ss",StringComparison.OrdinalIgnoreCase))variants.Add(term[..^1]);
                if(term.EndsWith("oes",StringComparison.OrdinalIgnoreCase))variants.Add(term[..^2]);
                foreach(var variant in variants)
                    if(Regex.IsMatch(text,@"(?<!\p{L})"+Regex.Escape(variant)+@"(?:s|es)?(?!\p{L})",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))return false;
            }
        }
        return true;
    }
}
