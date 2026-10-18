namespace CaloriesTracker.Core;

/// <summary>
/// Calorie and macro math ported from the "Calcul Macros" Excel workbook,
/// with the inconsistencies of the spreadsheet fixed (single deficit, single activity factor).
/// </summary>
public static class NutritionCalculator
{
    public const double KcalPerGramProtein = 4;
    public const double KcalPerGramCarbs = 4;
    public const double KcalPerGramFat = 9;

    /// <summary>Approximate energy content of 1 kg of body fat.</summary>
    public const double KcalPerKgBodyFat = 7700;

    /// <summary>Lean body mass in kg.</summary>
    public static double LeanMass(double weightKg, double bodyFatPercent) =>
        weightKg * (1 - bodyFatPercent / 100.0);

    /// <summary>Katch-McArdle BMR, based on lean body mass.</summary>
    public static double KatchMcArdle(double weightKg, double bodyFatPercent) =>
        370 + 21.6 * LeanMass(weightKg, bodyFatPercent);

    /// <summary>Mifflin-St Jeor BMR.</summary>
    public static double MifflinStJeor(Sex sex, double weightKg, double heightCm, int age) =>
        10 * weightKg + 6.25 * heightCm - 5 * age + (sex == Sex.Male ? 5 : -161);

    /// <summary>Computes the daily targets for the given profile and scale reading.</summary>
    public static NutritionTargets Compute(Profile profile, WeightEntry weight, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(weight);
        if (weight.WeightKg <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be positive.");
        }

        var method = ResolveMethod(profile.Method, weight.BodyFatPercent);
        double bmr = method == BmrMethod.KatchMcArdle
            ? KatchMcArdle(weight.WeightKg, weight.BodyFatPercent!.Value)
            : MifflinStJeor(profile.Sex, weight.WeightKg, profile.HeightCm, profile.AgeOn(today));

        double tdee = bmr * profile.ActivityFactor;
        double target = tdee * (1 - profile.Deficit);

        double protein = weight.WeightKg * profile.ProteinPerKg;
        double fat = weight.WeightKg * profile.FatPerKg;
        double carbsKcal = target - protein * KcalPerGramProtein - fat * KcalPerGramFat;
        double carbs = Math.Max(0, carbsKcal / KcalPerGramCarbs);

        // Negative when losing weight.
        double weeklyChange = (target - tdee) * 7 / KcalPerKgBodyFat;
        double? weeksToGoal = WeeksToGoal(weight.WeightKg, profile.GoalWeightKg, weeklyChange);

        return new NutritionTargets(method, bmr, tdee, target, protein, fat, carbs, weeklyChange, weeksToGoal);
    }

    /// <summary>Activity calories already assumed by the activity factor: TDEE − BMR.</summary>
    public static double IncludedActivityKcal(NutritionTargets targets) => targets.Tdee - targets.Bmr;

    /// <summary>
    /// Raises the day's target with measured active calories. Only the part above the activity already
    /// included in the activity factor counts (otherwise it would be counted twice), times <paramref name="share"/>.
    /// The extra calories go to carbohydrates; protein and fat targets stay weight-based.
    /// </summary>
    public static NutritionTargets AdjustForActivity(NutritionTargets targets, double activeKcal, double share)
    {
        double excess = Math.Max(0, activeKcal - IncludedActivityKcal(targets));
        double bonus = excess * Math.Clamp(share, 0, 1);

        // Always rebuild from the base target so re-adjusting never stacks or leaves stale carbs.
        double target = targets.BaseTargetKcal + bonus;
        double carbs = Math.Max(0, (target - targets.ProteinG * KcalPerGramProtein - targets.FatG * KcalPerGramFat) / KcalPerGramCarbs);
        return targets with { TargetKcal = target, CarbsG = carbs, ActivityBonusKcal = bonus };
    }

    /// <summary>Minimum sensible daily intake; below this the UI shows a warning.</summary>
    public static double MinimumSafeKcal(Sex sex) => sex == Sex.Male ? 1500 : 1200;

    private static BmrMethod ResolveMethod(BmrMethod requested, double? bodyFat) => requested switch
    {
        BmrMethod.KatchMcArdle when bodyFat is null => BmrMethod.MifflinStJeor,
        BmrMethod.Auto => bodyFat is null ? BmrMethod.MifflinStJeor : BmrMethod.KatchMcArdle,
        _ => requested,
    };

    private static double? WeeksToGoal(double current, double goal, double weeklyChange)
    {
        double remaining = goal - current;
        if (Math.Abs(remaining) < 0.05)
        {
            return 0;
        }
        // Only meaningful when the weekly change moves toward the goal.
        if (weeklyChange == 0 || Math.Sign(remaining) != Math.Sign(weeklyChange))
        {
            return null;
        }
        return remaining / weeklyChange;
    }
}
