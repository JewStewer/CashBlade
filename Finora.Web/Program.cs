using Finora.Web;
using Finora.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient());
builder.Services.AddSingleton<IndexedDbService>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton<UpBankWebSyncService>();
builder.Services.AddScoped<StripeCardService>();
builder.Services.AddSingleton<AppState>();

await builder.Build().RunAsync();
