using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaloriesTracker.Core;

/// <summary>Calories and macros for some amount of food.</summary>
public sealed record NutritionValues(double Kcal, double ProteinG, double CarbsG, double FatG)
{
    public static readonly NutritionValues Zero = new(0, 0, 0, 0);

    public NutritionValues Times(double factor) =>
        new(Kcal * factor, ProteinG * factor, CarbsG * factor, FatG * factor);

    public static NutritionValues operator +(NutritionValues a, NutritionValues b) =>
        new(a.Kcal + b.Kcal, a.ProteinG + b.ProteinG, a.CarbsG + b.CarbsG, a.FatG + b.FatG);
}

/// <summary>A packaged food with label values for one portion (as printed on the nutrition facts table).</summary>
public sealed class Food
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("nom")] public string Name { get; set; } = "";
    [JsonPropertyName("alias")] public List<string> Aliases { get; set; } = [];

    /// <summary>Portion the values refer to, e.g. "1 tasse (250 ml)".</summary>
    [JsonPropertyName("portion")] public string Portion { get; set; } = "";

    [JsonPropertyName("kcal")] public double Kcal { get; set; }
    [JsonPropertyName("proteines")] public double ProteinG { get; set; }
    [JsonPropertyName("glucides")] public double CarbsG { get; set; }
    [JsonPropertyName("lipides")] public double FatG { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }

    [JsonIgnore]
    public NutritionValues PerPortion => new(Kcal, ProteinG, CarbsG, FatG);
}

/// <summary>One food inside a favorite, as a number of label portions.</summary>
public sealed class FavoriteComponent
{
    [JsonPropertyName("aliment")] public string FoodId { get; set; } = "";
    [JsonPropertyName("portions")] public double Portions { get; set; } = 1;
}

/// <summary>A usual combination, e.g. coffee + 250 ml milk + sweetener.</summary>
public sealed class Favorite
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("nom")] public string Name { get; set; } = "";
    [JsonPropertyName("composants")] public List<FavoriteComponent> Components { get; set; } = [];
}

/// <summary>
/// Reference table of the user's usual foods (aliments.json), shared by the app and by Claude.
/// </summary>
public sealed class FoodCatalog
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("aliments")] public List<Food> Foods { get; set; } = [];
    [JsonPropertyName("favoris")] public List<Favorite> Favorites { get; set; } = [];

    /// <summary>Parses and validates the catalog. Throws <see cref="FormatException"/> on bad references.</summary>
    public static FoodCatalog Parse(string json)
    {
        var catalog = JsonSerializer.Deserialize<FoodCatalog>(json)
                      ?? throw new FormatException("Catalogue vide.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var food in catalog.Foods)
        {
            if (!ids.Add(food.Id))
            {
                throw new FormatException($"Aliment en double : « {food.Id} ».");
            }
        }
        foreach (var favorite in catalog.Favorites)
        {
            foreach (var component in favorite.Components)
            {
                if (!ids.Contains(component.FoodId))
                {
                    throw new FormatException($"Le favori « {favorite.Name} » utilise un aliment inconnu : « {component.FoodId} ».");
                }
            }
        }
        return catalog;
    }

    public Food? FindFood(string id) =>
        Foods.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Values for a number of label portions of one food.</summary>
    public static NutritionValues For(Food food, double portions) => food.PerPortion.Times(portions);

    /// <summary>Values for a favorite, multiplied by <paramref name="servings"/> (e.g. 2 coffees).</summary>
    public NutritionValues For(Favorite favorite, double servings = 1)
    {
        var total = NutritionValues.Zero;
        foreach (var component in favorite.Components)
        {
            var food = FindFood(component.FoodId)
                       ?? throw new InvalidOperationException($"Aliment inconnu : {component.FoodId}");
            total += For(food, component.Portions);
        }
        return total.Times(servings);
    }

    /// <summary>Builds a meal entry, rounding to one decimal like the label values.</summary>
    public static Meal ToMeal(string description, NutritionValues values, DateOnly date, MealType type) => new()
    {
        Date = date,
        Type = type,
        Description = description,
        Kcal = Math.Round(values.Kcal, 1),
        ProteinG = Math.Round(values.ProteinG, 1),
        CarbsG = Math.Round(values.CarbsG, 1),
        FatG = Math.Round(values.FatG, 1),
    };
}
