using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class SetupProfilesTests
{
    [Fact]
    public void The_draft_profile_has_no_rules_yet_and_parses_back()
    {
        var draft = SetupProfiles.Draft();

        Assert.Empty(draft.Fields);
        Assert.Empty(draft.Paths);
        Assert.Equal(draft.ToJson(), ProfileDocument.Parse(draft.ToJson()).ToJson());
    }

    [Fact]
    public void Each_side_keeps_its_own_culture_in_the_profile()
    {
        var profile = SetupProfiles.WithCultures(SetupProfiles.Draft(), "de-DE", string.Empty);

        Assert.Equal("de-DE", SetupProfiles.CultureOf(profile, Side.Left));
        Assert.Equal(string.Empty, SetupProfiles.CultureOf(profile, Side.Right));
    }

    [Fact]
    public void A_profile_without_cultures_declares_none() =>
        Assert.Null(SetupProfiles.CultureOf(SetupProfiles.Draft(), Side.Left));

    [Fact]
    public void Setting_cultures_keeps_the_other_reading_settings()
    {
        var strict = SetupProfiles.Draft() with
        {
            SourceReadings = new Dictionary<string, SourceReading>(StringComparer.Ordinal) { ["left"] = new SourceReading("en-GB", false) },
        };

        var profile = SetupProfiles.WithCultures(strict, "de-DE", "fr-FR");

        Assert.Equal(new SourceReading("de-DE", false), profile.SourceReadings["left"]);
        Assert.Equal(new SourceReading("fr-FR", null), profile.SourceReadings["right"]);
    }

    [Fact]
    public void Setting_cultures_returns_a_new_profile_and_leaves_the_original_alone()
    {
        var draft = SetupProfiles.Draft();

        _ = SetupProfiles.WithCultures(draft, "de-DE", "fr-FR");

        Assert.Empty(draft.SourceReadings);
    }
}
