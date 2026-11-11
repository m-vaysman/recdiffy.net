using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy;

// Builds one item per linked package, by the field rules of spec v3 §5.2. No value comes from "the first row".
internal static class Packages
{
    public static IReadOnlyList<ConsolidatedRow> Pack(IReadOnlyList<ConsolidatedRow> rows, PackagePlan plan) =>
        Pack(rows, plan, FieldUse.Everything);

    public static IReadOnlyList<ConsolidatedRow> Pack(IReadOnlyList<ConsolidatedRow> rows, PackagePlan plan, FieldUse use)
    {
        var active = rows.Where(row => row is { Quarantined: false, Suppression: null }).ToArray();
        var linked = active.Where(row => row.Context.Display(plan.LinkField) is not null).ToArray();
        var groups = linked.GroupBy(row => row.Context.Display(plan.LinkField)!, StringComparer.Ordinal);
        var packed = groups.SelectMany(group => Build(group.ToArray(), plan, use)).ToArray();
        var consumed = linked.Select(row => row.Context.RowKey).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. rows.Where(row => consumed.Contains(row.Context.RowKey) is false),
            .. packed,
        ];
    }

    // Members are ordered by row key, so nothing below depends on input order (§10.1).
    private static IReadOnlyList<ConsolidatedRow> Build(IReadOnlyList<ConsolidatedRow> unordered, PackagePlan plan, FieldUse use)
    {
        var members = unordered.OrderBy(member => member.Context.RowKey, StringComparer.Ordinal).ToArray();
        return plan.RequiredRoles.Where(role => members.Any(member => member.Context.Display(plan.RoleField) == role) is false).ToArray() switch
        {
            { Length: > 0 } => Quarantine(members, "IncompletePackage"),
            _ => Aggregate(members, plan, use) switch
            {
                { Failure: { } code } => Quarantine(members, code),
                { Context: { } context } =>
                [
                    new ConsolidatedRow(
                        [.. members.SelectMany(member => member.Members)],
                        context,
                        false,
                        null,
                        null,
                        true,
                        members.Any(member => member.LifecycleCollapsed),
                        Lineage(members),
                        OwnDiagnostics(members)),
                ],
            },
        };
    }

    private static SourceRef[]? Lineage(IReadOnlyList<ConsolidatedRow> members) =>
        members.Any(member => member.Lineage is not null)
            ? [.. members.SelectMany(member => member.Lineage ?? member.Members).Distinct()]
            : null;

    // Each leg keeps its own diagnostics, such as DuplicateRowCollapsed for a copied leg, so result building reports a
    // warning on the leg it belongs to and not on the whole package (spec v3 §3.2, §3.4; #217).
    private static Dictionary<SourceRef, ImmutableList<Diagnostic>> OwnDiagnostics(IReadOnlyList<ConsolidatedRow> members) =>
        members.SelectMany(member => member.Members.Select(source => (Source: source, member.Context.Diagnostics))).ToDictionary(pair => pair.Source, pair => pair.Diagnostics);

    // Fields without an aggregator are settled first: members that agree give the value, and members that disagree make
    // it unknown, with MembersDisagree when the run uses the field. Then each aggregator writes its own field.
    // The item starts with no leg's diagnostics and only the raw columns every leg agrees on, so nothing comes from the
    // first leg by row key (§5.2, §10.1; #217).
    private static PackageProjection Aggregate(IReadOnlyList<ConsolidatedRow> members, PackagePlan plan, FieldUse use)
    {
        var contexts = members.Select(member => member.Context).ToArray();
        var aggregated = plan.Aggregates.Select(aggregate => aggregate.Field).ToHashSet(StringComparer.Ordinal);
        var start = contexts[0] with { Raw = AgreedRaw(contexts), Diagnostics = [] };
        var warned = DisagreeingRawColumns(contexts, start.Raw, use)
            .Aggregate(start, (context, column) => context.Diagnose(MemberAgreement.Disagree, DiagnosticSeverity.Warning, "package", column, null));
        var settled = contexts
            .SelectMany(context => context.Values.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(field => aggregated.Contains(field) is false)
            .Order(StringComparer.Ordinal)
            .Aggregate(warned, (context, field) => Settle(context, field, contexts, use));
        return plan.Aggregates.Aggregate(
            new PackageProjection(settled, null),
            (projection, aggregate) => projection.Failure is not null
                ? projection
                : Apply(projection, aggregate, members, plan.RoleField, use));
    }

    // The target starts as the first member's context, so a field the members agree on is already in place.
    private static RowContext Settle(RowContext target, string field, IReadOnlyList<RowContext> members, FieldUse use) =>
        (MemberAgreement.Agree(members, field, use), use.IsUsed(field)) switch
        {
            (true, _) => target,
            (false, true) => Unknown(target, field, members, "package").Diagnose(MemberAgreement.Disagree, DiagnosticSeverity.Warning, "package", field, null),
            (false, false) => Unknown(target, field, members, "package"),
        };

    private static PackageProjection Apply(PackageProjection projection, FieldAggregate aggregate, IReadOnlyList<ConsolidatedRow> members, string roleField, FieldUse use) =>
        aggregate.Kind switch
        {
            AggregateKind.Same => Same(projection, aggregate.Field, members, use),
            AggregateKind.Sum => Sum(projection, aggregate.Field, members),
            AggregateKind.FromRole => FromRole(projection, aggregate.Field, aggregate.Role, members, roleField, use),
            AggregateKind.DifferenceByRole => Difference(projection, aggregate.Field, aggregate.Role, aggregate.OtherRole, members, roleField, use),
            _ => projection with { Failure = "PackageInconsistent" },
        };

    // Members must agree by value at the field's scale (100 and 100.00 do), else the package is inconsistent. The copy keeps its identity.
    private static PackageProjection Same(PackageProjection projection, string field, IReadOnlyList<ConsolidatedRow> members, FieldUse use) =>
        MemberAgreement.Agree([.. members.Select(member => member.Context)], field, use)
            ? projection with { Context = MemberAgreement.Copy(projection.Context, field, members[0].Context, "package.same") }
            : projection with { Failure = "PackageInconsistent" };

    // An unknown member amount makes the total unknown: null is never zero (§7.5). A total past the decimal range is
    // unknown too, with AggregateOverflow naming the field (#224).
    private static PackageProjection Sum(PackageProjection projection, string field, IReadOnlyList<ConsolidatedRow> members) =>
        members.Any(member => member.Context.IsNull(field))
            ? projection with { Context = projection.Context.Write(field, CanonicalCell.Null(FieldType.Decimal), "package.sum", null, null) }
            : projection with { Context = WriteChecked(projection.Context, field, CheckedDecimal.Sum(members.Select(member => member.Context.Values[field].Number)), "package.sum") };

    // The member with the role supplies the value. Several members with the role must agree, else the field is unknown.
    private static PackageProjection FromRole(PackageProjection projection, string field, string? role, IReadOnlyList<ConsolidatedRow> members, string roleField, FieldUse use)
    {
        RowContext[] holders = [.. members.Where(member => member.Context.Display(roleField) == role).Select(member => member.Context)];
        return holders switch
        {
            [] => projection with { Failure = "IncompletePackage" },
            _ when MemberAgreement.Agree(holders, field, use) => projection with { Context = MemberAgreement.Copy(projection.Context, field, holders[0], "package.fromRole") },
            _ => projection with
            {
                Context = Unknown(projection.Context, field, holders, "package.fromRole")
                    .Diagnose(MemberAgreement.Disagree, DiagnosticSeverity.Warning, "package", field, null),
            },
        };
    }

    // A missing role fails the package. An unknown leg amount makes the difference unknown (§7.5). Several members with
    // one role must agree, as FromRole requires, else the difference is unknown with MembersDisagree; no holder is chosen
    // by row key (§5.2; #217).
    private static PackageProjection Difference(PackageProjection projection, string field, string? leftRole, string? rightRole, IReadOnlyList<ConsolidatedRow> members, string roleField, FieldUse use)
    {
        RowContext[] pay = [.. members.Where(member => member.Context.Display(roleField) == leftRole).Select(member => member.Context)];
        RowContext[] receive = [.. members.Where(member => member.Context.Display(roleField) == rightRole).Select(member => member.Context)];
        return (pay, receive) switch
        {
            ([], _) or (_, []) => projection with { Failure = "IncompletePackage" },
            _ when MemberAgreement.Agree(pay, field, use) is false || MemberAgreement.Agree(receive, field, use) is false => projection with
            {
                Context = projection.Context.Write(field, CanonicalCell.Null(FieldType.Decimal), "package.difference", null, null)
                    .Diagnose(MemberAgreement.Disagree, DiagnosticSeverity.Warning, "package", field, null),
            },
            ([var left, ..], [var right, ..]) when left.IsNull(field) || right.IsNull(field) =>
                projection with { Context = projection.Context.Write(field, CanonicalCell.Null(FieldType.Decimal), "package.difference", null, null) },
            ([var left, ..], [var right, ..]) => projection with { Context = WriteChecked(projection.Context, field, CheckedDecimal.Subtract(left.Values[field].Number, right.Values[field].Number), "package.difference") },
        };
    }

    // A raw column stays on the item only when every leg holds the same text in it, read as a condition reads it.
    private static Dictionary<string, object?> AgreedRaw(RowContext[] members) =>
        members[0].Raw
            .Where(column => members.All(member => member.Raw.TryGetValue(column.Key, out var value) && string.Equals(RawText(value), RawText(column.Value), StringComparison.Ordinal)))
            .ToDictionary(column => column.Key, column => column.Value, StringComparer.Ordinal);

    private static string? RawText(object? value) => InvariantText.Of(value)?.Trim();

    // A raw column the run uses, such as one a diff condition reads, that the legs hold differently is unknown on the item
    // and named in a MembersDisagree warning, as a canonical field is (§5.2; #220). A canonical field of that name is
    // settled by Settle instead.
    private static IEnumerable<string> DisagreeingRawColumns(IReadOnlyList<RowContext> members, IReadOnlyDictionary<string, object?> agreed, FieldUse use) =>
        members
            .SelectMany(member => member.Raw.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(column => use.IsUsed(column) && agreed.ContainsKey(column) is false && members.All(member => member.Values.ContainsKey(column) is false))
            .Order(StringComparer.Ordinal);

    private static RowContext WriteChecked(RowContext target, string field, decimal? value, string ruleId) =>
        value switch
        {
            { } number => WriteNumber(target, field, number, ruleId),
            null => target.Write(field, CanonicalCell.Null(FieldType.Decimal), ruleId, null, null)
                .Diagnose(CheckedDecimal.Overflow, DiagnosticSeverity.Warning, "package", field, null),
        };

    private static RowContext WriteNumber(RowContext target, string field, decimal value, string ruleId) =>
        target.Write(field, CanonicalCell.FromDecimal(value), ruleId, null, value.ToString(CultureInfo.InvariantCulture));

    // The null is typed by a member that has the field, so a decimal stays a decimal.
    private static RowContext Unknown(RowContext target, string field, IReadOnlyList<RowContext> members, string ruleId) =>
        MemberAgreement.Unknown(target, field, members.FirstOrDefault(member => member.Values.ContainsKey(field)) ?? members[0], ruleId);

    private static IReadOnlyList<ConsolidatedRow> Quarantine(IReadOnlyList<ConsolidatedRow> members, string code) =>
        [.. members.Select(member => member with
        {
            Quarantined = true,
            Code = code,
            Context = member.Context.Diagnose(code, DiagnosticSeverity.Invalid, "package", null, null),
        })];

    private sealed record PackageProjection(RowContext Context, string? Failure);
}
