using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Compares a result with an oracle, one acceptance criterion of spec v3 §12.3 at a time. Each method returns every
// disagreement as one line, so a failing test says which cases disagree and how, not only that some did.
internal sealed class OracleComparison
{
    private readonly ReconciliationResult _result;
    private readonly IReadOnlyList<OracleCase> _cases;
    private readonly Dictionary<(string Source, string Key), RowResult> _rows;
    private readonly ILookup<GroupId, (string Source, string Key)> _members;
    private readonly Dictionary<GroupId, ResultEntry> _entries;

    private readonly HashSet<(string Source, string Key)> _deferredRows;

    // A deferred case is left out of every check, and so is an entry made only of a deferred case's rows. The test class
    // that defers a case says why, and a skipped test of its own checks the case once the cause is fixed.
    public OracleComparison(ReconciliationResult result, IReadOnlyList<OracleCase> cases, Func<OracleCase, string?>? deferred = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(cases);
        var defer = deferred ?? (_ => null);
        _result = result;
        _cases = [.. cases.Where(item => defer(item) is null)];
        _deferredRows = [.. cases.Where(item => defer(item) is not null).SelectMany(Rows)];
        _rows = result.Rows.ToDictionary(row => (row.Row.Source, row.Row.RowKey));
        _members = result.Rows.Where(row => row.EntryId is not null).ToLookup(row => row.EntryId!.Value, row => (row.Row.Source, row.Row.RowKey));
        _entries = result.Entries.ToDictionary(entry => entry.Id);
    }

    public int Checked => _cases.Count;

    // AC-5: every input row has exactly one row result, and nothing else has one.
    public IReadOnlyList<string> RowAccounting(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var expected = left.Select(key => ("left", key)).Concat(right.Select(key => ("right", key))).ToArray();
        var actual = _result.Rows.Select(row => (row.Row.Source, row.Row.RowKey)).ToArray();
        return
        [
            .. actual.GroupBy(row => row).Where(group => group.Count() > 1).Select(group => $"{group.Key}: {group.Count()} row results"),
            .. expected.Except(actual).Select(row => $"{row}: no row result"),
            .. actual.Except(expected).Select(row => $"{row}: a row result for a row the input doesn't have"),
        ];
    }

    // AC-1: each case is one entry, the rows linked to it are exactly the case's rows, and there are no other entries.
    public IReadOnlyList<string> Membership() =>
    [
        .. _cases.SelectMany(MembershipOf),
        .. _entries.Keys.Except(_cases.Select(Entry).OfType<GroupId>())
            .Where(id => _members[id].All(_deferredRows.Contains) is false)
            .Select(id => $"entry {Describe(_entries[id])} is in no case"),
    ];

    private IEnumerable<string> MembershipOf(OracleCase item)
    {
        var rows = Rows(item).ToArray();
        var missing = rows.Where(row => _rows.ContainsKey(row) is false).Select(row => $"{item.Id}: {row} has no row result").ToArray();
        var entries = rows.Where(_rows.ContainsKey).Select(row => _rows[row].EntryId).Distinct().ToArray();
        return (missing, entries) switch
        {
            ({ Length: > 0 }, _) => missing,
            (_, [null]) => [$"{item.Id}: no entry, dispositions {Dispositions(rows)}"],
            (_, [{ } id]) when _members[id].ToHashSet().SetEquals(rows) => [],
            (_, [{ } id]) => [$"{item.Id}: entry holds [{string.Join(", ", _members[id].Order())}], oracle [{string.Join(", ", rows.Order())}]"],
            _ => [$"{item.Id}: split over {entries.Length} entries: {Dispositions(rows)}"],
        };
    }

    // AC-2: alignment and outcome per §6.5, for each case whose rows form one entry.
    public IReadOnlyList<string> Outcomes() => CompareAxes(false, axes => axes with { Composition = Composition.None });

    // AC-2: composition per §6.5, for each case whose rows form one entry.
    public IReadOnlyList<string> Compositions(bool allocationsArePackages) =>
        CompareAxes(allocationsArePackages, axes => axes with { Alignment = Alignment.Matched, Outcome = FieldOutcome.NotCompared });

    // The three axes together, as §6.5 states them.
    public IReadOnlyList<string> AllAxes(bool allocationsArePackages) => CompareAxes(allocationsArePackages, axes => axes);

    private IReadOnlyList<string> CompareAxes(bool allocationsArePackages, Func<Axes, Axes> compared) =>
    [
        .. _cases.Select(item => (Case: item, Entry: EntryOf(item)))
            .Where(pair => pair.Entry is not null)
            .Select(pair => (pair.Case, Expected: OracleLabels.Expected(pair.Case.Kind, allocationsArePackages), Actual: new Axes(pair.Entry!.Alignment, pair.Entry.Composition, pair.Entry.Outcome)))
            .Where(triple => compared(triple.Expected) != compared(triple.Actual))
            .Select(triple => $"{triple.Case.Id} ({triple.Case.Label}): expected {Show(triple.Expected)}, got {Show(triple.Actual)}{Breaks(EntryOf(triple.Case)!)}"),
    ];

