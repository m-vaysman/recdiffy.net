using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy;

internal static class Leftovers
{
    public static IReadOnlyList<RecRow> Of(IReconciliationResult result, ProfileDocument profile, string side)
    {
        var open = result.Entries.Where(entry => entry.Alignment is not Alignment.Matched).Select(entry => entry.Id).ToImmutableHashSet();
        var columns = Columns(profile, side);
        return
        [
            .. result.Rows
                .Where(row => string.Equals(row.Row.Source, side, StringComparison.Ordinal))
                .Where(row => row.Disposition is Disposition.Quarantined || (row.EntryId is { } id && open.Contains(id)))
                .OrderBy(row => row.Row.Ordinal)
                .Select(row => Rebuild(result, row.Row, columns)),
        ];
    }

    private static RecRow Rebuild(IReconciliationResult result, SourceRef row, ImmutableArray<string> columns) =>
        new(columns
            .Select(column => (Column: column, Text: result.Raw(row, column)))
            .Where(cell => cell.Text is not null)
            .ToImmutableSortedDictionary(cell => cell.Column, cell => cell.Text, StringComparer.Ordinal));

    private static ImmutableArray<string> Columns(ProfileDocument profile, string side)
    {
        var rules = profile.Normalization.TryGetValue(side, out var normalization)
            ? [.. normalization.Identify, .. normalization.Parse, .. normalization.Derive, .. normalization.Validate]
            : ImmutableArray<RuleCall>.Empty;
        var named = rules.SelectMany(rule => rule.Strings.Values.Concat(rule.When is { } when ? [when.Field] : []));
        var key = profile.SourceKeys.TryGetValue(side, out var rowKey) ? [rowKey] : Array.Empty<string>();
        return [.. named.Concat(key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
