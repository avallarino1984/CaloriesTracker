using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CaloriesTracker.Core;
using Microsoft.JSInterop;

namespace CaloriesTracker.Web.Services;

/// <summary>Everything the app persists, stored as one JSON document.</summary>
public sealed class AppData
{
    public int Version { get; set; } = 1;

    /// <summary>Last local change; drives last-write-wins sync between devices.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public Profile Profile { get; set; } = new();
    public List<WeightEntry> Weights { get; set; } = [];
    public List<Meal> Meals { get; set; } = [];

    [JsonIgnore]
    public bool HasEntries => Weights.Count > 0 || Meals.Count > 0;
}

/// <summary>State of the cloud sync, shown in the Profile page.</summary>
public enum SyncStatus
{
    /// <summary>Not signed in (or Supabase not configured): data stays on this device.</summary>
    LocalOnly,
    Syncing,
    Synced,

    /// <summary>The last upload failed (offline, expired session...). Retried on the next change or refresh.</summary>
    Pending,
}

/// <summary>
/// App state, saved first to localStorage (works offline) and then synced to Supabase when signed in.
/// Pages only talk to this class, so the storage strategy can change without touching them.
/// </summary>
public sealed class AppState(IJSRuntime js, SupabaseClient cloud)
{
    private const string StorageKey = "calories-tracker:v1";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private AppData _data = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public bool IsLoaded { get; private set; }
    public SyncStatus Sync { get; private set; } = SyncStatus.LocalOnly;
    public string? SyncError { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public bool CloudAvailable => cloud.IsConfigured;
    public string? CloudEmail => cloud.Email;

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

        // Opened from the email link (e.g. on the Mac): sign in with the tokens in the URL.
        if (await TryTakeAuthRedirectAsync())
        {
            return;
        }

        // Show local data immediately; the cloud pull happens in the background.
        _ = SyncAsync();
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

    // ---------- Cloud ----------

    public Task SendCodeAsync(string email) => cloud.SendCodeAsync(email);

    /// <summary>Signs in, then reconciles: on a first sign-in the cloud copy wins if it exists.</summary>
    public async Task SignInAsync(string email, string code)
    {
        await cloud.VerifyCodeAsync(email, code);
        await SyncAsync(firstSyncOnDevice: true);
    }

    private async Task<bool> TryTakeAuthRedirectAsync()
    {
        if (!cloud.IsConfigured)
        {
            return false;
        }
        try
        {
            var url = await js.InvokeAsync<string?>("calories.takeAuthRedirect");
            if (url is null)
            {
                return false;
            }
            await cloud.VerifyCodeAsync("", url);
            _ = SyncAsync(firstSyncOnDevice: true);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(SyncStatus.LocalOnly, ex.Message);
            return false;
        }
    }

    public async Task SignOutAsync()
    {
        await cloud.SignOutAsync();
        SetStatus(SyncStatus.LocalOnly);
    }

    /// <summary>Pulls or pushes so this device and the cloud agree. Safe to call at any time.</summary>
    public async Task SyncAsync(bool firstSyncOnDevice = false)
    {
        if (!cloud.IsConfigured || !await cloud.IsSignedInAsync())
        {
            SetStatus(SyncStatus.LocalOnly);
            return;
        }

        await _syncLock.WaitAsync();
        try
        {
            SetStatus(SyncStatus.Syncing);
            var remote = await cloud.GetDocumentAsync();
            var action = SyncResolver.Resolve(_data.UpdatedAt, _data.HasEntries, remote?.UpdatedAt, firstSyncOnDevice);

            switch (action)
            {
                case SyncAction.UseRemote:
                    _data = remote!.Data.Deserialize<AppData>(JsonOptions) ?? new AppData();
                    _data.UpdatedAt = remote.UpdatedAt;
                    await SaveLocalAsync();
                    break;
                case SyncAction.PushLocal:
                    _data.UpdatedAt ??= NowMs();
                    await cloud.PutDocumentAsync(ToNode(_data), _data.UpdatedAt.Value);
                    break;
            }

            LastSyncedAt = DateTimeOffset.Now;
            SetStatus(SyncStatus.Synced);
        }
        catch (Exception ex)
        {
            SetStatus(await cloud.IsSignedInAsync() ? SyncStatus.Pending : SyncStatus.LocalOnly, ex.Message);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // ---------- Persistence ----------

    private async Task SaveAsync()
    {
        _data.UpdatedAt = NowMs();
        await SaveLocalAsync();
        Changed?.Invoke();

        // Push in the background; a failure leaves the status "Pending" and the next sync retries.
        _ = SyncAsync();
    }

    private Task SaveLocalAsync() =>
        js.InvokeVoidAsync("localStorage.setItem", StorageKey, ExportJson()).AsTask();

    // Postgres keeps microseconds and .NET keeps 100 ns ticks: round to milliseconds so timestamps compare equal.
    private static DateTimeOffset NowMs() =>
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static JsonNode ToNode(AppData data) =>
        JsonSerializer.SerializeToNode(data, JsonOptions)!;

    private void SetStatus(SyncStatus status, string? error = null)
    {
        Sync = status;
        SyncError = error;
        Changed?.Invoke();
    }
}
