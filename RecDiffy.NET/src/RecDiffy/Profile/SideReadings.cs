using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy;

internal sealed record ResolvedReading(CultureInfo Culture, CultureOrigin Origin, bool StrictDateFormats)
{
    public SideReading Stamp() =>
        new(Culture.Name, Culture.NumberFormat.NumberDecimalSeparator, Culture.NumberFormat.NumberGroupSeparator, Origin, StrictDateFormats);
}

internal sealed record ReadingOutcome(ImmutableDictionary<string, ResolvedReading> Sides, IReadOnlyList<ProfileError> Errors);

// Each side resolves once, at compile: its own culture, else the run's, else the server's. Strictness resolves the same way and defaults to strict.
internal static class SideReadings
{
    private static readonly string[] Sides = ["left", "right"];

    public static ReadingOutcome Resolve(ProfileDocument document, CultureInfo server)
    {
        var outcomes = Sides.Select(side => (Side: side, Outcome: Resolve(document, side, server))).ToArray();
        return new ReadingOutcome(
            outcomes
                .Where(pair => pair.Outcome.Reading is not null)
                .ToImmutableDictionary(pair => pair.Side, pair => pair.Outcome.Reading!, StringComparer.Ordinal),
            [.. outcomes.SelectMany(pair => pair.Outcome.Errors)]);
    }

    private static (ResolvedReading? Reading, IReadOnlyList<ProfileError> Errors) Resolve(ProfileDocument document, string side, CultureInfo server)
    {
        var own = document.SourceReadings.GetValueOrDefault(side);
        var strict = own?.StrictDateFormats ?? document.StrictDateFormats ?? true;
        return (own?.Culture, document.Culture) switch
        {
            ({ } name, _) => Named(name, CultureOrigin.Side, strict, $"sources.{side}.culture"),
            (null, { } name) => Named(name, CultureOrigin.Run, strict, "culture"),
            _ => (new ResolvedReading(CultureInfo.ReadOnly(server), CultureOrigin.Server, strict), []),
        };
    }

    private static (ResolvedReading? Reading, IReadOnlyList<ProfileError> Errors) Named(string name, CultureOrigin origin, bool strict, string location) =>
        Known(name) is { } culture
            ? (new ResolvedReading(culture, origin, strict), [])
            : (null, [new ProfileError("UnknownCulture", $"{location} '{name}' is not a culture this host knows.")]);

    // predefinedOnly rejects a made-up name instead of building a culture for it. A host in invariant-globalization mode knows only "".
    private static CultureInfo? Known(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
