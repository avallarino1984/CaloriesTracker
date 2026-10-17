using CaloriesTracker.Core;

namespace CaloriesTracker.Core.Tests;

public class MealLineParserTests
{
    [Fact]
    public void ParseLine_ValidLine()
    {
        var r = MealLineParser.ParseLine("MEAL|2026-10-17|dejeuner|Rôties, oeufs, café|400|20|40|18");

        Assert.True(r.IsSuccess);
        var m = r.Meal!;
        Assert.Equal(new DateOnly(2026, 10, 17), m.Date);
        Assert.Equal(MealType.Dejeuner, m.Type);
        Assert.Equal("Rôties, oeufs, café", m.Description);
        Assert.Equal(400, m.Kcal);
        Assert.Equal(20, m.ProteinG);
        Assert.Equal(40, m.CarbsG);
        Assert.Equal(18, m.FatG);
    }

    [Theory]
    [InlineData("Déjeuner", MealType.Dejeuner)]
    [InlineData("DÎNER", MealType.Diner)]
    [InlineData("souper", MealType.Souper)]
    [InlineData("Collation", MealType.Collation)]
    public void ParseLine_AcceptsAccentsAndCase(string keyword, MealType expected)
    {
        var r = MealLineParser.ParseLine($"MEAL|2026-10-17|{keyword}|x|100|1|2|3");

        Assert.Equal(expected, r.Meal!.Type);
    }

    [Fact]
    public void ParseLine_AcceptsDecimalCommaAndSpaces()
    {
        var r = MealLineParser.ParseLine("meal | 2026-10-17 | souper | Poulet riz | 495,5 | 33 | 46.5 | 20 ");

        Assert.True(r.IsSuccess);
        Assert.Equal(495.5, r.Meal!.Kcal);
        Assert.Equal(46.5, r.Meal.CarbsG);
        Assert.Equal("Poulet riz", r.Meal.Description);
    }

    [Theory]
    [InlineData("MEAL|2026-10-17|dejeuner|x|440|21|43", "8 champs")]
    [InlineData("MEAL|17-10-2026|dejeuner|x|440|21|43|21", "Date invalide")]
    [InlineData("MEAL|2026-10-17|brunch|x|440|21|43|21", "Repas inconnu")]
    [InlineData("MEAL|2026-10-17|dejeuner|x|abc|21|43|21", "kcal")]
    [InlineData("MEAL|2026-10-17|dejeuner|x|440|-1|43|21", "protéines")]
    public void ParseLine_InvalidInput_ReturnsError(string line, string expectedFragment)
    {
        var r = MealLineParser.ParseLine(line);

        Assert.False(r.IsSuccess);
        Assert.Contains(expectedFragment, r.Error);
    }

    [Fact]
    public void ParseMany_IgnoresNonMealLinesAndCodeFences()
    {
        const string pasted = """
            Environ 400 kcal pour ce déjeuner.

            ```
            MEAL|2026-10-17|dejeuner|Rôties|400|20|40|18
            ```
            `MEAL|2026-10-17|diner|Salade|350|25|20|18`
            MEAL|2026-10-17|souper|incomplet|500
            """;

        var results = MealLineParser.ParseMany(pasted);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsSuccess);
        Assert.Equal(MealType.Diner, results[1].Meal!.Type);
        Assert.False(results[2].IsSuccess);
    }

    [Fact]
    public void ParseMany_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(MealLineParser.ParseMany("   "));
        Assert.Empty(MealLineParser.ParseMany(null));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var meal = new Meal
        {
            Date = new DateOnly(2026, 10, 14),
            Type = MealType.Souper,
            Description = "Poulet | riz",
            Kcal = 495,
            ProteinG = 33,
            CarbsG = 46,
            FatG = 20.5,
        };

        string line = MealLineParser.Format(meal);
        var parsed = MealLineParser.ParseLine(line).Meal!;

        Assert.Equal("MEAL|2026-10-14|souper|Poulet / riz|495|33|46|20.5", line);
        Assert.Equal(meal.Kcal, parsed.Kcal);
        Assert.Equal(meal.FatG, parsed.FatG);
        Assert.Equal(meal.Type, parsed.Type);
    }
}
