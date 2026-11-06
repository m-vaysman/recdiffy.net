using System.Diagnostics;
using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

public class SeededFuzzTests
{
    private static readonly int[] SeedValues = [1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 310, 2026];

    private static readonly Disposition[] ExpectedDispositions =
    [
        Disposition.Matched,
        Disposition.Ambiguous,
        Disposition.LeftOnly,
        Disposition.RightOnly,
        Disposition.Quarantined,
        Disposition.Suppressed,
        Disposition.SearchIncomplete,
    ];

    private static readonly CompiledProfile StockProfile = Reconciler.Compile(ProfileDocument.Parse(Stock), RuleRegistry.CreateCore());

    private static readonly CompiledProfile ScaleProfile = Reconciler.Compile(ProfileDocument.Parse(Scale), RuleRegistry.CreateCore());

    private static readonly SourceMap<StockRow> StockMap = new SourceMap<StockRow>()
        .Text("Id", row => row.Id)
        .Text("Sku", row => row.Sku)
        .Text("Quantity", row => row.Quantity)
        .Text("Action", row => row.Action)
        .Text("Target", row => row.Target);

    private static readonly SourceMap<Dictionary<string, string>> ScaleMap = new SourceMap<Dictionary<string, string>>()
        .Text("Id", row => row["Id"])
        .Text("Instrument", row => row["Instrument"])
        .Text("Quantity", row => row["Quantity"]);

