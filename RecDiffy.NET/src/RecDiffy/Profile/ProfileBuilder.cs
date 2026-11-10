using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RecDiffy;

/// <summary>Builds a <see cref="ProfileDocument"/> in code. Every method returns a new builder; none changes the one it is called on.</summary>
/// <remarks>
/// Column names are strings; everything else is typed. Each lambda overload has a value overload, so a program or a UI
/// can drive the builder without lambdas. <see cref="Build()"/> returns the document, or the problems that stop it.
/// </remarks>
public sealed class ProfileBuilder
{
    private readonly ProfileDraft _draft;

    private ProfileBuilder(ProfileDraft draft) => _draft = draft;

    public static ProfileBuilder Create(string profileId, string version)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(version);
        return new(ProfileDraft.Empty(profileId, version));
    }

    /// <summary>Loads an existing profile for editing. <c>From(document).Build()</c> gives back an equal document.</summary>
    public static ProfileBuilder From(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(new ProfileDraft(
            document.FormatVersion,
            document.ProfileId,
            document.Version,
            document.Midpoint,
            [.. document.Fields],
            document.SourceKeys.ToImmutableDictionary(StringComparer.Ordinal),
            document.Normalization.ToImmutableDictionary(pair => pair.Key, pair => SideBuilder.From(pair.Value), StringComparer.Ordinal),
            document.Consolidation.ToImmutableDictionary(StringComparer.Ordinal),
            [.. document.Paths.Select(PathBuilder.From)],
            document.Limits,
            document.Culture,
            document.StrictDateFormats,
            document.SourceReadings.ToImmutableDictionary(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Declares the culture both sides' text is read with, unless a side declares its own. Without a declaration the
    /// server's culture is used, read once when the profile is compiled.
    /// </summary>
    public ProfileBuilder Culture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return With(_draft with { Culture = culture.Name });
    }

    /// <summary>Declares the culture one side's text is read with. It overrides the run's culture for that side.</summary>
    public ProfileBuilder Culture(Side side, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Reading(side, reading => reading with { Culture = culture.Name });
    }

    /// <summary>
    /// Strict date formats, the default, read <c>/</c> and <c>:</c> as exactly those characters. False lets them also
    /// match the culture's date and time separators, as .NET does.
    /// </summary>
    public ProfileBuilder StrictDateFormats(bool strict) => With(_draft with { StrictDateFormats = strict });

    /// <summary>Sets strict date formats for one side. It overrides the run's setting for that side.</summary>
    public ProfileBuilder StrictDateFormats(Side side, bool strict) => Reading(side, reading => reading with { StrictDateFormats = strict });

    public ProfileBuilder RowKeys(string left, string right) => RowKey(Side.Left, left).RowKey(Side.Right, right);

    public ProfileBuilder RowKey(Side side, string column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return With(_draft with { SourceKeys = _draft.SourceKeys.SetItem(SideName(side), column) });
    }

    // A repeated name is stored. Build reports DuplicateField later.
    public ProfileBuilder Field(string name, FieldType type, FieldRole role, int scale = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        return With(_draft with { Fields = _draft.Fields.Add(new FieldDeclaration(name, type, role, scale)) });
    }

    public ProfileBuilder Key(string name, FieldType type, int scale = 0) => Field(name, type, FieldRole.Key, scale);

    public ProfileBuilder Economic(string name, FieldType type, int scale = 0) => Field(name, type, FieldRole.Economic, scale);

    public ProfileBuilder Identity(string name, FieldType type) => Field(name, type, FieldRole.Identity);

    public ProfileBuilder Lifecycle(string name, FieldType type) => Field(name, type, FieldRole.Lifecycle);

    public ProfileBuilder Package(string name, FieldType type) => Field(name, type, FieldRole.Package);

    public ProfileBuilder Left(Func<SideBuilder, SideBuilder> configure) => Configure(Side.Left, configure);

    public ProfileBuilder Right(Func<SideBuilder, SideBuilder> configure) => Configure(Side.Right, configure);

    public ProfileBuilder Left(SideBuilder rules) => Normalize(Side.Left, rules);

    public ProfileBuilder Right(SideBuilder rules) => Normalize(Side.Right, rules);

    /// <summary>Sets one side's normalization rules, replacing any set before.</summary>
    public ProfileBuilder Normalize(Side side, SideBuilder rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return With(_draft with { Normalization = _draft.Normalization.SetItem(SideName(side), rules) });
    }

    /// <summary>Collapses lifecycle events on one side into the surviving row before matching. A cancel removes its target, and a correction supersedes its predecessor.</summary>
    public ProfileBuilder Lifecycle(Side side, string action = "EventAction", string target = "EventTarget", string scope = "Counterparty", string corrPointsAt = "CANC")
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(corrPointsAt);
        var name = SideName(side);
        return With(_draft with { Consolidation = _draft.Consolidation.SetItem(name, Consolidation(name) with { Lifecycle = new LifecyclePlan(action, target, scope, corrPointsAt) }) });
    }

    /// <summary>Merges linked rows on one side into one package each, after any packages already added for that side.</summary>
    public ProfileBuilder Packages(Side side, params PackageBuilder[] packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var name = SideName(side);
        var current = Consolidation(name);
        return With(_draft with { Consolidation = _draft.Consolidation.SetItem(name, current with { Packages = [.. current.Packages, .. packages.Select(package => package.ToPlan())] }) });
    }

    public ProfileBuilder Midpoint(MidpointMode midpoint) => With(_draft with { Midpoint = midpoint });

    public ProfileBuilder Limits(SearchLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return With(_draft with { Limits = limits });
    }

    public ProfileBuilder Path(string id, Func<PathBuilder, PathBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Path(configure(PathBuilder.Create(id)));
    }

    public ProfileBuilder Path(PathBuilder path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return With(_draft with { Paths = _draft.Paths.Add(path) });
    }

    /// <summary>Checks the profile's structure and returns the document, or the problems found.</summary>
    public ProfileBuildResult Build() => Finish(ProfileValidation.Check(Document()), null);

    /// <summary>
    /// Also checks rules and parameters against <paramref name="registry"/> by compiling the document, so a profile that
    /// builds here compiles in <see cref="Reconciler.Compile"/>.
    /// </summary>
    public ProfileBuildResult Build(RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return Finish(ProfileValidation.Check(Document()), registry);
    }

    // Structural problems return before compile runs. Build without a registry never compiles.
    private static ProfileBuildResult Finish(ProfileCheckOutcome outcome, RuleRegistry? registry) => (outcome.Problems, registry) switch
    {
        ({ Count: > 0 }, _) => new ProfileBuildResult(null, outcome.Problems),
        (_, null) => new ProfileBuildResult(outcome.Document, []),
        (_, { } rules) => ProfileCompiler.Compile(outcome.Document, rules, CultureInfo.CurrentCulture) switch
        {
            { Errors: [] } => new ProfileBuildResult(outcome.Document, []),
            var failed => new ProfileBuildResult(null, [.. failed.Errors.Select(error => new ProfileProblem(error.Code, error.Message, "compile"))]),
        },
    };

    private ProfileDocument Document() => new()
    {
        FormatVersion = _draft.FormatVersion,
        ProfileId = _draft.ProfileId,
        Version = _draft.Version,
        Midpoint = _draft.Midpoint,
        Fields = [.. _draft.Fields],
        Normalization = _draft.Normalization.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToSide(), StringComparer.Ordinal),
        Paths = [.. _draft.Paths.Select(path => path.ToDeclaration())],
        SourceKeys = KeysForEverySide(),
        Limits = _draft.Limits,
        Consolidation = _draft.Consolidation,
        Culture = _draft.Culture,
        StrictDateFormats = _draft.StrictDateFormats,
        SourceReadings = _draft.SourceReadings,
    };

    // The writer puts every side that declares a reading under "sources", and the parser reads a side with no rowKey as ""
    // (the row ordinal). The built document records that "" too, so it compiles to the same hash after a JSON round trip
    // (#206).
    private ImmutableDictionary<string, string> KeysForEverySide() =>
        _draft.SourceReadings.Keys.Aggregate(_draft.SourceKeys, (keys, side) => keys.ContainsKey(side) ? keys : keys.Add(side, ""));

    private ProfileBuilder Reading(Side side, Func<SourceReading, SourceReading> change)
    {
        var name = SideName(side);
        var current = _draft.SourceReadings.GetValueOrDefault(name) ?? new SourceReading(null, null);
        return With(_draft with { SourceReadings = _draft.SourceReadings.SetItem(name, change(current)) });
    }

    // The lambda receives the side already stored, or an empty side. The builder it returns replaces that side.
    private ProfileBuilder Configure(Side side, Func<SideBuilder, SideBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Normalize(side, configure(_draft.Normalization.GetValueOrDefault(SideName(side)) ?? SideBuilder.Empty));
    }

    private ConsolidationSide Consolidation(string side) =>
        _draft.Consolidation.GetValueOrDefault(side) ?? new ConsolidationSide(null, []);

    private static ProfileBuilder With(ProfileDraft draft) => new(draft);

    internal static string SideName(Side side) => side switch
    {
        Side.Left => "left",
        Side.Right => "right",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "A side is Left or Right."),
    };
}

