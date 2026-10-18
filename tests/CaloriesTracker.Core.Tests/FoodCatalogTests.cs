using CaloriesTracker.Core;

namespace CaloriesTracker.Core.Tests;

public class FoodCatalogTests
{
    private const string Json = """
        {
          "version": 1,
          "aliments": [
            { "id": "lait", "nom": "Lait", "portion": "250 ml", "kcal": 160, "proteines": 18, "glucides": 9, "lipides": 5 },
            { "id": "cafe", "nom": "Café", "portion": "2 g", "kcal": 2, "proteines": 0.2, "glucides": 2, "lipides": 0 },
            { "id": "sucrant", "nom": "Sucrant", "portion": "0,5 g", "kcal": 0, "proteines": 0, "glucides": 0, "lipides": 0 }
          ],
          "favoris": [
            { "id": "cafe-lait", "nom": "Café au lait", "composants": [
              { "aliment": "cafe", "portions": 1 },
              { "aliment": "lait", "portions": 1 },
              { "aliment": "sucrant", "portions": 2 }
            ] }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsFoodsAndFavorites()
    {
        var catalog = FoodCatalog.Parse(Json);

        Assert.Equal(3, catalog.Foods.Count);
        Assert.Single(catalog.Favorites);
        Assert.Equal(18, catalog.FindFood("LAIT")!.ProteinG);
    }

    [Fact]
    public void Favorite_SumsComponents()
    {
        var catalog = FoodCatalog.Parse(Json);

        var v = catalog.For(catalog.Favorites[0]);

        Assert.Equal(new NutritionValues(162, 18.2, 11, 5), v);
    }

    [Fact]
    public void Favorite_ScalesWithServings()
    {
        var catalog = FoodCatalog.Parse(Json);

        var v = catalog.For(catalog.Favorites[0], servings: 2);

        Assert.Equal(324, v.Kcal, 6);
        Assert.Equal(36.4, v.ProteinG, 6);
    }

    [Fact]
    public void Food_ScalesWithPortions()
    {
        var lait = FoodCatalog.Parse(Json).FindFood("lait")!;

        // Half a cup (125 ml).
        Assert.Equal(new NutritionValues(80, 9, 4.5, 2.5), FoodCatalog.For(lait, 0.5));
    }

    [Fact]
    public void ToMeal_RoundsToOneDecimal()
    {
        var meal = FoodCatalog.ToMeal("x", new NutritionValues(100.04, 3.333, 1.25, 0.06), new DateOnly(2026, 10, 17), MealType.Collation);

        Assert.Equal(100, meal.Kcal);
        Assert.Equal(3.3, meal.ProteinG);
        Assert.Equal(MealType.Collation, meal.Type);
    }

    [Fact]
    public void Parse_UnknownFoodInFavorite_Throws()
    {
        const string bad = """{ "aliments": [], "favoris": [ { "id": "f", "nom": "F", "composants": [ { "aliment": "nope", "portions": 1 } ] } ] }""";

        var ex = Assert.Throws<FormatException>(() => FoodCatalog.Parse(bad));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void Parse_DuplicateId_Throws()
    {
        const string bad = """{ "aliments": [ { "id": "a", "nom": "A" }, { "id": "A", "nom": "B" } ] }""";

        Assert.Throws<FormatException>(() => FoodCatalog.Parse(bad));
    }

    [Fact]
    public void ShippedCatalog_IsValid()
    {
        // The real aliments.json published with the app must always parse.
        string path = Path.Combine(FindRepoRoot(), "src", "CaloriesTracker.Web", "wwwroot", "aliments.json");
        var catalog = FoodCatalog.Parse(File.ReadAllText(path));

        Assert.NotEmpty(catalog.Foods);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "CaloriesTracker.Web")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
