using System.Collections.Immutable;

namespace RecDiffy.ExcelAddIn.ViewModels;

public static class SetupProfiles
{
    public static ProfileDocument Draft() => Canonical(new ProfileDocument
    {
        FormatVersion = 1,
        ProfileId = "reconciliation",
        Version = "1",
        Midpoint = MidpointMode.ToEven,
        Fields = [],
        Normalization = ImmutableDictionary.Create<string, NormalizationSide>(StringComparer.Ordinal),
        Paths = [],
        SourceKeys = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal),
        Limits = SearchLimits.Default,
    });

    public static ProfileDocument Canonical(ProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ProfileDocument.Parse(profile.ToJson(indented: false));
    }

    public static string? CultureOf(ProfileDocument profile, Side side)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.SourceReadings.GetValueOrDefault(Key(side))?.Culture;
    }

    public static ProfileDocument WithCultures(ProfileDocument profile, string left, string right)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var readings = profile.SourceReadings.ToImmutableDictionary(StringComparer.Ordinal);
        return Canonical(profile with
        {
            SourceReadings = readings
                .SetItem(Key(Side.Left), Reading(readings, Side.Left) with { Culture = left })
                .SetItem(Key(Side.Right), Reading(readings, Side.Right) with { Culture = right }),
        });
    }

    private static SourceReading Reading(ImmutableDictionary<string, SourceReading> readings, Side side) =>
        readings.GetValueOrDefault(Key(side)) ?? new SourceReading(null, null);

    private static string Key(Side side) => side switch
    {
        Side.Left => "left",
        Side.Right => "right",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "A side is Left or Right."),
    };
}
