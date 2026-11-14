using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #43: every test here starts with a live token and cancels it from a call the work itself makes, so the
// cancellation lands partway through. No test waits on a timer.
public class MidWorkCancellationTests
{
    private static readonly FieldDeclaration[] Fields =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
        new("TradeRef", FieldType.Identifier, FieldRole.Identity, 0),
    ];

    // Four many-side rows, then two targets, all in one block. The targets carry no Quantity, so no subset
    // search runs: its own entry check cannot stand in for a missing check in component construction.
    private static readonly ConsolidatedRow[] Block =
    [
        Row("L0", Side.Left, 1m, "REF-A"),
        Row("L1", Side.Left, 2m, "REF-A"),
        Row("L2", Side.Left, 3m, null),
        Row("L3", Side.Left, 4m, "REF-B"),
        Row("R0", Side.Right, null, "REF-A"),
        Row("R1", Side.Right, null, "REF-B"),
    ];

    // A component reads the shared-reference field once for each row it claims: the census reads the four
    // many-side rows, then the merge reads each target. Claim 1 is inside the census, claim 5 inside the merge.
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Component_construction_stops_when_the_run_is_cancelled_partway(int cancelAtClaim)
    {
        var finished = Claims();
        using var source = new CancellationTokenSource();
        var schema = new CancelOnClaim(Schema(), "TradeRef", cancelAtClaim, source);

        Assert.Throws<OperationCanceledException>(() => Build(Workspace(schema, source.Token)));
        Assert.True(source.IsCancellationRequested);
        Assert.InRange(schema.Claims, cancelAtClaim, finished - 1);
    }

    [Fact]
    public void Component_construction_reads_the_shared_reference_while_it_builds()
    {
        var claims = Claims();

        Assert.True(claims > 5, $"Construction made {claims.ToString(CultureInfo.InvariantCulture)} claims; the theory above cancels at claim 5.");
    }

    // One negative class turns off the suffix bound (spec v3 §4.7), so the walk visits node after node until its
    // budget runs out. It reads a class each time it branches.
    [Fact]
    public void A_subset_search_runs_long_enough_to_be_cancelled_partway()
    {
        var classes = new CancelOnRead(Unreachable(), int.MaxValue, null);

        var search = SubsetSearch.Search(classes, [1_000m], 20, 100_000, CancellationToken.None);

        Assert.Equal(SearchState.Incomplete, search.State);
        Assert.True(classes.Reads > 10_000, $"The walk read {classes.Reads.ToString(CultureInfo.InvariantCulture)} classes.");
    }

    [Fact]
    public void A_subset_search_stops_when_the_run_is_cancelled_partway_through_the_walk()
    {
        using var source = new CancellationTokenSource();
        var classes = new CancelOnRead(Unreachable(), 1_000, source);

        Assert.Throws<OperationCanceledException>(() => SubsetSearch.Search(classes, [1_000m], 20, 100_000, source.Token));
        Assert.InRange(classes.Reads, 1_000, 2_000);
    }

    // The rule cancels while intake normalizes row L2. The run is past its first check by then, and the rows
    // would go on to a group-sum pass if the run kept going.
    [Fact]
    public void A_run_cancelled_while_it_normalizes_rows_returns_no_result()
    {
        using var source = new CancellationTokenSource();
        var profile = Compile(source);

        Assert.Throws<OperationCanceledException>(() =>
            Reconciler.Run(profile, Source("left", "L"), Source("right", "R"), ReferenceDataSet.Empty, cancellationToken: source.Token));
        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public async Task An_async_run_cancelled_while_it_normalizes_rows_returns_no_result()
    {
        using var source = new CancellationTokenSource();
        var profile = Compile(source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reconciler.RunAsync(profile, Source("left", "L"), Source("right", "R"), ReferenceDataSet.Empty, cancellationToken: source.Token));
        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public void The_same_run_completes_when_no_row_cancels_it()
    {
        using var source = new CancellationTokenSource();
        var profile = Compile(source);

        var result = Reconciler.Run(profile, Source("left", "X"), Source("right", "Y"), ReferenceDataSet.Empty, cancellationToken: source.Token);

        Assert.False(source.IsCancellationRequested);
        Assert.Equal(4, result.Rows.Length);
        Assert.Contains(result.Entries, entry => entry.Alignment == Alignment.Matched);
    }

    private static int Claims()
    {
        var schema = new CancelOnClaim(Schema(), "TradeRef", int.MaxValue, null);
        _ = Build(Workspace(schema, CancellationToken.None));
        return schema.Claims;
    }

    private static (PassIndex Index, MatchCounters Counters) Build(MatchWorkspace workspace) =>
        new GroupPass("G1", ["Counterparty"], ["Quantity"], Side.Left, 8).Build(workspace, new MatchCounters());

    private static MatchWorkspace Workspace(IReadOnlyDictionary<string, FieldDeclaration> schema, CancellationToken token)
    {
        ActiveItem[] items = [.. Block.Select((row, index) => new ActiveItem(index, row.Context.Side, row))];
        return new MatchWorkspace("G1", items, items.Select(item => item.Index).ToImmutableHashSet(), schema, SearchLimits.Default, "TradeRef", SearchLimits.Default.MaxGroupSize, token);
    }

    private static Dictionary<string, FieldDeclaration> Schema() =>
        Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);

    private static ConservedClass[] Unreachable() =>
        [.. Enumerable.Range(1, 19).Reverse().Select(value => new ConservedClass([value], [value])), new ConservedClass([-1m], [0])];

    private static ConsolidatedRow Row(string id, Side side, decimal? quantity, string? tradeRef)
    {
        var start = RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (symbols, party) = start.Pool.InternSymbol("JPM");
        var withParty = start.WithPool(symbols).Write("Counterparty", CanonicalCell.FromInterned(FieldType.Symbol, party), "test", null, "JPM");
        var withQuantity = quantity is { } number
            ? withParty.Write("Quantity", CanonicalCell.FromDecimal(number), "test", null, null)
            : withParty;
        var context = tradeRef is null ? withQuantity : WithReference(withQuantity, tradeRef);
        return new ConsolidatedRow([new SourceRef(side is Side.Left ? "left" : "right", id, 0)], context, false, null, null);
    }

    private static RowContext WithReference(RowContext row, string tradeRef)
    {
        var (pool, id) = row.Pool.InternIdentifier("shared", "", tradeRef);
        return row.WithPool(pool).Write("TradeRef", CanonicalCell.FromInterned(FieldType.Identifier, id), "test", null, tradeRef);
    }

    private static CompiledProfile Compile(CancellationTokenSource source)
    {
        var registry = RuleRegistry.CreateCore()
            .Register("test.cancelOnRow", RulePhase.Validate, ["row"], (call, _, _) => new CancelOnRow(call.Strings["row"], source));
        return ProfileCompiler.Compile(ProfileDocument.Parse(CancellingProfile), registry, CultureInfo.InvariantCulture).Profile
            ?? throw new InvalidOperationException("The profile did not compile.");
    }

    // Left L1 + L2 sum to right R1, so a run that keeps going reaches the group-sum pass.
    private static RecordSource Source(string side, string prefix)
    {
        var rows = side is "left"
            ? new[] { ($"{prefix}1", "2"), ($"{prefix}2", "3"), ($"{prefix}3", "7") }
            : [($"{prefix}1", "5")];
        var map = new SourceMap<(string Id, string Qty)>()
            .Text("Id", row => row.Id)
            .Text("Book", _ => "B1")
            .Text("Qty", row => row.Qty);
        return RecordSources.FromObjects(side, rows, map);
    }

    private const string CancellingProfile = """
        {
          "formatVersion": 1,
          "profileId": "cancel-mid-run",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Book", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ],
              "validate": [ { "rule": "test.cancelOnRow", "row": "L2" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.groupSum", "block": [ "Book" ], "conserve": [ "Quantity" ], "many": "left" }
          ] } ]
        }
        """;

    // Counts lookups of one field and cancels on the chosen one. Only a component's claim looks the field up
    // with TryGetValue; block keys use the indexer.
    private sealed class CancelOnClaim(
        IReadOnlyDictionary<string, FieldDeclaration> inner,
        string field,
        int cancelAt,
        CancellationTokenSource? source) : IReadOnlyDictionary<string, FieldDeclaration>
    {
        public int Claims { get; private set; }

        public FieldDeclaration this[string key] => inner[key];

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<FieldDeclaration> Values => inner.Values;

        public int Count => inner.Count;

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out FieldDeclaration value)
        {
            if (string.Equals(key, field, StringComparison.Ordinal))
            {
                Claims++;
                if (Claims == cancelAt)
                    source?.Cancel();
            }

            return inner.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, FieldDeclaration>> GetEnumerator() => inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Counts reads through the indexer and cancels on the chosen one.
    private sealed class CancelOnRead(IReadOnlyList<ConservedClass> inner, int cancelAt, CancellationTokenSource? source) : IReadOnlyList<ConservedClass>
    {
        public int Reads { get; private set; }

        public ConservedClass this[int index]
        {
            get
            {
                Reads++;
                if (Reads == cancelAt)
                    source?.Cancel();
                return inner[index];
            }
        }

        public int Count => inner.Count;

        public IEnumerator<ConservedClass> GetEnumerator() => inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CancelOnRow(string rowKey, CancellationTokenSource source) : INormalizationRule
    {
        public string RuleId => "test.cancelOnRow";

        public RulePhase Phase => RulePhase.Validate;

        public IReadOnlyList<FieldId> Reads { get; } = [];

        public IReadOnlyList<FieldId> Writes { get; } = [];

        public void Apply(ref RowContext row)
        {
            if (string.Equals(row.RowKey, rowKey, StringComparison.Ordinal))
                source.Cancel();
        }
    }
}
