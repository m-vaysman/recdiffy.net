using RecDiffy.ThirteenF;
using RecDiffy.ThirteenFDemo.Components;
using RecDiffy.ThirteenFDemo.Demo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Swap point: replace FakeThirteenFService with the real RecDiffy.ThirteenF service registration.
builder.Services.AddSingleton<IThirteenFService, FakeThirteenFService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync().ConfigureAwait(false);
