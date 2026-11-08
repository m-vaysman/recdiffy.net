using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using RecDiffy.Results;
using Xunit.Abstractions;

namespace RecDiffy.Tests;

// Adversarial pass on main. Spec §12.4 property: "P3 agrees with a brute-force enumerator on random blocks up to 12
// items". Each block has one or two targets on the right and 2 to 12 allocations on the left, with repeated values
// (classes), zero and negative values. The enumerator counts every admissible subset (2 to maxGroupSize items) that
// conserves Quantity and NetCash exactly, then applies the §4.7 component table with a budget large enough to finish.
// Scale with RECDIFFY_ORACLE_RUNS (default 24) x RECDIFFY_ORACLE_BLOCKS (blocks per run, default 400).
public class AdversarialSubsetOracleTests(ITestOutputHelper output)
{
    // Which limits a sweep runs under, and how many items its blocks hold.
    private sealed record Setting(CompiledProfile Profile, int MaxGroup, int MinItems, int MaxItems);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-subset-oracle",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Block", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Block", "to": "Block" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Block", "to": "Block" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ] }
          },
          "limits": { "maxGroupSize": 8, "maxBlockItems": 24, "maxTargetsPerComponent": 8, "maxNodesPerTarget": 5000000 },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Block"], "conserve": ["Quantity", "NetCash"] } ] } ]
        }
        """;

    private static readonly Setting Standard = new(Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()), 8, 2, 12);

    // The hard cap on maxGroupSize is 16 (spec §9.3); blocks of 13 to 18 items stay under maxBlockItems 24.
    private static readonly Setting NearCaps = new(
        Reconciler.Compile(ProfileDocument.Parse(Profile) with { Limits = new SearchLimits(16, 24, 8, 50_000_000) }, RuleRegistry.CreateCore()),
        16,
        13,
        18);

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Block", row => row.Block)
        .Text("Qty", row => row.Qty.ToString(CultureInfo.InvariantCulture)).Text("Cash", row => row.Cash.ToString(CultureInfo.InvariantCulture));

    private static int Env(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    [Fact]
    public void Group_sum_agrees_with_a_brute_force_enumerator() =>
        Sweep(Standard, Env("RECDIFFY_ORACLE_RUNS", 24), Env("RECDIFFY_ORACLE_BLOCKS", 400));

    [Fact]
    public void Group_sum_agrees_with_a_brute_force_enumerator_near_the_hard_caps() =>
        Sweep(NearCaps, Env("RECDIFFY_ORACLE_RUNS", 24), Env("RECDIFFY_ORACLE_CAP_BLOCKS", 20));

    private void Sweep(Setting setting, int runs, int blocks)
    {
        var failures = new ConcurrentBag<string>();
        var clock = Stopwatch.StartNew();
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var states = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        Parallel.For(0, runs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, run =>
        {
            try
            {
                Check(setting, run, blocks, failures, states);
            }
            catch (Exception exception)
            {
                failures.Add($"run {run}: threw {exception}");
            }
        });
        var used = Process.GetCurrentProcess().TotalProcessorTime - cpu;
        output.WriteLine($"runs={runs} blocks/run={blocks} wall={clock.Elapsed.TotalSeconds:F1}s cpu={used.TotalSeconds:F1}s cores~{used.TotalSeconds / Math.Max(clock.Elapsed.TotalSeconds, 0.001):F1} failures={failures.Count}");
        output.WriteLine("expected states: " + string.Join("; ", states.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")));
        foreach (var failure in failures.OrderBy(text => text, StringComparer.Ordinal).Take(15))
            output.WriteLine(failure);
        Assert.Empty(failures);
    }

    private static void Check(Setting setting, int run, int count, ConcurrentBag<string> failures, ConcurrentDictionary<string, int> states)
    {
        var random = new Random(run * 7919 + 17);
        var blocks = Enumerable.Range(0, count).Select(index => Block.Generate(random, $"B{run}-{index}", setting.MinItems, setting.MaxItems)).ToArray();
        var left = blocks.SelectMany(block => block.Items).ToArray();
        var right = blocks.SelectMany(block => block.Targets).ToArray();
        var result = Execute(setting.Profile, left, right);
        var shuffled = Execute(setting.Profile, AdversarialSweepTests.Shuffle(left, run + 1), AdversarialSweepTests.Shuffle(right, run + 2));
        var dispositions = Dispositions(result);
        var again = Dispositions(shuffled);
        foreach (var block in blocks)
        {
            var expected = block.Expected(setting.MaxGroup);
            states.AddOrUpdate(expected.Kind, 1, (_, value) => value + 1);
            var actual = block.Items.Concat(block.Targets).ToDictionary(row => row.Id, row => dispositions[row.Id], StringComparer.Ordinal);
            var problem = expected.Explain(block, actual);
            if (problem is not null)
                failures.Add($"run {run} block {block.Name} ({expected.Kind}): {problem}\n  {block}");
            var order = block.Items.Concat(block.Targets).Where(row => dispositions[row.Id] != again[row.Id]).Select(row => $"{row.Id} {dispositions[row.Id]}->{again[row.Id]}").ToArray();
            if (order.Length > 0)
                failures.Add($"run {run} block {block.Name}: shuffle changed {string.Join(", ", order)}\n  {block}");
        }
    }

    private static ReconciliationResult Execute(CompiledProfile compiled, Row[] left, Row[] right) =>
        Reconciler.Run(compiled, RecordSources.FromObjects("left", left, Map), RecordSources.FromObjects("right", right, Map), ReferenceDataSet.Empty);

    private static Dictionary<string, Disposition> Dispositions(ReconciliationResult result) =>
        result.Rows.ToDictionary(row => row.Row.RowKey, row => row.Disposition, StringComparer.Ordinal);

    public sealed record Row(string Id, string Block, decimal Qty, decimal Cash);

    private sealed record Block(string Name, Row[] Items, Row[] Targets)
    {
        public static Block Generate(Random random, string name, int minItems, int maxItems)
        {
            var size = random.Next(minItems, maxItems + 1);
            // Near the caps, half the blocks have powers-of-two cash: exactly one subset conserves, of a chosen size from 9
            // to 17, so large unique groups, and one just over maxGroupSize 16, are exercised.
            if (minItems > 12 && random.Next(2) == 0)
            {
                var distinct = Enumerable.Range(0, size).Select(index => new Row($"{name}-L{index.ToString(CultureInfo.InvariantCulture)}", name, 1m, 1L << index)).ToArray();
                var pick = AdversarialSweepTests.Shuffle(Enumerable.Range(0, size).ToArray(), random.Next()).Take(Math.Min(size, random.Next(9, 18))).ToArray();
                return new Block(name, distinct, [new Row($"{name}-R0", name, pick.Length, pick.Sum(index => 1L << index))]);
            }

            var pool = random.Next(3) switch { 0 => 3, 1 => 6, _ => 40 };
            var signed = random.Next(5) == 0;
            var items = Enumerable.Range(0, size).Select(index =>
            {
                var quantity = (decimal)random.Next(signed ? -pool : 0, pool + 1);
                var cash = random.Next(4) == 0 ? quantity * 10m + random.Next(-1, 2) : quantity * 10m;
                return new Row($"{name}-L{index.ToString(CultureInfo.InvariantCulture)}", name, quantity, cash);
            }).ToArray();
            var targets = Enumerable.Range(0, random.Next(4) == 0 ? 2 : 1).Select(index =>
            {
                var chosen = items.Where(_ => random.Next(2) == 0).ToArray();
                var quantity = random.Next(6) == 0 ? (decimal)random.Next(1, 50) : chosen.Sum(item => item.Qty);
                var cash = random.Next(6) == 0 ? quantity * 10m : chosen.Sum(item => item.Cash);
                return new Row($"{name}-R{index.ToString(CultureInfo.InvariantCulture)}", name, quantity, cash);
            }).ToArray();
            return new Block(name, items, targets);
        }

        // Every admissible conserving subset of the items, as bitmasks. The generator only makes whole numbers, so the
        // sums run in long arithmetic: 2^18 subsets per target stay cheap.
        public int[] Solutions(Row target, int maxGroup)
        {
            var quantities = Items.Select(item => (long)item.Qty).ToArray();
            var cashes = Items.Select(item => (long)item.Cash).ToArray();
            var (wantQuantity, wantCash) = ((long)target.Qty, (long)target.Cash);
            var found = new List<int>();
            for (var mask = 1; mask < 1 << Items.Length; mask++)
            {
                var size = System.Numerics.BitOperations.PopCount((uint)mask);
                if (size < 2 || size > maxGroup)
                    continue;
                long quantity = 0, cash = 0;
                for (var bits = mask; bits != 0; bits &= bits - 1)
                {
                    var bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                    quantity += quantities[bit];
                    cash += cashes[bit];
                }

                if (quantity == wantQuantity && cash == wantCash)
                    found.Add(mask);
            }

            return [.. found];
        }

        public Expectation Expected(int maxGroup)
        {
            var solutions = Targets.Select(target => Solutions(target, maxGroup)).ToArray();
            var unique = solutions.Where(found => found.Length == 1).Select(found => found[0]).ToArray();
            var shared = unique.SelectMany((mask, index) => unique.Skip(index + 1).Where(other => (mask & other) != 0)).Any();
            return solutions.Any(found => found.Length > 1) || shared
                ? new Expectation("Contest", solutions)
                : solutions.All(found => found.Length == 0)
                    ? new Expectation("None", solutions)
                    : new Expectation("Commit", solutions);
        }

        public override string ToString() =>
            $"items [{string.Join(", ", Items.Select(item => $"{item.Qty.ToString(CultureInfo.InvariantCulture)}/{item.Cash.ToString(CultureInfo.InvariantCulture)}"))}] targets [{string.Join(", ", Targets.Select(item => $"{item.Qty.ToString(CultureInfo.InvariantCulture)}/{item.Cash.ToString(CultureInfo.InvariantCulture)}"))}]";
    }

    private sealed record Expectation(string Kind, int[][] Solutions)
    {
        // Returns null when the engine's dispositions fit the expectation, else what is wrong.
        public string? Explain(Block block, IReadOnlyDictionary<string, Disposition> actual)
        {
            string Show(Row row) => $"{row.Id}={actual[row.Id]}";
            var items = block.Items;
            switch (Kind)
            {
                case "None":
                    return block.Targets.Any(target => actual[target.Id] != Disposition.RightOnly) || items.Any(item => actual[item.Id] != Disposition.LeftOnly)
                        ? "expected no group: " + string.Join(", ", items.Concat(block.Targets).Select(Show))
                        : null;
                case "Commit":
                {
                    var matched = block.Targets.Select((target, index) => (target, index)).Where(pair => Solutions[pair.index].Length == 1).ToArray();
                    var mask = matched.Aggregate(0, (all, pair) => all | Solutions[pair.index][0]);
                    var wrongTargets = block.Targets.Select((target, index) => (target, index))
                        .Where(pair => actual[pair.target.Id] != (Solutions[pair.index].Length == 1 ? Disposition.Matched : Disposition.RightOnly)).ToArray();
                    var wrongItems = items.Select((item, bit) => (item, bit))
                        .Where(pair => actual[pair.item.Id] != ((mask & (1 << pair.bit)) != 0 ? Disposition.Matched : Disposition.LeftOnly)).ToArray();
                    return wrongTargets.Length + wrongItems.Length > 0
                        ? $"expected unique group(s) {string.Join(" ", matched.Select(pair => Convert.ToString(Solutions[pair.index][0], 2)))}: " + string.Join(", ", items.Concat(block.Targets).Select(Show))
                        : null;
                }

                default:
                {
                    var drawable = Solutions.SelectMany(found => found).Aggregate(0, (all, mask) => all | mask);
                    // A target with no conserving subset at all stays unmatched beside a contest.
                    var bad = block.Targets.Select((target, index) => (target, index)).Any(pair => actual[pair.target.Id] != (Solutions[pair.index].Length == 0 ? Disposition.RightOnly : Disposition.Ambiguous))
                        || items.Any(item => actual[item.Id] == Disposition.Matched)
                        || items.Select((item, bit) => (item, bit)).Any(pair => actual[pair.item.Id] == Disposition.Ambiguous && (drawable & (1 << pair.bit)) == 0);
                    return bad
                        ? $"expected a contest ({string.Join(" | ", Solutions.Select(found => found.Length.ToString(CultureInfo.InvariantCulture)))} solutions): " + string.Join(", ", items.Concat(block.Targets).Select(Show))
                        : null;
                }
            }
        }
    }
}
