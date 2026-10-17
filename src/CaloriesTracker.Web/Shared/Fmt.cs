using System.Globalization;
using CaloriesTracker.Core;

namespace CaloriesTracker.Web.Shared;

/// <summary>French display helpers.</summary>
public static class Fmt
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    public static string Kcal(double value) => Math.Round(value).ToString("N0", Fr);

    public static string Grams(double value) => Math.Round(value).ToString("N0", Fr) + " g";

    public static string Kg(double value) => value.ToString("0.0#", Fr) + " kg";

    public static string Day(DateOnly day)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (day == today) return "Aujourd'hui";
        if (day == today.AddDays(-1)) return "Hier";
        return day.ToString("dddd d MMMM", Fr);
    }

    public static string MealLabel(MealType type) => type switch
    {
        MealType.Dejeuner => "Déjeuner",
        MealType.Diner => "Dîner",
        MealType.Souper => "Souper",
        _ => "Collation",
    };

    public static string MethodLabel(BmrMethod method) => method switch
    {
        BmrMethod.KatchMcArdle => "Katch-McArdle",
        BmrMethod.MifflinStJeor => "Mifflin-St Jeor",
        _ => "Auto",
    };

    /// <summary>Percentage for a progress bar, capped at 100.</summary>
    public static double Pct(double value, double target) =>
        target <= 0 ? 0 : Math.Clamp(value / target * 100, 0, 100);
}
