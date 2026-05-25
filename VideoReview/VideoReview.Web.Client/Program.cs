using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VideoReview.Shared.Services;
using VideoReview.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Add device-specific services used by the VideoReview.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddSingleton<IVideoReviewService, BrowserVideoReviewService>();

await builder.Build().RunAsync();
