using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;
using Xunit.Abstractions;

namespace RecDiffy.Tests;

// Adversarial pass on main: a seeded sweep of spec §10.1. Each seed builds a trade-like population (pairs, breaks,
// tolerance moves, allocations, partial fills, packages, lifecycle chains, duplicates, invalid text, orphans and
// crowds), writes its numbers and dates as text in each side's declared culture, and checks that the answer does not
// change under: row order, row-key scrambling, shared-reference scrambling, thread count, the server's culture, and
// the culture the same values were written in. The generator avoids the triggers of #163-#172.
// Scale with RECDIFFY_SWEEP_SEEDS (default 48) and RECDIFFY_SWEEP_GROUPS (scenario groups per seed, default 150).
public class AdversarialSweepTests(ITestOutputHelper output)
{
    private static readonly string[] Cultures = ["en-US", "de-DE", "sv-SE", "fr-FR", "hi-IN", "de-CH", "", "tr-TR", "ja-JP"];
    private static readonly string[] Servers = ["en-US", "de-DE", "tr-TR", "hi-IN", "sv-SE"];
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "dd MMMM yyyy"];

    private static readonly ProfileDocument Document = ProfileDocument.Parse(Profile);

    private static readonly SourceMap<Text> Map = new SourceMap<Text>()
        .Text("Id", row => row.Id).Text("Cpty", row => row.Cpty).Text("Instr", row => row.Instr).Text("Ref", row => row.Ref)
        .Text("Date", row => row.Date).Text("Qty", row => row.Qty).Text("Price", row => row.Price).Text("Cash", row => row.Cash)
        .Text("Action", row => row.Action).Text("Target", row => row.Target).Text("Link", row => row.Link).Text("Role", row => row.Role);

    private static int Env(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    [Fact]
    public void Answers_are_invariant_across_order_ids_threads_and_cultures()
    {
        var seeds = Env("RECDIFFY_SWEEP_SEEDS", 48);
        var groups = Env("RECDIFFY_SWEEP_GROUPS", 150);
        var failures = new ConcurrentBag<string>();
        var rows = 0L;
        var clock = Stopwatch.StartNew();
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, seed =>
        {
            try
            {
                Interlocked.Add(ref rows, Check(seed, groups, failures));
            }
            catch (Exception exception)
            {
                failures.Add($"seed {seed}: threw {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
            }
        });
        var used = Process.GetCurrentProcess().TotalProcessorTime - cpu;
        output.WriteLine($"seeds={seeds} groups={groups} rows/run={rows / Math.Max(seeds, 1)} wall={clock.Elapsed.TotalSeconds:F1}s cpu={used.TotalSeconds:F1}s cores~{used.TotalSeconds / Math.Max(clock.Elapsed.TotalSeconds, 0.001):F1} failures={failures.Count}");
        output.WriteLine("coverage: " + string.Join("; ", Coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")));
        foreach (var failure in failures.OrderBy(text => text, StringComparer.Ordinal).Take(12))
            output.WriteLine(failure);
        Assert.Empty(failures);
    }

    internal static long Check(int seed, int groups, ConcurrentBag<string> failures)
    {
        var population = Population.Generate(seed, groups);
        var leftCulture = Cultures[seed % Cultures.Length];
        var rightCulture = Cultures[(seed / Cultures.Length + 3) % Cultures.Length];
        var baseline = Answer(population.Run(leftCulture, rightCulture, seed, server: "en-US", threads: 1));

        void Expect(string variant, string answer)
        {
            if (string.Equals(baseline, answer, StringComparison.Ordinal) is false)
                failures.Add($"seed {seed} ({leftCulture}|{rightCulture}) {variant}: {FirstDifference(baseline, answer)}");
        }

        Expect("shuffled", Answer(population.Shuffled(seed).Run(leftCulture, rightCulture, seed, "en-US", 1)));
        Expect("threads", Answer(population.Run(leftCulture, rightCulture, seed, "en-US", Environment.ProcessorCount)));
        Expect("row-keys scrambled", Answer(population.ScrambledKeys(seed).Run(leftCulture, rightCulture, seed, "en-US", 1), population.KeyMap(seed)));
        Expect("refs scrambled", Answer(population.ScrambledRefs(seed).Run(leftCulture, rightCulture, seed, "en-US", 1)));
        foreach (var server in Servers.Skip(1))
            Expect($"server {server}", Answer(population.Run(leftCulture, rightCulture, seed, server, 1)));
        Expect("written en-US|de-DE", Answer(population.Run("en-US", "de-DE", seed + 7, "en-US", 1)));
        Expect("written hi-IN|sv-SE", Answer(population.Run("hi-IN", "sv-SE", seed + 11, "en-US", 1)));

        var result = population.Run(leftCulture, rightCulture, seed, "en-US", 1);
        var keys = population.Left.Select(row => ("left", row.Id)).Concat(population.Right.Select(row => ("right", row.Id))).Order().ToArray();
        var placed = result.Rows.Select(row => (row.Row.Source, row.Row.RowKey)).Order().ToArray();
        if (keys.SequenceEqual(placed) is false)
            failures.Add($"seed {seed}: rows placed {placed.Length}, input {keys.Length}");
        var twice = result.Entries.SelectMany(entry => entry.Left.Concat(entry.Right).Distinct().Select(member => (member, entry.Id))).GroupBy(pair => pair.member).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (twice.Length > 0)
            failures.Add($"seed {seed}: {twice.Length} rows in two entries, e.g. {twice[0]}");
        foreach (var row in result.Rows)
            Coverage.AddOrUpdate("row " + row.Disposition, 1, (_, count) => count + 1);
        foreach (var entry in result.Entries)
            Coverage.AddOrUpdate($"entry {entry.Alignment}/{entry.Composition}/{entry.Explanation.CommittedBy}/{entry.Explanation.Trail.FirstOrDefault()?.Detail}", 1, (_, count) => count + 1);
        foreach (var code in result.Rows.SelectMany(row => row.Diagnostics).Select(diagnostic => diagnostic.Code))
            Coverage.AddOrUpdate("diagnostic " + code, 1, (_, count) => count + 1);
        return population.Left.Length + population.Right.Length;
    }

    internal static readonly ConcurrentDictionary<string, int> Coverage = new(StringComparer.Ordinal);

    internal static string FirstDifference(string expected, string actual)
    {
        var left = expected.Split('\n');
        var right = actual.Split('\n');
        var index = Enumerable.Range(0, Math.Min(left.Length, right.Length)).FirstOrDefault(i => !string.Equals(left[i], right[i], StringComparison.Ordinal), -1);
        return index < 0
            ? $"line counts {left.Length} vs {right.Length}"
            : $"line {index}\n  expected {left[index]}\n  actual   {right[index]}";
    }

    // Everything §10.1 promises, with row keys mapped back to the originals and group ids replaced by membership.
    internal static string Answer(IReconciliationResult result, IReadOnlyDictionary<string, string>? back = null)
    {
        string Key(SourceRef member) => member.Source + ":" + (back is not null && back.TryGetValue(member.Source + ":" + member.RowKey, out var original) ? original : member.RowKey);
        string Members(ResultEntry entry) => string.Join(",", entry.Left.Concat(entry.Right).Select(Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var byId = result.Entries.ToDictionary(entry => entry.Id);
        var rows = result.Rows
            .Select(row => string.Join('|', Key(row.Row), row.Disposition, row.Suppression?.ToString() ?? "", row.Validity, string.Join(",", row.Diagnostics.Select(d => d.Code).Order(StringComparer.Ordinal)), row.EntryId is { } id ? Members(byId[id]) : ""))
            .Order(StringComparer.Ordinal);
        var entries = result.Entries
            .Select(entry => string.Join('|', entry.PathId, entry.Alignment, entry.Composition, entry.Outcome, entry.Explanation.CommittedBy ?? "", Members(entry),
                string.Join(",", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Kind}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}:{diff.Delta.Days.ToString(CultureInfo.InvariantCulture)}:{string.Join("+", diff.AttributedTo)}").Order(StringComparer.Ordinal))))
            .Order(StringComparer.Ordinal);
        return string.Join('\n', rows) + "\n--\n" + string.Join('\n', entries) + "\n--\n" + result.Summary;
    }

    // One logical row: typed values, written as text only when a run picks the side's culture.
    internal sealed record Logical(string Id, string Cpty, string Instr, string? Ref, DateOnly? Date, decimal? Qty, decimal? Price, decimal? Cash, string? Action = null, string? Target = null, string? Link = null, string? Role = null, string? Bad = null);

    internal sealed record Text(string Id, string Cpty, string Instr, string? Ref, string? Date, string? Qty, string? Price, string? Cash, string? Action, string? Target, string? Link, string? Role);

    internal sealed record Population(Logical[] Left, Logical[] Right)
    {
        public static Population Generate(int seed, int groups)
        {
            var random = new Random(seed);
            var left = new List<Logical>();
            var right = new List<Logical>();
            var instruments = Math.Max(4, groups / 3);
            for (var group = 0; group < groups; group++)
            {
                var cpty = "CP" + random.Next(0, 4).ToString(CultureInfo.InvariantCulture);
                var instr = "XS" + random.Next(0, instruments).ToString("D6", CultureInfo.InvariantCulture);
                var date = new DateOnly(2025, 6, 9).AddDays(random.Next(0, 3));
                var price = Math.Round((decimal)random.Next(9_000, 12_000) / 100m + random.Next(0, 100) / 10_000m, 4);
                var tag = group.ToString(CultureInfo.InvariantCulture);
                var qty = Money(random, 1, 5_000_000);
                var cash = Math.Round(qty * price / 100m, 2);
                string? Uti() => random.Next(3) == 0 ? null : "UTI-" + tag;
                Logical L(string id, decimal? q, decimal? c, string? reference = null, decimal? p = null) => new("L" + tag + id, cpty, instr, reference, date, q, p ?? price, c);
                Logical R(string id, decimal? q, decimal? c, string? reference = null, decimal? p = null) => new("R" + tag + id, cpty, instr, reference, date, q, p ?? price, c);
                switch (random.Next(16))
                {
                    case 0 or 1:
                    {
                        var uti = Uti();
                        left.Add(L("a", qty, cash, uti));
                        right.Add(R("a", qty, cash, uti));
                        break;
                    }

                    case 2:
                        left.Add(L("a", qty, cash, "UTI-" + tag));
                        right.Add(R("a", qty + Money(random, 1, 1000), cash, "UTI-" + tag));
                        break;
                    case 3:
                        left.Add(L("a", qty, cash, "UTI-" + tag));
                        right.Add(R("a", qty, cash, "UTI-" + tag, price + 0.0001m * random.Next(-2, 3)));
                        break;
                    case 4:
                    {
                        var parts = Enumerable.Range(0, random.Next(2, 5)).Select(_ => Money(random, 1, 100_000)).ToArray();
                        var cashes = parts.Select(part => Math.Round(part * price / 100m, 2)).ToArray();
                        left.AddRange(parts.Select((part, index) => L("p" + index.ToString(CultureInfo.InvariantCulture), part, cashes[index])));
                        right.Add(R("a", parts.Sum(), cashes.Sum()));
                        break;
                    }

                    case 5:
                        left.Add(L("a", qty, cash));
                        right.Add(R("a", Math.Round(qty * 0.8m, 2), Math.Round(cash * 0.8m, 2)));
                        break;
                    case 6:
                        left.Add(L("a", qty, cash, Uti()));
                        break;
                    case 7:
                        right.Add(R("a", qty, cash, Uti()));
                        break;
                    case 8:
                        left.Add(L("a", qty, cash));
                        left.Add(L("b", qty, cash));
                        right.Add(R("a", qty, cash));
                        break;
                    case 9:
                        left.Add(L("a", qty, cash) with { Bad = random.Next(2) == 0 ? "abc" : "1.2.3" });
                        right.Add(R("a", qty, cash));
                        break;
                    case 10:
                    {
                        var row = L("a", qty, cash, Uti());
                        left.Add(row);
                        left.Add(row);
                        right.Add(R("a", qty, cash, row.Ref));
                        break;
                    }

                    case 11:
                    {
                        var corrected = Money(random, 1, 5_000_000);
                        var correctedCash = Math.Round(corrected * price / 100m, 2);
                        left.Add(L("a", corrected, correctedCash));
                        right.Add(R("n", qty, cash) with { Action = "NEW" });
                        right.Add(R("c", qty, cash) with { Action = "CANC", Target = "R" + tag + "n" });
                        right.Add(R("x", corrected, correctedCash) with { Action = "CORR", Target = "R" + tag + "c" });
                        break;
                    }

                    case 12:
                        right.Add(R("n", qty, cash) with { Action = "NEW" });
                        right.Add(R("c", qty, cash) with { Action = "CANC", Target = "R" + tag + "n" });
                        break;
                    case 13:
                    {
                        var pay = Money(random, 1000, 900_000);
                        var receive = Money(random, 1000, 900_000);
                        var uti = Uti();
                        left.Add(L("pay", qty, pay, uti) with { Link = "PLK-" + tag, Role = "PAY" });
                        left.Add(L("rec", qty, receive, uti, price - 0.25m) with { Link = "PLK-" + tag, Role = "REC" });
                        right.Add(R("a", qty, pay - receive, uti));
                        break;
                    }

                    case 14:
                    {
                        // More eligible items than maxBlockItems (10): BlockTooLarge.
                        var parts = Enumerable.Range(0, 11 + random.Next(4)).Select(_ => Money(random, 1, 1000)).ToArray();
                        left.AddRange(parts.Select((part, index) => L("k" + index.ToString(CultureInfo.InvariantCulture), part, Math.Round(part * price / 100m, 2))));
                        right.Add(R("a", parts.Take(3).Sum(), parts.Take(3).Sum(part => Math.Round(part * price / 100m, 2))));
                        break;
                    }

                    default:
                    {
                        // Ten small whole quantities: many subsets reach the quantity, none the cash, so the node budget runs out.
                        var parts = Enumerable.Range(1, 10).Select(part => (decimal)part).ToArray();
                        left.AddRange(parts.Select((part, index) => L("b" + index.ToString(CultureInfo.InvariantCulture), part, part * 10m + random.Next(0, 3) / 100m)));
                        right.Add(R("a", 27m, 271.37m));
                        break;
                    }
                }
            }

            return new Population([.. left], [.. right]);
        }

        private static decimal Money(Random random, int low, int high) => random.Next(low, high) + random.Next(0, 100) / 100m;

        public Population Shuffled(int seed) => new(Shuffle(Left, seed), Shuffle(Right, seed ^ 0x5bd1e995));

        public IReadOnlyDictionary<string, string> KeyMap(int seed)
        {
            var forward = Forward(seed);
            return forward.ToDictionary(pair => pair.Value, pair => pair.Key[(pair.Key.IndexOf(':', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
        }

        // A bijection on each side's row keys that destroys their order; lifecycle targets follow it.
        private Dictionary<string, string> Forward(int seed)
        {
            var random = new Random(seed * 31 + 7);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (side, rows) in new[] { ("left", Left), ("right", Right) })
            {
                var keys = rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).ToArray();
                var labels = Shuffle(Enumerable.Range(0, keys.Length).Select(index => "Z" + (keys.Length - index).ToString("X5", CultureInfo.InvariantCulture) + random.Next(10).ToString(CultureInfo.InvariantCulture)).ToArray(), seed + 3);
                foreach (var (key, index) in keys.Select((key, index) => (key, index)))
                    map[side + ":" + key] = side + ":" + labels[index];
            }

            return map;
        }

        public Population ScrambledKeys(int seed)
        {
            var map = Forward(seed);
            string Rename(string side, string key) => map[side + ":" + key][(side.Length + 1)..];
            return new(
                Shuffle([.. Left.Select(row => row with { Id = Rename("left", row.Id) })], seed + 5),
                Shuffle([.. Right.Select(row => row with { Id = Rename("right", row.Id), Target = row.Target is null ? null : Rename("right", row.Target) })], seed + 6));
        }

        public Population ScrambledRefs(int seed)
        {
            var references = Left.Concat(Right).Select(row => row.Ref).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var renamed = Shuffle(references.Select((_, index) => "EX-" + (index * 7919 % 100_003).ToString("D6", CultureInfo.InvariantCulture)).ToArray(), seed + 9);
            var map = references.Select((reference, index) => (reference, index)).ToDictionary(pair => pair.reference, pair => renamed[pair.index], StringComparer.Ordinal);
            string? Rename(string? reference) => reference is null ? null : map[reference];
            return new([.. Left.Select(row => row with { Ref = Rename(row.Ref) })], [.. Right.Select(row => row with { Ref = Rename(row.Ref) })]);
        }

        public IReconciliationResult Run(string leftCulture, string rightCulture, int textSeed, string server, int threads)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(server);
                var document = Document with
                {
                    SourceReadings = ImmutableDictionary.Create<string, SourceReading>(StringComparer.Ordinal)
                        .Add("left", new SourceReading(leftCulture, null))
                        .Add("right", new SourceReading(rightCulture, null)),
                };
                var profile = Reconciler.Compile(document, RuleRegistry.CreateCore());
                return Reconciler.Run(
                    profile,
                    RecordSources.FromObjects("left", Write(Left, leftCulture, textSeed), Map),
                    RecordSources.FromObjects("right", Write(Right, rightCulture, textSeed + 1), Map),
                    ReferenceDataSet.Empty,
                    new RunOptions(threads));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        // Writes each value as a file in that culture would: grouped or plain numbers, numeric or month-name dates.
        // Identical duplicates (same id) get the same text, so they stay identical.
        private static Text[] Write(Logical[] rows, string cultureName, int seed)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var dates = CultureDateParsing.Strict(culture);
            return [.. rows.Select(row =>
            {
                var random = new Random(HashCode.Combine(seed, StringComparer.Ordinal.GetHashCode(row.Id)));
                var grouped = random.Next(2) == 0;
                string? Number(decimal? value, int scale) => value?.ToString((grouped ? "N" : "F") + scale.ToString(CultureInfo.InvariantCulture), culture);
                var dateFormat = DateFormats[random.Next(DateFormats.Length)];
                return new Text(
                    row.Id, row.Cpty, row.Instr, row.Ref,
                    row.Date?.ToString(dateFormat, dates),
                    row.Bad ?? Number(row.Qty, 2), Number(row.Price, 4), Number(row.Cash, 2),
                    row.Action, row.Target, row.Link, row.Role);
            })];
        }
    }

    internal static T[] Shuffle<T>(T[] rows, int seed)
    {
        var copy = (T[])rows.Clone();
        var random = new Random(seed);
        for (var index = copy.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (copy[index], copy[swap]) = (copy[swap], copy[index]);
        }

        return copy;
    }

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-sweep",
          "version": "1.0.0",
          "rounding": { "midpoint": "ToEven" },
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Identifier", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "TradeRef", "type": "Identifier", "role": "Identity" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 4, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "EventAction", "type": "Text", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Instr", "to": "Instrument", "scheme": "ISIN" },
                { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "shared" },
                { "rule": "text.copy", "from": "Link", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Role", "to": "LegRole" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Date", "to": "TradeDate", "formats": ["dd/MM/yyyy", "dd MMMM yyyy"] },
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Instr", "to": "Instrument", "scheme": "ISIN" },
                { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "shared" },
                { "rule": "text.copy", "from": "Action", "to": "EventAction" },
                { "rule": "text.copy", "from": "Target", "to": "EventTarget" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Date", "to": "TradeDate", "formats": ["dd/MM/yyyy", "dd MMMM yyyy"] },
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            }
          },
          "consolidation": {
            "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"],
                                      "aggregate": { "Quantity": "Same", "Price": "FromRole(PAY)", "NetCash": "DifferenceByRole(PAY,REC)" } } ] },
            "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
          },
          "limits": { "maxBlockItems": 10, "maxTargetsPerComponent": 6, "maxNodesPerTarget": 60 },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" },
            "passes": [
              { "id": "P1", "pass": "match.sharedRef" },
              { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Quantity"] },
              { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Price"], "conserve": ["Quantity", "NetCash"] },
              { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate"] } ],
            "diff": { "fields": { "Price": { "tolerance": { "absolute": 0.0001 } } } } } ]
        }
        """;
}
