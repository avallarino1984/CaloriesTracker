using CaloriesTracker.Core;

namespace CaloriesTracker.Core.Tests;

public class ActivityAdjustmentTests
{
    private static readonly DateOnly Today = new(2026, 10, 18);

    // BMR 1666 (Katch-McArdle, 60 kg lean mass), factor 1.2 -> 333.2 kcal of activity already included.
    private static NutritionTargets Base() => NutritionCalculator.Compute(
        new Profile { ActivityFactor = 1.2, Deficit = 0.20, ProteinPerKg = 1.6, FatPerKg = 0.9, GoalWeightKg = 68 },
        new WeightEntry { Date = Today, WeightKg = 80, BodyFatPercent = 25 },
        Today);

    [Fact]
    public void IncludedActivity_IsTdeeMinusBmr()
    {
        Assert.Equal(333.2, NutritionCalculator.IncludedActivityKcal(Base()), 2);
    }

    [Fact]
    public void ActiveBelowIncluded_AddsNothing()
    {
        var t = NutritionCalculator.AdjustForActivity(Base(), activeKcal: 300, share: 1);

        Assert.Equal(0, t.ActivityBonusKcal);
        Assert.Equal(Base().TargetKcal, t.TargetKcal, 6);
    }

    [Fact]
    public void ActiveAboveIncluded_AddsOnlyTheExcess()
    {
        var t = NutritionCalculator.AdjustForActivity(Base(), activeKcal: 533.2, share: 1);

        Assert.Equal(200, t.ActivityBonusKcal, 6);
        Assert.Equal(Base().TargetKcal + 200, t.TargetKcal, 6);
        Assert.Equal(Base().BaseTargetKcal, t.BaseTargetKcal, 6);
    }

    [Fact]
    public void HalfShare_AddsHalfTheExcess()
    {
        var t = NutritionCalculator.AdjustForActivity(Base(), activeKcal: 533.2, share: 0.5);

        Assert.Equal(100, t.ActivityBonusKcal, 6);
    }

    [Fact]
    public void ZeroShare_Disables()
    {
        var t = NutritionCalculator.AdjustForActivity(Base(), activeKcal: 900, share: 0);

        Assert.Equal(0, t.ActivityBonusKcal);
    }

    [Fact]
    public void Bonus_GoesToCarbs_MacrosStillAddUp()
    {
        var b = Base();
        var t = NutritionCalculator.AdjustForActivity(b, activeKcal: 533.2, share: 1);

        Assert.Equal(b.ProteinG, t.ProteinG);
        Assert.Equal(b.FatG, t.FatG);
        Assert.Equal(b.CarbsG + 50, t.CarbsG, 6);
        Assert.Equal(t.TargetKcal, t.ProteinG * 4 + t.CarbsG * 4 + t.FatG * 9, 6);
    }

    [Fact]
    public void AdjustingTwice_DoesNotStackBonuses()
    {
        var once = NutritionCalculator.AdjustForActivity(Base(), 533.2, 1);
        var twice = NutritionCalculator.AdjustForActivity(once, 433.2, 1);

        Assert.Equal(100, twice.ActivityBonusKcal, 6);
        Assert.Equal(Base().TargetKcal + 100, twice.TargetKcal, 6);
    }

    [Fact]
    public void ReadjustingToNoBonus_RestoresBaseCarbs()
    {
        var adjusted = NutritionCalculator.AdjustForActivity(Base(), 900, 1);
        var back = NutritionCalculator.AdjustForActivity(adjusted, 0, 1);

        Assert.Equal(Base().CarbsG, back.CarbsG, 6);
        Assert.Equal(Base().TargetKcal, back.TargetKcal, 6);
    }
}
