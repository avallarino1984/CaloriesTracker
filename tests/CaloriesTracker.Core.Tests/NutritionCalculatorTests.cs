using CaloriesTracker.Core;

namespace CaloriesTracker.Core.Tests;

public class NutritionCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    // Generic reference profile: 170 cm, 40 years old, desk job, 20 % deficit.
    private static Profile ReferenceProfile() => new()
    {
        Sex = Sex.Male,
        HeightCm = 170,
        BirthDate = new DateOnly(1986, 1, 1),
        ActivityFactor = 1.2,
        Deficit = 0.20,
        ProteinPerKg = 1.6,
        FatPerKg = 0.9,
        GoalWeightKg = 68,
    };

    private static WeightEntry Reading(double? bodyFat = 25) =>
        new() { Date = Today, WeightKg = 80, BodyFatPercent = bodyFat };

    [Fact]
    public void KatchMcArdle_MatchesHandCalculation()
    {
        // Lean mass = 80 * 0.75 = 60 kg -> 370 + 21.6 * 60 = 1666
        Assert.Equal(1666, NutritionCalculator.KatchMcArdle(80, 25), 2);
    }

    [Theory]
    // 10*80 + 6.25*170 - 5*40 + 5 = 1667.5
    [InlineData(Sex.Male, 80, 170, 40, 1667.5)]
    // 10*60 + 6.25*165 - 5*35 - 161 = 1295.25
    [InlineData(Sex.Female, 60, 165, 35, 1295.25)]
    public void MifflinStJeor_MatchesHandCalculation(Sex sex, double weight, double height, int age, double expected)
    {
        Assert.Equal(expected, NutritionCalculator.MifflinStJeor(sex, weight, height, age), 2);
    }

    [Fact]
    public void Compute_Auto_UsesKatchMcArdleWhenBodyFatKnown()
    {
        var t = NutritionCalculator.Compute(ReferenceProfile(), Reading(), Today);

        Assert.Equal(BmrMethod.KatchMcArdle, t.MethodUsed);
        Assert.Equal(1999.2, t.Tdee, 2);
        Assert.Equal(1599.36, t.TargetKcal, 2);
        Assert.Equal(128, t.ProteinG, 2);
        Assert.Equal(72, t.FatG, 2);
        // (1599.36 - 128*4 - 72*9) / 4 = 109.84
        Assert.Equal(109.84, t.CarbsG, 2);
    }

    [Fact]
    public void Compute_Auto_FallsBackToMifflinWithoutBodyFat()
    {
        var t = NutritionCalculator.Compute(ReferenceProfile(), Reading(bodyFat: null), Today);

        Assert.Equal(BmrMethod.MifflinStJeor, t.MethodUsed);
        Assert.Equal(1667.5, t.Bmr, 2);
    }

    [Fact]
    public void Compute_ForcedKatchWithoutBodyFat_FallsBackToMifflin()
    {
        var profile = ReferenceProfile();
        profile.Method = BmrMethod.KatchMcArdle;

        var t = NutritionCalculator.Compute(profile, Reading(bodyFat: null), Today);

        Assert.Equal(BmrMethod.MifflinStJeor, t.MethodUsed);
    }

    [Fact]
    public void Compute_MacrosAddUpToTarget()
    {
        var t = NutritionCalculator.Compute(ReferenceProfile(), Reading(), Today);

        double total = t.ProteinG * 4 + t.CarbsG * 4 + t.FatG * 9;
        Assert.Equal(t.TargetKcal, total, 6);
    }

    [Fact]
    public void Compute_WeeklyLossAndWeeksToGoal()
    {
        var t = NutritionCalculator.Compute(ReferenceProfile(), Reading(), Today);

        // ~400 kcal/day deficit -> ~0.36 kg/week -> 12 kg in ~33 weeks
        Assert.Equal(-0.363, t.WeeklyChangeKg, 3);
        Assert.NotNull(t.WeeksToGoal);
        Assert.InRange(t.WeeksToGoal!.Value, 33, 34);
    }

    [Fact]
    public void Compute_SurplusWhileGoalIsLower_HasNoWeeksToGoal()
    {
        var profile = ReferenceProfile();
        profile.Deficit = -0.10;

        var t = NutritionCalculator.Compute(profile, Reading(), Today);

        Assert.Null(t.WeeksToGoal);
    }

    [Fact]
    public void Compute_CarbsNeverNegative()
    {
        var profile = ReferenceProfile();
        profile.Deficit = 0.60;

        var t = NutritionCalculator.Compute(profile, Reading(), Today);

        Assert.Equal(0, t.CarbsG);
    }

    [Theory]
    [InlineData(2026, 10, 17, 40)]
    [InlineData(2025, 12, 31, 39)]
    public void AgeOn_CountsFullYears(int y, int m, int d, int expected)
    {
        Assert.Equal(expected, ReferenceProfile().AgeOn(new DateOnly(y, m, d)));
    }

    [Fact]
    public void DayTotals_SumsMeals()
    {
        var totals = DayTotals.From(
        [
            new Meal { Kcal = 400, ProteinG = 20, CarbsG = 40, FatG = 18 },
            new Meal { Kcal = 500, ProteinG = 35, CarbsG = 45, FatG = 20 },
        ]);

        Assert.Equal(new DayTotals(900, 55, 85, 38), totals);
    }
}