    public static IEnumerable<object[]> Seeds() => SeedValues.Select(seed => new object[] { seed });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Places_every_record_exactly_once(int seed)
    {
        var population = Population.Generate(seed);
        var result = population.Run(StockProfile);
        var expected = population.Keys();
        var actual = result.Rows
            .Select(row => (row.Row.Source, row.Row.RowKey))
            .OrderBy(row => row.Source, StringComparer.Ordinal)
            .ThenBy(row => row.RowKey, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);

        var shared = result.Entries
            .SelectMany(entry => entry.Left.Concat(entry.Right)
                .Select(member => (member.Source, member.RowKey))
                .Distinct()
                .Select(key => (Key: key, entry.Id)))
            .GroupBy(pair => pair.Key)
            .Where(group => group.Select(pair => pair.Id).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(shared);
        Assert.Equal(result.Entries.Count, result.Entries.Select(entry => entry.Id).Distinct().Count());
        Assert.Empty(result.Entries
            .SelectMany(entry => entry.Left.Concat(entry.Right))
            .Select(member => (member.Source, member.RowKey))
            .Where(key => !expected.Contains(key))
            .Distinct());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Repeats_the_same_answer(int seed)
    {
        var population = Population.Generate(seed);
        var first = Answer(population.Run(StockProfile));
        var second = Answer(population.Run(StockProfile));
        var shuffled = Answer(population.Shuffled(seed).Run(StockProfile));

        Assert.Equal(first, second);
        Assert.Equal(first, shuffled);
    }

    [Fact]
    public void Reaches_every_disposition_the_population_can_produce()
    {
        var results = SeedValues.Select(seed => Population.Generate(seed).Run(StockProfile)).ToArray();
        var seen = results.SelectMany(result => result.Rows).Select(row => row.Disposition).ToHashSet();

        Assert.Equal(ExpectedDispositions.OrderBy(item => item), seen.OrderBy(item => item));
        Assert.Contains(results.SelectMany(result => result.Rows), row => row.Validity == InputValidity.Invalid);
    }

    [Fact]
    public void One_to_one_matching_scales_linearly()
    {
        const int smallRows = 4_000;
        const int largeRows = 16_000;
        const int samples = 5;

        _ = Time(smallRows);
        _ = Time(largeRows);
        var small = Best(smallRows, samples);
        var large = Best(largeRows, samples);
        var ratio = (double)large / small;

        Assert.True(
            ratio < 8,
            $"16,000 rows took {TimeSpan.FromTicks(large).TotalMilliseconds:0} ms vs {TimeSpan.FromTicks(small).TotalMilliseconds:0} ms for 4,000 ({ratio:0.00}x). Linear growth is about 4x.");
    }

    private static long Best(int rows, int samples)
    {
        var best = long.MaxValue;
        for (var sample = 0; sample < samples; sample++)
        {
            var ticks = Time(rows);
            if (ticks < best)
                best = ticks;
        }

        return best;
    }

    private static long Time(int rows)
    {
        var left = ScaleRows(rows, "L");
        var right = ScaleRows(rows, "R");
        var watch = Stopwatch.StartNew();
        var result = Reconciler.Run(
            ScaleProfile,
            RecordSources.FromObjects("book", left, ScaleMap),
            RecordSources.FromObjects("book", right, ScaleMap),
            ReferenceDataSet.Empty);
        watch.Stop();
        Assert.Equal(rows, result.Summary.Matched);
        Assert.Equal(rows * 2, result.Rows.Count);
        return watch.ElapsedTicks;
    }

    private static Dictionary<string, string>[] ScaleRows(int rows, string prefix) =>
    [
        .. Enumerable.Range(0, rows).Select(index => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = prefix + index.ToString(CultureInfo.InvariantCulture),
            ["Instrument"] = "I" + index.ToString(CultureInfo.InvariantCulture),
            ["Quantity"] = "1",
        }),
    ];

    private static string Answer(ReconciliationResult result)
    {
        var rows = result.Rows
            .Select(row => string.Join('\u001f', [row.Row.Source, row.Row.RowKey, row.Disposition.ToString(), row.Validity.ToString(), row.EntryId?.ToString() ?? ""]))
            .OrderBy(line => line, StringComparer.Ordinal);
        var entries = result.Entries
            .Select(entry => string.Join('\u001f', [entry.Id.ToString(), entry.PathId, entry.Alignment.ToString(), entry.Composition.ToString(), entry.Outcome.ToString(), Members(entry.Left), Members(entry.Right), Diffs(entry)]))
            .OrderBy(line => line, StringComparer.Ordinal);
        return string.Join('\n', rows) + "\n--\n" + string.Join('\n', entries) + "\n--\n" + result.Summary;
    }

    private static string Members(IReadOnlyList<SourceRef> members) =>
        string.Join(",", members.Select(member => member.Source + ":" + member.RowKey).OrderBy(text => text, StringComparer.Ordinal));

    private static string Diffs(ResultEntry entry) =>
        string.Join(
            ",",
            entry.Diffs
                .Select(diff => diff.Field.Name + ":" + diff.Outcome + ":" + diff.Delta.Kind + ":" + diff.Delta.Number.ToString(CultureInfo.InvariantCulture) + ":" + diff.Delta.Days.ToString(CultureInfo.InvariantCulture))
                .OrderBy(text => text, StringComparer.Ordinal));

    private enum Mode
    {
        Pair,
        Allocation,
        LeftOnly,
        RightOnly,
        Ambiguous,
        Invalid,
        DuplicateConflict,
        IdenticalDuplicate,
        Lifecycle,
        Crowd,
    }

    private static readonly Mode[] Modes = Enum.GetValues<Mode>();

    private readonly record struct StockRow(string Id, string Sku, string Quantity, string? Action = null, string? Target = null);

    private sealed record Draft(int LeftId, int RightId, StockRow[] Left, StockRow[] Right)
    {
        public Draft Append(Mode mode, string sku, Random random)
        {
            var emitted = Emit(LeftId, RightId, sku, mode, random);
            return new Draft(emitted.LeftId, emitted.RightId, [.. Left, .. emitted.Left], [.. Right, .. emitted.Right]);
        }
    }

    private readonly record struct Emitted(int LeftId, int RightId, StockRow[] Left, StockRow[] Right);

    private sealed record Population(StockRow[] Left, StockRow[] Right)
    {
        public static Population Generate(int seed)
        {
            var random = new Random(seed);
            var plan = Modes
                .Concat(Enumerable.Range(0, random.Next(0, 5)).Select(_ => Modes[random.Next(Modes.Length)]))
                .Select((mode, index) => (Mode: mode, Sku: "S" + index.ToString(CultureInfo.InvariantCulture)))
                .ToArray();
            var draft = plan.Aggregate(new Draft(0, 0, [], []), (current, group) => current.Append(group.Mode, group.Sku, random));
            return new Population(draft.Left, draft.Right);
        }

        public Population Shuffled(int seed) => new(Shuffle(Left, seed), Shuffle(Right, seed + 1));

        public ReconciliationResult Run(CompiledProfile profile) =>
            Reconciler.Run(
                profile,
                RecordSources.FromObjects("left", Left, StockMap),
                RecordSources.FromObjects("right", Right, StockMap),
                ReferenceDataSet.Empty);

        public (string Source, string RowKey)[] Keys() =>
        [
            .. Left.Select(row => ("left", row.Id)).Concat(Right.Select(row => ("right", row.Id)))
                .OrderBy(row => row.Item1, StringComparer.Ordinal)
                .ThenBy(row => row.Item2, StringComparer.Ordinal),
        ];
    }

    private static Emitted Emit(int leftId, int rightId, string sku, Mode mode, Random random) =>
        mode switch
        {
            Mode.Pair => Pair(leftId, rightId, sku, random),
            Mode.Allocation => Allocation(leftId, rightId, sku, random),
            Mode.LeftOnly => new Emitted(leftId + 1, rightId, [Row(leftId, "L", sku, Qty(random))], []),
            Mode.RightOnly => new Emitted(leftId, rightId + 1, [], [Row(rightId, "R", sku, Qty(random))]),
            Mode.Ambiguous => Ambiguous(leftId, rightId, sku, random),
            Mode.Invalid => Invalid(leftId, rightId, sku, random),
            Mode.DuplicateConflict => Conflict(leftId, rightId, sku),
            Mode.IdenticalDuplicate => Identical(leftId, rightId, sku, random),
            Mode.Lifecycle => Lifecycle(leftId, rightId, sku, random),
            Mode.Crowd => Crowd(leftId, rightId, sku),
            _ => throw new InvalidOperationException($"Unhandled population mode {mode}."),
        };

    private static Emitted Pair(int leftId, int rightId, string sku, Random random)
    {
        var quantity = Qty(random);
        return new Emitted(leftId + 1, rightId + 1, [Row(leftId, "L", sku, quantity)], [Row(rightId, "R", sku, quantity)]);
    }

    private static Emitted Allocation(int leftId, int rightId, string sku, Random random)
    {
        var parts = Enumerable.Range(0, random.Next(2, 5)).Select(_ => random.Next(1, 20)).ToArray();
        var left = parts.Select((quantity, offset) => Row(leftId + offset, "L", sku, quantity.ToString(CultureInfo.InvariantCulture))).ToArray();
        return new Emitted(leftId + parts.Length, rightId + 1, left, [Row(rightId, "R", sku, parts.Sum().ToString(CultureInfo.InvariantCulture))]);
    }

    private static Emitted Ambiguous(int leftId, int rightId, string sku, Random random)
    {
        var quantity = Qty(random);
        return new Emitted(
            leftId + 2,
            rightId + 1,
            [Row(leftId, "L", sku, quantity), Row(leftId + 1, "L", sku, quantity)],
            [Row(rightId, "R", sku, quantity)]);
    }

    private static Emitted Invalid(int leftId, int rightId, string sku, Random random)
    {
        var token = random.Next(2) == 0 ? "bad" : "1.2.3";
        var quantity = Qty(random);
        var badOnLeft = random.Next(2) == 0;
        return new Emitted(
            leftId + 1,
            rightId + 1,
            [Row(leftId, "L", sku, badOnLeft ? token : quantity)],
            [Row(rightId, "R", sku, badOnLeft ? quantity : token)]);
    }

    private static Emitted Conflict(int leftId, int rightId, string sku)
    {
        var row = Row(leftId, "L", sku, "3");
        return new Emitted(leftId + 1, rightId, [row, row with { Quantity = "9" }], []);
    }

    private static Emitted Identical(int leftId, int rightId, string sku, Random random)
    {
        var row = Row(leftId, "L", sku, Qty(random));
        return new Emitted(leftId + 1, rightId, [row, row], []);
    }

    // NEW, then CANC of the NEW, then CORR of the CANC on the right: the CORR survives and matches the left row; NEW and CANC are Suppressed.
    private static Emitted Lifecycle(int leftId, int rightId, string sku, Random random)
    {
        var quantity = Qty(random);
        var created = Row(rightId, "R", sku, "500") with { Action = "NEW" };
        var cancelled = Row(rightId + 1, "R", sku, "500") with { Action = "CANC", Target = created.Id };
        var corrected = Row(rightId + 2, "R", sku, quantity) with { Action = "CORR", Target = cancelled.Id };
        return new Emitted(leftId + 1, rightId + 3, [Row(leftId, "L", sku, quantity)], [created, cancelled, corrected]);
    }

    // More group-sum candidates in one block than limits.maxBlockItems allows (6), and no one-to-one match: the block is SearchIncomplete.
    private static Emitted Crowd(int leftId, int rightId, string sku) =>
        new(
            leftId + 7,
            rightId + 1,
            [.. Enumerable.Range(0, 7).Select(offset => Row(leftId + offset, "L", sku, (offset + 1).ToString(CultureInfo.InvariantCulture)))],
            [Row(rightId, "R", sku, "1000")]);

    private static StockRow Row(int id, string side, string sku, string quantity) =>
        new(side + id.ToString(CultureInfo.InvariantCulture), sku, quantity);

    private static string Qty(Random random) => random.Next(1, 100).ToString(CultureInfo.InvariantCulture);

    private static StockRow[] Shuffle(StockRow[] rows, int seed)
    {
        var copy = (StockRow[])rows.Clone();
        var random = new Random(seed);
        for (var index = copy.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (copy[index], copy[swap]) = (copy[swap], copy[index]);
        }

        return copy;
    }

    private const string Stock = """
        {
          "formatVersion": 1,
          "profileId": "fuzz-stock",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" },
            { "name": "Action", "type": "Text", "role": "Lifecycle" },
            { "name": "Target", "type": "Text", "role": "Lifecycle" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" }, { "rule": "text.copy", "from": "Action", "to": "Action" }, { "rule": "text.copy", "from": "Target", "to": "Target" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "Action", "target": "Target", "scope": "Sku", "corrTarget": "CANC" } } },
          "limits": { "maxBlockItems": 6 },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.naturalKey", "key": ["Sku", "Quantity"] },
            { "id": "P2", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] }
          ] } ]
        }
        """;

    private const string Scale = """
        {
          "formatVersion": 1,
          "profileId": "fuzz-scale",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.naturalKey", "key": ["Instrument"] }
          ] } ]
        }
        """;
}
