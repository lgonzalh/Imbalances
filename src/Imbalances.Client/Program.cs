using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Imbalances.Client;
using Imbalances.Client.Services;
using Imbalances.Client.UI.Core;
using Imbalances.Core.Services;
using MudBlazor.Services;
using Imbalances.Infrastructure;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ExplorerStateService>();
builder.Services.AddScoped<IStatePersistenceService, StatePersistenceService>();
builder.Services.AddScoped<UiFeedbackService>();
builder.Services.AddScoped<FirebaseMotorsService>();
builder.Services.AddScoped<UiConfigService>();
builder.Services.AddScoped<UiSnapshotService>();
builder.Services.AddMudServices();
builder.Services.AddInfrastructureServices();
builder.Services.AddScoped<IConfigService, BrowserConfigService>();

await builder.Build().RunAsync();
