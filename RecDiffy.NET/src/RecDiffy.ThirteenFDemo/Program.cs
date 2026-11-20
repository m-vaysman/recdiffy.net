using RecDiffy.ThirteenF;
using RecDiffy.ThirteenFDemo.Components;
using RecDiffy.ThirteenFDemo.Demo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

if (string.Equals(builder.Configuration["Demo:UseSampleData"], "true", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IThirteenFService, FakeThirteenFService>();
}
else
{
    var contact = builder.Configuration["Sec:UserAgentContact"];
    if (string.IsNullOrWhiteSpace(contact))
    {
        throw new InvalidOperationException(
            "SEC EDGAR requires a contact in the User-Agent header. Set configuration key 'Sec:UserAgentContact' (environment variable 'Sec__UserAgentContact'), or set 'Demo:UseSampleData=true' to run offline with sample data.");
    }

    builder.Services.AddThirteenF(contact);
}

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
