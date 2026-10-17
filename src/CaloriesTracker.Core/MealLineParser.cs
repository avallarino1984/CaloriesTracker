using System.Globalization;

namespace CaloriesTracker.Core;

/// <summary>Result of parsing one input line.</summary>
public sealed record MealParseResult(int LineNumber, string RawLine, Meal? Meal, string? Error)
{
    public bool IsSuccess => Meal is not null;
}

/// <summary>
/// Parses the standard line produced by Claude for a meal photo:
/// <c>MEAL|2026-10-17|dejeuner|Arepa, oeuf, café|440|21|43|21</c>
/// (date | meal type | description | kcal | protein g | carbs g | fat g).
/// </summary>
public static class MealLineParser
{
    public const string Prefix = "MEAL";
    private const int FieldCount = 8;

    /// <summary>
    /// Parses every MEAL line of a pasted block. Lines that do not start with MEAL are ignored,
    /// so a whole chat answer or a phone note can be pasted as is.
    /// </summary>
    public static IReadOnlyList<MealParseResult> ParseMany(string? text)
    {
        var results = new List<MealParseResult>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return results;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim().Trim('`').Trim();
            if (IsMealLine(line))
            {
                results.Add(ParseLine(line, i + 1));
            }
        }
        return results;
    }

    /// <summary>True when the line starts with "MEAL", optional spaces, then "|".</summary>
    private static bool IsMealLine(string line)
    {
        if (!line.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return line.AsSpan(Prefix.Length).TrimStart().StartsWith("|");
    }

    /// <summary>Parses a single MEAL line.</summary>
    public static MealParseResult ParseLine(string line, int lineNumber = 1)
    {
        var fields = line.Split('|').Select(f => f.Trim()).ToArray();
        if (fields.Length != FieldCount || !fields[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"{FieldCount} champs attendus séparés par « | », {fields.Length} trouvés.");
        }

        if (!DateOnly.TryParseExact(fields[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Fail($"Date invalide « {fields[1]} » (format AAAA-MM-JJ).");
        }

        if (!TryParseMealType(fields[2], out var type))
        {
            return Fail($"Repas inconnu « {fields[2]} » (dejeuner, diner, souper, collation).");
        }

        var numbers = new double[4];
        string[] names = ["kcal", "protéines", "glucides", "lipides"];
        for (int n = 0; n < 4; n++)
        {
            if (!TryParseNumber(fields[4 + n], out numbers[n]) || numbers[n] < 0)
            {
                return Fail($"Valeur invalide pour {names[n]} : « {fields[4 + n]} ».");
            }
        }

        var meal = new Meal
        {
            Date = date,
            Type = type,
            Description = fields[3],
            Kcal = numbers[0],
            ProteinG = numbers[1],
            CarbsG = numbers[2],
            FatG = numbers[3],
        };
        return new MealParseResult(lineNumber, line, meal, null);

        MealParseResult Fail(string error) => new(lineNumber, line, null, error);
    }

    /// <summary>Formats a meal back into its MEAL line (used for export).</summary>
    public static string Format(Meal meal) => string.Join('|',
        Prefix,
        meal.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ToKeyword(meal.Type),
        meal.Description.Replace('|', '/'),
        meal.Kcal.ToString("0.#", CultureInfo.InvariantCulture),
        meal.ProteinG.ToString("0.#", CultureInfo.InvariantCulture),
        meal.CarbsG.ToString("0.#", CultureInfo.InvariantCulture),
        meal.FatG.ToString("0.#", CultureInfo.InvariantCulture));

    public static string ToKeyword(MealType type) => type switch
    {
        MealType.Dejeuner => "dejeuner",
        MealType.Diner => "diner",
        MealType.Souper => "souper",
        _ => "collation",
    };

    public static bool TryParseMealType(string value, out MealType type)
    {
        // Accept accents and casing variations ("Déjeuner", "DÎNER").
        string key = RemoveAccents(value).ToLowerInvariant();
        switch (key)
        {
            case "dejeuner": type = MealType.Dejeuner; return true;
            case "diner": type = MealType.Diner; return true;
            case "souper": type = MealType.Souper; return true;
            case "collation": type = MealType.Collation; return true;
            default: type = default; return false;
        }
    }

    private static bool TryParseNumber(string value, out double result) =>
        // Accept both "21.5" and "21,5".
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static string RemoveAccents(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }
}
