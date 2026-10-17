using System.Text.Json;
using System.Text.Json.Serialization;
using CaloriesTracker.Core;
using Microsoft.JSInterop;

namespace CaloriesTracker.Web.Services;

/// <summary>Everything the app persists, stored as one JSON document.</summary>
public sealed class AppData
{
    public int Version { get; set; } = 1;
    public Profile Profile { get; set; } = new();
    public List<WeightEntry> Weights { get; set; } = [];
    public List<Meal> Meals { get; set; } = [];
}

/// <summary>
/// In-memory app state backed by the browser's localStorage.
/// Kept behind this class so it can later be swapped for Supabase sync without touching the pages.
/// </summary>
public sealed class AppState(IJSRuntime js)
{
    private const string StorageKey = "calories-tracker:v1";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private AppData _data = new();

    public bool IsLoaded { get; private set; }

    /// <summary>Raised after any change so components can re-render.</summary>
    public event Action? Changed;

    public Profile Profile => _data.Profile;
    public IReadOnlyList<WeightEntry> Weights => _data.Weights;
    public IReadOnlyList<Meal> Meals => _data.Meals;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Most recent scale reading, or null if none was entered yet.</summary>
    public WeightEntry? LatestWeight =>
        _data.Weights.OrderByDescending(w => w.Date).FirstOrDefault();

    /// <summary>Targets for today, or null until a weight has been entered.</summary>
    public NutritionTargets? CurrentTargets =>
        LatestWeight is { } weight ? NutritionCalculator.Compute(Profile, weight, Today) : null;

    public async Task LoadAsync()
    {
        if (IsLoaded)
        {
            return;
        }
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _data = JsonSerializer.Deserialize<AppData>(json, JsonOptions) ?? new AppData();
            }
        }
        catch (Exception ex)
        {
            // Corrupt or unavailable storage: start fresh rather than crash.
            Console.Error.WriteLine($"Failed to load data: {ex.Message}");
            _data = new AppData();
        }
        IsLoaded = true;
    }

    public IEnumerable<Meal> MealsOn(DateOnly day) =>
        _data.Meals.Where(m => m.Date == day).OrderBy(m => m.Type);

    public async Task AddMealsAsync(IEnumerable<Meal> meals)
    {
        _data.Meals.AddRange(meals);
        await SaveAsync();
    }

    public async Task DeleteMealAsync(Guid id)
    {
        _data.Meals.RemoveAll(m => m.Id == id);
        await SaveAsync();
    }

    /// <summary>Adds a reading, replacing any existing reading on the same day.</summary>
    public async Task UpsertWeightAsync(WeightEntry entry)
    {
        _data.Weights.RemoveAll(w => w.Date == entry.Date);
        _data.Weights.Add(entry);
        await SaveAsync();
    }

    public async Task DeleteWeightAsync(DateOnly date)
    {
        _data.Weights.RemoveAll(w => w.Date == date);
        await SaveAsync();
    }

    /// <summary>Persists profile edits made directly on <see cref="Profile"/>.</summary>
    public Task SaveProfileAsync() => SaveAsync();

    public string ExportJson() => JsonSerializer.Serialize(_data, JsonOptions);

    /// <summary>Replaces all data with an exported backup. Throws on invalid JSON.</summary>
    public async Task ImportJsonAsync(string json)
    {
        var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions)
                   ?? throw new JsonException("Fichier vide.");
        _data = data;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, ExportJson());
        Changed?.Invoke();
    }
}
