using CaloriesTracker.Core;

namespace CaloriesTracker.Web.Services;

/// <summary>Loads wwwroot/aliments.json once (the service worker keeps it available offline).</summary>
public sealed class FoodCatalogService(HttpClient http)
{
    private FoodCatalog? _catalog;

    /// <summary>Error to show when the catalog could not be loaded, or null.</summary>
    public string? Error { get; private set; }

    public async Task<FoodCatalog> GetAsync()
    {
        if (_catalog is not null)
        {
            return _catalog;
        }
        try
        {
            _catalog = FoodCatalog.Parse(await http.GetStringAsync("aliments.json"));
            Error = null;
        }
        catch (Exception ex)
        {
            // An empty catalog keeps the page usable; paste and manual entry still work.
            Error = $"Table des aliments illisible : {ex.Message}";
            _catalog = new FoodCatalog();
        }
        return _catalog;
    }
}
