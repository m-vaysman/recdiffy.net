using System.IO;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"recdiffy-settings-{Guid.NewGuid():N}");

    [Fact]
    public void Settings_are_saved_to_the_file_and_read_back()
    {
        var path = Path.Combine(_folder, "RecDiffy", "settings.json");
        var store = new JsonSettingsStore(path);

        Assert.Null(store.Save(new AddInSettings(IncludeExactMatches: false, OffersExplanation: true)));

        Assert.Equal(new AddInSettings(false, true), new JsonSettingsStore(path).Load());
        Assert.False(File.Exists($"{path}.tmp"));
    }

    [Fact]
    public void A_missing_file_gives_the_defaults() =>
        Assert.Equal(AddInSettings.Default, new JsonSettingsStore(Path.Combine(_folder, "missing.json")).Load());

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "includeExactMatches": "no" }""")]
    public void A_file_that_cannot_be_read_as_settings_gives_the_defaults(string json) =>
        Assert.Equal(AddInSettings.Default, JsonSettingsStore.Parse(json));

    [Fact]
    public void A_setting_missing_from_the_file_keeps_its_default() =>
        Assert.Equal(new AddInSettings(IncludeExactMatches: true, OffersExplanation: false), JsonSettingsStore.Parse("""{ "offerExplanation": false }"""));

    [Fact]
    public void The_file_holds_both_settings_by_name() =>
        Assert.Equal(
            new AddInSettings(false, false),
            JsonSettingsStore.Parse(JsonSettingsStore.Format(new AddInSettings(IncludeExactMatches: false, OffersExplanation: false))));

    [Fact]
    public void A_file_that_cannot_be_written_says_why()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "settings.json"));
        var store = new JsonSettingsStore(Path.Combine(_folder, "settings.json"));

        Assert.False(string.IsNullOrEmpty(store.Save(AddInSettings.Default)));
    }

    [Fact]
    public void The_windows_user_file_is_under_app_data() =>
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RecDiffy", "settings.json"),
            JsonSettingsStore.ForWindowsUser());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}