internal sealed record ProfileDraft(
    int FormatVersion,
    string ProfileId,
    string Version,
    MidpointMode Midpoint,
    ImmutableList<FieldDeclaration> Fields,
    ImmutableDictionary<string, string> SourceKeys,
    ImmutableDictionary<string, SideBuilder> Normalization,
    ImmutableDictionary<string, ConsolidationSide> Consolidation,
    ImmutableList<PathBuilder> Paths,
    SearchLimits Limits,
    string? Culture,
    bool? StrictDateFormats,
    ImmutableDictionary<string, SourceReading> SourceReadings)
{
    public static ProfileDraft Empty(string profileId, string version) => new(
        1,
        profileId,
        version,
        MidpointMode.ToEven,
        [],
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, SideBuilder>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, ConsolidationSide>(StringComparer.Ordinal),
        [],
        SearchLimits.Default,
        null,
        null,
        ImmutableDictionary.Create<string, SourceReading>(StringComparer.Ordinal));
}

/// <summary>The document a builder produced, or the problems that stopped it. Exactly one of the two is present.</summary>
public sealed record ProfileBuildResult(ProfileDocument? Document, IReadOnlyList<ProfileProblem> Problems)
{
    [MemberNotNullWhen(true, nameof(Document))]
    public bool Succeeded => Document is not null;
}

