using System.Collections.Immutable;

namespace RecDiffy;

/// <summary>One reconciliation path: its route, its passes from strongest to weakest evidence, and its field tolerances. Every method returns a new builder.</summary>
public sealed class PathBuilder
{
    private readonly PathDraft _draft;

    private PathBuilder(PathDraft draft) => _draft = draft;

    /// <summary>A path that takes every row (<c>route.all</c>) until a route is set.</summary>
    public static PathBuilder Create(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new(new PathDraft(id, new RouteDeclaration("route.all", null, null), [], null));
    }

    public PathBuilder RouteAll() => new(_draft with { Route = new RouteDeclaration("route.all", null, null) });

    /// <summary>Takes the rows whose <paramref name="field"/> equals <paramref name="value"/> (<c>route.byField</c>).</summary>
    public PathBuilder RouteWhen(string field, string value)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);
        return new(_draft with { Route = new RouteDeclaration("route.byField", field, value) });
    }

    public PathBuilder SharedRef(string id, params string[] key) => Pass(id, "match.sharedRef", key, [], [], null, null);

    public PathBuilder NaturalKey(string id, params string[] key) => Pass(id, "match.naturalKey", key, [], [], null, null);

    public PathBuilder ReducedKey(string id, params string[] key) => Pass(id, "match.reducedKey", key, [], [], null, null);

    /// <summary>Matches several items on the <paramref name="many"/> side to one on the other, within a block, when the conserved fields sum.</summary>
    // An empty key is stored empty. sharedRef fills a default key later; this pass does not.
    public PathBuilder GroupSum(string id, Side many, IReadOnlyList<string> block, IReadOnlyList<string> conserve, int? maxGroupSize = null, IReadOnlyList<string>? key = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(conserve);
        return Pass(id, "match.groupSum", key ?? [], block, conserve, ProfileBuilder.SideName(many), maxGroupSize);
    }

    /// <summary>Adds any pass declaration, including one for a custom pass.</summary>
    public PathBuilder Pass(PassDeclaration pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        return Pass(pass.Id, pass.Pass, pass.Key, pass.Block, pass.Conserve, pass.Many, pass.MaxGroupSize);
    }

    /// <summary>
    /// Lets a decimal field differ by up to <paramref name="absolute"/>, or by <paramref name="relative"/> of the larger value, or either
    /// when both are given. The bound is inclusive. <paramref name="when"/> must hold on both sides; <paramref name="applicableWhen"/>
    /// compares the field only for rows it holds on, on either side.
    /// </summary>
    public PathBuilder Tolerance(string field, decimal? absolute = null, decimal? relative = null, WhenClause? when = null, WhenClause? applicableWhen = null)
    {
        if (absolute is null && relative is null)
            throw new ArgumentException("Give an absolute or a relative bound, or both.", nameof(absolute));
        var kind = (absolute, relative) switch
        {
            (not null, not null) => "AbsoluteOrRelative",
            (not null, null) => "Absolute",
            _ => "Relative",
        };
        return Diff(field, diff => diff with { Tolerance = new ToleranceDeclaration(kind, absolute, relative, null, Condition.Frozen(when)) }, applicableWhen);
    }

    /// <summary>Accepts a difference that a registered attribution formula, such as <c>trading.netCash</c>, fully explains.</summary>
    public PathBuilder AttributedTolerance(string field, string formula, WhenClause? when = null, WhenClause? applicableWhen = null)
    {
        ArgumentNullException.ThrowIfNull(formula);
        return Diff(field, diff => diff with { Tolerance = new ToleranceDeclaration("Attributed", null, null, formula, Condition.Frozen(when)) }, applicableWhen);
    }

    /// <summary>Names the formula that attributes this field's difference to its roots, alongside its tolerance.</summary>
    public PathBuilder Attribution(string field, string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        return Diff(field, diff => diff with { Attributed = formula }, null);
    }

    /// <summary>Compares the field only for rows where <paramref name="when"/> holds, on either side.</summary>
    public PathBuilder ApplicableWhen(string field, WhenClause when)
    {
        ArgumentNullException.ThrowIfNull(when);
        return Diff(field, diff => diff, when);
    }

    internal PathDeclaration ToDeclaration() =>
        new(_draft.Id, _draft.Route, [.. _draft.Passes], _draft.Diff is { } fields ? new DiffDeclaration(fields) : null);

    internal static PathBuilder From(PathDeclaration path) => new(new PathDraft(
        path.Id,
        path.Route,
        [.. path.Passes],
        path.Diff?.Fields.ToImmutableDictionary(StringComparer.Ordinal)));

    private PathBuilder Pass(string id, string pass, IReadOnlyList<string> key, IReadOnlyList<string> block, IReadOnlyList<string> conserve, string? many, int? maxGroupSize)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(key);
        return new(_draft with { Passes = _draft.Passes.Add(new PassDeclaration(id, pass, [.. key], [.. block], [.. conserve], many, maxGroupSize)) });
    }

    // A null applicableWhen leaves the clause already stored. It does not clear it.
    private PathBuilder Diff(string field, Func<FieldDiffDeclaration, FieldDiffDeclaration> change, WhenClause? applicableWhen)
    {
        ArgumentNullException.ThrowIfNull(field);
        var fields = _draft.Diff ?? ImmutableDictionary.Create<string, FieldDiffDeclaration>(StringComparer.Ordinal);
        var current = fields.GetValueOrDefault(field) ?? new FieldDiffDeclaration(new ToleranceDeclaration("Exact", null, null, null, null), null, null);
        var changed = change(current);
        var applied = applicableWhen is null ? changed : changed with { ApplicableWhen = Condition.Frozen(applicableWhen) };
        return new(_draft with { Diff = fields.SetItem(field, applied) });
    }

    private sealed record PathDraft(string Id, RouteDeclaration Route, ImmutableList<PassDeclaration> Passes, ImmutableDictionary<string, FieldDiffDeclaration>? Diff);
}

