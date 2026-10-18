namespace CaloriesTracker.Core;

/// <summary>Biological sex, used by the Mifflin-St Jeor equation.</summary>
public enum Sex
{
    Male,
    Female,
}

/// <summary>Meal slot of the day. Serialized as the French keyword used in MEAL lines.</summary>
public enum MealType
{
    Dejeuner,
    Diner,
    Souper,
    Collation,
}

/// <summary>Which BMR equation to use.</summary>
public enum BmrMethod
{
    /// <summary>Katch-McArdle when body fat is known, Mifflin-St Jeor otherwise.</summary>
    Auto,
    KatchMcArdle,
    MifflinStJeor,
}

/// <summary>Stable user settings. Weight and body fat come from the latest <see cref="WeightEntry"/>.</summary>
public sealed class Profile
{
    public Sex Sex { get; set; } = Sex.Male;
    public double HeightCm { get; set; } = 170;
    public DateOnly BirthDate { get; set; } = new(1990, 1, 1);

    /// <summary>TDEE multiplier (1.2 = desk job, no training).</summary>
    public double ActivityFactor { get; set; } = 1.2;

    /// <summary>Calorie deficit as a fraction of TDEE (0.20 = 20 %). Negative means surplus.</summary>
    public double Deficit { get; set; } = 0.15;

    public double ProteinPerKg { get; set; } = 1.6;
    public double FatPerKg { get; set; } = 0.9;
    public double GoalWeightKg { get; set; } = 70;
    public BmrMethod Method { get; set; } = BmrMethod.Auto;

    /// <summary>
    /// Share of the measured active calories (e.g. Apple Watch) added to the day's target,
    /// counted only above the activity already included in <see cref="ActivityFactor"/>. 0 disables it.
    /// </summary>
    public double ActivityBonusShare { get; set; } = 1.0;

    /// <summary>Age in full years on the given day.</summary>
    public int AgeOn(DateOnly day)
    {
        int age = day.Year - BirthDate.Year;
        if (day < BirthDate.AddYears(age))
        {
            age--;
        }
        return age;
    }
}

/// <summary>One scale reading (e.g. from the Renpho app).</summary>
public sealed class WeightEntry
{
    public DateOnly Date { get; set; }
    public double WeightKg { get; set; }

    /// <summary>Body fat percentage (22.5 = 22.5 %), or null when unknown.</summary>
    public double? BodyFatPercent { get; set; }
}

/// <summary>Active calories burned on a day, as measured by a watch (Apple Watch "Active Energy").</summary>
public sealed class ActivityEntry
{
    public DateOnly Date { get; set; }
    public double ActiveKcal { get; set; }
}

/// <summary>One logged meal with its estimated nutrition.</summary>
public sealed class Meal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public MealType Type { get; set; }
    public string Description { get; set; } = "";
    public double Kcal { get; set; }
    public double ProteinG { get; set; }
    public double CarbsG { get; set; }
    public double FatG { get; set; }
}

/// <summary>Daily calorie and macro targets computed from a profile and a weight reading.</summary>
public sealed record NutritionTargets(
    BmrMethod MethodUsed,
    double Bmr,
    double Tdee,
    double TargetKcal,
    double ProteinG,
    double FatG,
    double CarbsG,
    double WeeklyChangeKg,
    double? WeeksToGoal,
    double ActivityBonusKcal = 0)
{
    /// <summary>Daily target before any activity bonus.</summary>
    public double BaseTargetKcal => TargetKcal - ActivityBonusKcal;
}

/// <summary>Totals for a set of meals.</summary>
public sealed record DayTotals(double Kcal, double ProteinG, double CarbsG, double FatG)
{
    public static DayTotals From(IEnumerable<Meal> meals)
    {
        double kcal = 0, protein = 0, carbs = 0, fat = 0;
        foreach (var meal in meals)
        {
            kcal += meal.Kcal;
            protein += meal.ProteinG;
            carbs += meal.CarbsG;
            fat += meal.FatG;
        }
        return new DayTotals(kcal, protein, carbs, fat);
    }
}
