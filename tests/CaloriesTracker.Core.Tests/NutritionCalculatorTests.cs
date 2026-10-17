using CaloriesTracker.Core;

namespace CaloriesTracker.Core.Tests;

public class NutritionCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    // Reference profile: 165 cm, 42 years old, desk job, 20 % deficit.
    private static Profile AndresProfile() => new()
    {
        Sex = Sex.Male,
        HeightCm = 165,
        BirthDate = new DateOnly(1984, 1, 1),
        ActivityFactor = 1.2,
        Deficit = 0.20,
        ProteinPerKg = 1.6,
        FatPerKg = 0.9,
        GoalWeightKg = 68,
    };

    private static WeightEntry Reading(double? bodyFat = 25.8) =>
        new() { Date = Today, WeightKg = 79.65, BodyFatPercent = bodyFat };

    [Fact]
    public void KatchMcArdle_MatchesHandCalculation()
    {
        // Lean mass = 79.65 * 0.742 = 59.10 kg -> 370 + 21.6 * 59.10 = 1646.6
        Assert.Equal(1646.57, NutritionCalculator.KatchMcArdle(79.65, 25.8), 2);
    }

    [Theory]
    [InlineData(Sex.Male, 79.65, 165, 42, 1622.75)]
    // Female check against the "Calcul-Femme" sheet: 55 kg, 162 cm, 43 years -> 1186.5
    [InlineData(Sex.Female, 55, 162, 43, 1186.5)]
    public void MifflinStJeor_MatchesWorkbook(Sex sex, double weight, double height, int age, double expected)
    {
        Assert.Equal(expected, NutritionCalculator.MifflinStJeor(sex, weight, height, age), 2);
    }

    [Fact]
    public void Compute_Auto_UsesKatchMcArdleWhenBodyFatKnown()
    {
        var t = NutritionCalculator.Compute(AndresProfile(), Reading(), Today);

        Assert.Equal(BmrMethod.KatchMcArdle, t.MethodUsed);
        Assert.Equal(1975.88, t.Tdee, 2);
        Assert.Equal(1580.70, t.TargetKcal, 2);
        Assert.Equal(127.44, t.ProteinG, 2);
        Assert.Equal(71.685, t.FatG, 3);
        // (1580.70 - 127.44*4 - 71.685*9) / 4 = 106.44
        Assert.Equal(106.44, t.CarbsG, 2);
    }

    [Fact]
    public void Compute_Auto_FallsBackToMifflinWithoutBodyFat()
    {
        var t = NutritionCalculator.Compute(AndresProfile(), Reading(bodyFat: null), Today);

        Assert.Equal(BmrMethod.MifflinStJeor, t.MethodUsed);
        Assert.Equal(1622.75, t.Bmr, 2);
    }

    [Fact]
    public void Compute_ForcedKatchWithoutBodyFat_FallsBackToMifflin()
    {
        var profile = AndresProfile();
        profile.Method = BmrMethod.KatchMcArdle;

        var t = NutritionCalculator.Compute(profile, Reading(bodyFat: null), Today);

        Assert.Equal(BmrMethod.MifflinStJeor, t.MethodUsed);
    }

    [Fact]
    public void Compute_MacrosAddUpToTarget()
    {
        var t = NutritionCalculator.Compute(AndresProfile(), Reading(), Today);

        double total = t.ProteinG * 4 + t.CarbsG * 4 + t.FatG * 9;
        Assert.Equal(t.TargetKcal, total, 6);
    }

    [Fact]
    public void Compute_WeeklyLossAndWeeksToGoal()
    {
        var t = NutritionCalculator.Compute(AndresProfile(), Reading(), Today);

        // 395 kcal/day deficit -> ~0.36 kg/week -> 11.65 kg in ~32 weeks
        Assert.Equal(-0.359, t.WeeklyChangeKg, 3);
        Assert.NotNull(t.WeeksToGoal);
        Assert.InRange(t.WeeksToGoal!.Value, 32, 33);
    }

    [Fact]
    public void Compute_SurplusWhileGoalIsLower_HasNoWeeksToGoal()
    {
        var profile = AndresProfile();
        profile.Deficit = -0.10;

        var t = NutritionCalculator.Compute(profile, Reading(), Today);

        Assert.Null(t.WeeksToGoal);
    }

    [Fact]
    public void Compute_CarbsNeverNegative()
    {
        var profile = AndresProfile();
        profile.Deficit = 0.60;

        var t = NutritionCalculator.Compute(profile, Reading(), Today);

        Assert.Equal(0, t.CarbsG);
    }

    [Theory]
    [InlineData(2026, 10, 17, 42)]
    [InlineData(2025, 12, 31, 41)]
    public void AgeOn_CountsFullYears(int y, int m, int d, int expected)
    {
        Assert.Equal(expected, AndresProfile().AgeOn(new DateOnly(y, m, d)));
    }

    [Fact]
    public void DayTotals_SumsMeals()
    {
        var totals = DayTotals.From(
        [
            new Meal { Kcal = 440, ProteinG = 21, CarbsG = 43, FatG = 21 },
            new Meal { Kcal = 495, ProteinG = 33, CarbsG = 46, FatG = 20 },
        ]);

        Assert.Equal(new DayTotals(935, 54, 89, 41), totals);
    }
}
