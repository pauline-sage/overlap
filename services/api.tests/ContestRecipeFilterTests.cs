using Overlap.Api.Cooking;
using Xunit;
namespace Overlap.Api.Tests;
public class ContestRecipeFilterTests
{
    [Theory]
    [InlineData("Beef",true,"",false)]
    [InlineData("Pork sausages",true,"",false)]
    [InlineData("Bison",true,"",false)]
    [InlineData("Canned tuna",false,"tuna",false)]
    [InlineData("Chopped tomatoes",false,"tomato",false)]
    [InlineData("1 egg",false,"eggs",false)]
    [InlineData("Tomato puree",false,"tomatoes",false)]
    [InlineData("Champignons",true,"",true)]
    [InlineData("Milk",true,"tuna",true)]
    public void MatchesWholeIngredientsAndCommonPlurals(string ingredient,bool redMeat,string exclusion,bool allowed)
    {
        var recipe=new Recipe(1,1,"Dish",[ingredient],["Mix"],"https://en.wikibooks.org",null);
        Assert.Equal(allowed,ContestRecipeFilter.Allows(recipe,new ContestPreferences(redMeat,exclusion=="" ? [] : [exclusion])));
    }
    [Fact]
    public void BothListsAndInstructionIngredientsApply()
    {
        var recipe=new Recipe(1,1,"Dish",["Milk"],["Stir in the tuna"],"https://en.wikibooks.org",null);
        Assert.False(ContestRecipeFilter.Allows(recipe,new(false,[]),new(false,["tuna"])));
    }
}
