using RecDiffy.Rules;

namespace RecDiffy;

internal static class Packages
{
    public static IReadOnlyList<ConsolidatedRow> Pack(IReadOnlyList<ConsolidatedRow> rows, PackagePlan plan)
    {
        var active = rows.Where(row => row is { Quarantined: false, Suppression: null }).ToArray();
        var linked = active.Where(row => row.Context.Display(plan.LinkField) is not null).ToArray();
        var groups = linked.GroupBy(row => row.Context.Display(plan.LinkField)!, StringComparer.Ordinal);
        var packed = groups.SelectMany(group => Build(group.ToArray(), plan)).ToArray();
        var consumed = linked.Select(row => row.Context.RowKey).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. rows.Where(row => consumed.Contains(row.Context.RowKey) is false),
            .. packed,
        ];
    }

    private static IReadOnlyList<ConsolidatedRow> Build(IReadOnlyList<ConsolidatedRow> members, PackagePlan plan) =>
        plan.RequiredRoles.Where(role => members.Any(member => member.Context.Display(plan.RoleField) == role) is false).ToArray() switch
        {
            { Length: > 0 } => Quarantine(members, "IncompletePackage"),
            _ => Aggregate(members, plan) switch
            {
                { Failure: { } code } => Quarantine(members, code),
                { Context: { } context } => [new ConsolidatedRow([.. members.SelectMany(member => member.Members)], context, false, null, null, true)],
            },
        };

    private static PackageProjection Aggregate(IReadOnlyList<ConsolidatedRow> members, PackagePlan plan) =>
        plan.Aggregates.Aggregate(
            new PackageProjection(members[0].Context, null),
            (projection, aggregate) => projection.Failure is not null
                ? projection
                : Apply(projection, aggregate, members, plan.RoleField));

    private static PackageProjection Apply(PackageProjection projection, FieldAggregate aggregate, IReadOnlyList<ConsolidatedRow> members, string roleField) =>
        aggregate.Kind switch
        {
            AggregateKind.Same => Same(projection, aggregate.Field, members),
            AggregateKind.Sum => Sum(projection, aggregate.Field, members),
            AggregateKind.FromRole => FromRole(projection, aggregate.Field, aggregate.Role, members, roleField),
            AggregateKind.DifferenceByRole => Difference(projection, aggregate.Field, aggregate.Role, aggregate.OtherRole, members, roleField),
            _ => projection with { Failure = "PackageInconsistent" },
        };

    private static PackageProjection Same(PackageProjection projection, string field, IReadOnlyList<ConsolidatedRow> members) =>
        members.Select(member => member.Context.Display(field)).Distinct(StringComparer.Ordinal).Count() <= 1
            ? projection with { Context = Copy(projection.Context, field, members[0].Context) }
            : projection with { Failure = "PackageInconsistent" };

    private static PackageProjection Sum(PackageProjection projection, string field, IReadOnlyList<ConsolidatedRow> members)
    {
        var total = members.Sum(member => member.Context.Values.TryGetValue(field, out var cell) && !cell.IsNull ? cell.Number : 0m);
        return projection with { Context = projection.Context.Write(field, CanonicalCell.FromDecimal(total), "package.sum", null, total.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
    }

    private static PackageProjection FromRole(PackageProjection projection, string field, string? role, IReadOnlyList<ConsolidatedRow> members, string roleField) =>
        members.FirstOrDefault(member => member.Context.Display(roleField) == role) is { } source
            ? projection with { Context = Copy(projection.Context, field, source.Context) }
            : projection with { Failure = "IncompletePackage" };

    private static PackageProjection Difference(PackageProjection projection, string field, string? leftRole, string? rightRole, IReadOnlyList<ConsolidatedRow> members, string roleField)
    {
        var left = members.FirstOrDefault(member => member.Context.Display(roleField) == leftRole);
        var right = members.FirstOrDefault(member => member.Context.Display(roleField) == rightRole);
        return (left, right) switch
        {
            (null, _) or (_, null) => projection with { Failure = "IncompletePackage" },
            ({ } pay, { } receive) => WriteDifference(projection, field, Number(pay, field) - Number(receive, field)),
        };
    }

    private static PackageProjection WriteDifference(PackageProjection projection, string field, decimal value) =>
        projection with
        {
            Context = projection.Context.Write(field, CanonicalCell.FromDecimal(value), "package.difference", null, value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };

    private static decimal Number(ConsolidatedRow row, string field) =>
        row.Context.Values.TryGetValue(field, out var cell) && !cell.IsNull ? cell.Number : 0m;

    private static RowContext Copy(RowContext target, string field, RowContext source) =>
        source.Values.TryGetValue(field, out var cell) && !cell.IsNull
            ? cell.Type switch
            {
                FieldType.Decimal => target.Write(field, cell, "package.same", null, source.Display(field)),
                FieldType.Date => target.Write(field, cell, "package.same", null, source.Display(field)),
                FieldType.Symbol => InternSymbol(target, field, source.Display(field)!),
                FieldType.Identifier => InternIdentifier(target, field, source.Display(field)!),
                FieldType.Text => target.Write(field, CanonicalCell.FromText(source.Display(field)), "package.same", null, source.Display(field)),
                _ => target,
            }
            : target.Write(field, CanonicalCell.Null(source.ContextType(field)), "package.same", null, null);

    private static FieldType ContextType(this RowContext row, string field) =>
        row.Values.TryGetValue(field, out var cell) ? cell.Type : FieldType.Text;

    private static RowContext InternSymbol(RowContext target, string field, string text)
    {
        var (pool, id) = target.Pool.InternSymbol(text);
        return target.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), "package.same", null, text);
    }

    private static RowContext InternIdentifier(RowContext target, string field, string text)
    {
        var (pool, id) = target.Pool.InternIdentifier("PACKAGE", "", text);
        return target.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Identifier, id), "package.same", null, text);
    }

    private static IReadOnlyList<ConsolidatedRow> Quarantine(IReadOnlyList<ConsolidatedRow> members, string code) =>
        [.. members.Select(member => member with
        {
            Quarantined = true,
            Code = code,
            Context = member.Context.Diagnose(code, DiagnosticSeverity.Invalid, "package", null, null),
        })];

    private sealed record PackageProjection(RowContext Context, string? Failure);
}
