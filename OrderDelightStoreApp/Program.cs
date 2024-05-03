using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OrderDelightLibrary.Shared.Services;
using OrderDelightStoreApp;
using OrderDelightStoreApp.Services;
using Syncfusion.Blazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
			builder.Services.AddSyncfusionBlazor();
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");


//builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<Radzen.DialogService>();
builder.Services.AddScoped<Radzen.NotificationService>();
builder.Services.AddScoped<Radzen.TooltipService>();
builder.Services.AddScoped<Radzen.ContextMenuService>();



var isAzureHosted = Environment.GetEnvironmentVariable("IS_AZURE_HOSTED") == "true";
var useRemote = true;
// Register HttpClient for dependency injection
var baseAddress = "http://localhost:7071/"; // Default to local

if (isAzureHosted || useRemote)
{
    baseAddress = "https://orderly-list-store.azurewebsites.net/"; //builder.Configuration["api-url"];  
}
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(baseAddress) });
builder.Services.AddScoped<SharedService>();

// Register your StoreAppService with a factory delegate to inject HttpClient and configuration settings
builder.Services.AddScoped<StoreAppService>(sp => new StoreAppService(sp.GetRequiredService<HttpClient>(), builder.Configuration, new SharedService()));

await builder.Build().RunAsync();

