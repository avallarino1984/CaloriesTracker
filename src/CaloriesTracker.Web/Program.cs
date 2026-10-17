using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CaloriesTracker.Web;
using CaloriesTracker.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Single user, single browser tab: one shared state instance is enough.
builder.Services.AddSingleton<AppState>();

await builder.Build().RunAsync();