/// <summary>One reason a profile cannot be built. <see cref="Code"/> is stable; <see cref="Location"/> says where, for example <c>paths[trades].passes[P2].key[3]</c>.</summary>
public sealed record ProfileProblem(string Code, string Message, string Location);

/// <summary>The stable codes a <see cref="ProfileBuilder"/> reports. Compile problems keep the compiler's own codes.</summary>
public static class ProfileProblemCodes
{
    public const string DuplicateField = "DuplicateField";
    public const string UndeclaredField = "UndeclaredField";
    public const string UndeclaredRuleTarget = "UndeclaredRuleTarget";
    public const string UndeclaredPassField = "UndeclaredPassField";
    public const string UndeclaredDiffField = "UndeclaredDiffField";
    public const string ToleranceMismatch = "ToleranceMismatch";
    public const string DuplicatePath = "DuplicatePath";
    public const string DuplicatePass = "DuplicatePass";
    public const string MissingPath = "MissingPath";
}

/// <summary>Conditions for a rule's <c>when</c>, a tolerance's <c>when</c>, or a field's <c>applicableWhen</c>.</summary>
public static class Condition
{
    public static WhenClause Equal(string field, string value)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);
        return new WhenClause(field, value, null, null);
    }

    public static WhenClause NotEqual(string field, string value)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);
        return new WhenClause(field, null, value, null);
    }

    public static WhenClause In(string field, params string[] values)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(values);
        return new WhenClause(field, null, null, [.. values]);
    }

    internal static WhenClause? Frozen(WhenClause? when) => when is { In: { } values } ? when with { In = [.. values] } : when;
}