/// <summary>A package of linked rows on one side (legs of a swap, allocations of a block), merged into one item before matching.</summary>
public sealed class PackageBuilder
{
    private readonly PackagePlan _plan;

    private PackageBuilder(PackagePlan plan) => _plan = plan;

    /// <summary>Rows sharing a <paramref name="link"/> value form a package; <paramref name="role"/> names each row's role in it. Both are declared fields.</summary>
    public static PackageBuilder Create(string link = "Parent_Link_ID", string role = "Leg_Structure")
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(role);
        return new(new PackagePlan(link, role, [], []));
    }

    /// <summary>A package missing any of these roles is quarantined as incomplete.</summary>
    // Roles append. A second call keeps the roles from the first.
    public PackageBuilder RequireRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return new(_plan with { RequiredRoles = [.. _plan.RequiredRoles, .. roles] });
    }

    /// <summary>Every member must agree on the field; the package takes that value.</summary>
    public PackageBuilder Same(string field) => Aggregate(field, AggregateKind.Same, null, null);

    public PackageBuilder Sum(string field) => Aggregate(field, AggregateKind.Sum, null, null);

    public PackageBuilder FromRole(string field, string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return Aggregate(field, AggregateKind.FromRole, role, null);
    }

    /// <summary>The value of <paramref name="role"/> minus the value of <paramref name="otherRole"/>.</summary>
    public PackageBuilder DifferenceByRole(string field, string role, string? otherRole = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        return Aggregate(field, AggregateKind.DifferenceByRole, role, otherRole);
    }

    internal PackagePlan ToPlan() => _plan;

    private PackageBuilder Aggregate(string field, AggregateKind kind, string? role, string? otherRole)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new(_plan with { Aggregates = [.. _plan.Aggregates, new FieldAggregate(field, kind, role, otherRole)] });
    }
}