    // AC-3: a break's unattributed break fields are exactly the fields the oracle blames.
    public IReadOnlyList<string> BreakRoots(Func<string, string?> field) =>
    [
        .. _cases.Where(item => item.BreakFields.Count > 0)
            .Select(item => (Case: item, Entry: EntryOf(item)))
            .Where(pair => pair.Entry is not null)
            .Select(pair => (pair.Case, Expected: pair.Case.BreakFields.Select(name => field(name) ?? $"?{name}").Order(StringComparer.Ordinal).ToArray(), Actual: Roots(pair.Entry!)))
            .Where(triple => triple.Expected.SequenceEqual(triple.Actual, StringComparer.Ordinal) is false)
            .Select(triple => $"{triple.Case.Id}: oracle blames [{string.Join(", ", triple.Expected)}], engine roots [{string.Join(", ", triple.Actual)}]{Breaks(EntryOf(triple.Case)!)}"),
    ];

    // AC-3: every numeric difference the oracle states is a field difference with that delta (right minus left).
    public IReadOnlyList<string> Deltas(Func<string, string?> field) =>
    [
        .. _cases.Where(item => item.Deltas.Count > 0)
            .Select(item => (Case: item, Entry: EntryOf(item)))
            .Where(pair => pair.Entry is not null)
            .SelectMany(pair => pair.Case.Deltas.Select(delta => DeltaOf(pair.Case, pair.Entry!, delta, field(delta.Field))))
            .OfType<string>(),
    ];

    private static string? DeltaOf(OracleCase item, ResultEntry entry, OracleDelta delta, string? field) =>
        (field, entry.Diffs.Where(diff => diff.Field.Name == field).ToArray()) switch
        {
            (null, _) => $"{item.Id}: oracle field {delta.Field} has no profile field",
            (_, []) => $"{item.Id}: {field} has no difference, oracle delta {delta.Delta}",
            (_, [var diff]) when diff.Delta.Number == delta.Delta => null,
            (_, [var diff]) => $"{item.Id}: {field} delta {diff.Delta.Number} ({diff.Outcome}), oracle {delta.Delta}",
            _ => $"{item.Id}: {field} has more than one difference",
        };

    // AC-4: the survivor is the oracle's active report, and the chain's other rows are suppressed.
    public IReadOnlyList<string> Lifecycle() =>
    [
        .. _cases.Where(item => item.Survivor is not null).SelectMany(item =>
            item.Right.Select(key => (Key: key, Expected: key == item.Survivor ? Disposition.Matched : Disposition.Suppressed, Actual: _rows.GetValueOrDefault(("right", key))?.Disposition))
                .Where(row => row.Actual != row.Expected)
                .Select(row => $"{item.Id}: {row.Key} expected {row.Expected}, got {row.Actual?.ToString() ?? "no row result"}")),
    ];

    private ResultEntry? EntryOf(OracleCase item) => Entry(item) is { } id ? _entries.GetValueOrDefault(id) : null;

    // The case's entry when all its rows link to one entry; otherwise none, and Membership says why.
    private GroupId? Entry(OracleCase item) =>
        Rows(item).Select(row => _rows.GetValueOrDefault(row)?.EntryId).Distinct().ToArray() is [{ } id] ? id : null;

    private static IEnumerable<(string Source, string Key)> Rows(OracleCase item) =>
        item.Left.Select(key => ("left", key)).Concat(item.Right.Select(key => ("right", key)));

    private static string[] Roots(ResultEntry entry) =>
        [.. entry.Diffs.Where(diff => diff.Outcome is FieldOutcome.Break && diff.AttributedTo.IsDefaultOrEmpty).Select(diff => diff.Field.Name).Order(StringComparer.Ordinal)];

    private string Dispositions(IEnumerable<(string Source, string Key)> rows) =>
        string.Join(", ", rows.Select(row => $"{row.Key}={_rows.GetValueOrDefault(row)?.Disposition.ToString() ?? "none"}{Codes(row)}"));

    private string Codes((string Source, string Key) row) =>
        _rows.GetValueOrDefault(row)?.Diagnostics is { Count: > 0 } diagnostics ? $"[{string.Join(" ", diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Field}"))}]" : "";

    private static string Describe(ResultEntry entry) =>
        $"{entry.Alignment} [{string.Join(", ", entry.Left.Select(row => row.RowKey).Concat(entry.Right.Select(row => row.RowKey)))}]";

    private static string Show(Axes axes) => $"{axes.Alignment}/{axes.Composition}/{axes.Outcome}";

    private static string Breaks(ResultEntry entry) =>
        entry.Diffs.Where(diff => diff.Outcome is not FieldOutcome.Exact) is var diffs && diffs.Any()
            ? $" diffs {string.Join(" ", diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number}{Attributed(diff.AttributedTo)}"))}"
            : "";

    private static string Attributed(ImmutableArray<string> roots) => roots.IsDefaultOrEmpty ? "" : $"<-{string.Join("+", roots)}";
}
