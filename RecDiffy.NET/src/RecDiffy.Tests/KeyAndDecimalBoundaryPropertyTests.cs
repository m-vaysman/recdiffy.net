using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class KeyAndDecimalBoundaryPropertyTests
{
    private static readonly char[] Separators = ['\u001f', '|', ',', '=', '\u001e'];

    private static readonly string[] Alphabet =
    [
        "",
        "A",
        "B",
        @"\",
        @"\\",
        "\u001f",
        "\u001e",
        "|",
        ",",
        "=",
        "\r",
        "\n",
        @"\0",
        @"\-",
        @"A\u001fB",
        "A\u001fB",
        "A\u001eB",
        @"shared\u001e",
        "shared\u001e",
        "broker",
        "Broker",
    ];

    [Fact]
    public void Delimited_join_is_injective_and_round_trips_across_seeded_adversarial_tuples()
    {
        for (var seed = 0; seed < 64; seed++)
        {
            var random = new Random(seed * 997 + 17);
            var tuples = Enumerable.Range(0, 60)
                .Select(_ => RandomTuple(random))
                .ToArray();

            foreach (var separator in Separators)
            {
                var seen = new Dictionary<string, string[]>(StringComparer.Ordinal);
                foreach (var tuple in tuples)
                {
                    var joined = Delimited.Join(separator, tuple);
                    var decoded = Unjoin(separator, joined);
                    Assert.Equal(tuple, decoded);

                    if (seen.TryGetValue(joined, out var existing))
                        Assert.Equal(existing, tuple);
                    else
                        seen[joined] = tuple;
                }
            }
        }
    }

    [Fact]
    public void Match_keys_preserve_tuple_boundaries_scheme_isolation_value_text_and_blank_non_equivalence()
    {
        var schema = new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal)
        {
            ["Ref"] = new("Ref", FieldType.Identifier, FieldRole.Key, 0),
            ["Book"] = new("Book", FieldType.Symbol, FieldRole.Key, 0),
            ["Qty"] = new("Qty", FieldType.Decimal, FieldRole.Economic, 2),
        };
        string[] fields = ["Ref", "Book", "Qty"];
        string[] schemes = ["shared", "ISIN", "broker", "Broker", "cusip|1"];

        for (var seed = 0; seed < 64; seed++)
        {
            var random = new Random(seed * 521 + 31);
            var items = Enumerable.Range(0, 40)
                .Select(index =>
                {
                    var scheme = schemes[random.Next(schemes.Length)];
                    var refVal = random.Next(6) == 0 ? null : RandomToken(random);
                    var bookVal = random.Next(6) == 0 ? null : RandomToken(random);
                    var qtyVal = random.Next(6) == 0 ? (decimal?)null : (random.Next(2) == 0 ? -0.00m : random.Next(-500, 500) / 100m);
                    var rowId = $"R{index.ToString(CultureInfo.InvariantCulture)}";
                    var context = BuildRow(rowId, scheme, refVal, bookVal, qtyVal);
                    var item = new ActiveItem(
                        index,
                        Side.Left,
                        new ConsolidatedRow([new SourceRef("left", rowId, index)], context, false, null, null));
                    return (Item: item, Scheme: scheme, RefVal: refVal, BookVal: bookVal, QtyVal: qtyVal);
                })
                .ToArray();

            foreach (var entry in items)
            {
                var context = entry.Item.Row.Context;
                Assert.Equal(entry.RefVal, MatchKeys.ValueText(context, schema["Ref"]));
                Assert.Equal(entry.BookVal, MatchKeys.ValueText(context, schema["Book"]));

                var generalKey = MatchKeys.Compose(entry.Item, fields, schema, sharedOnly: false);
                var sharedKey = MatchKeys.Compose(entry.Item, fields, schema, sharedOnly: true);
                var hasNull = entry.RefVal is null || entry.BookVal is null || entry.QtyVal is null;

                if (hasNull)
                {
                    Assert.Null(generalKey);
                    Assert.Null(sharedKey);
                }
                else
                {
                    Assert.NotNull(generalKey);
                    if (entry.Scheme is "broker")
                        Assert.Null(sharedKey);
                    else
                        Assert.Equal(generalKey, sharedKey);
                }
            }

            var nonNull = items.Where(e => e.RefVal is not null && e.BookVal is not null && e.QtyVal is not null).ToArray();
            for (var i = 0; i < nonNull.Length; i++)
            {
                for (var j = i + 1; j < nonNull.Length; j++)
                {
                    var a = nonNull[i];
                    var b = nonNull[j];
                    var sameCanonical =
                        string.Equals(a.Scheme, b.Scheme, StringComparison.Ordinal)
                        && string.Equals(a.RefVal, b.RefVal, StringComparison.Ordinal)
                        && string.Equals(a.BookVal, b.BookVal, StringComparison.Ordinal)
                        && decimal.Round(a.QtyVal!.Value, 2) == decimal.Round(b.QtyVal!.Value, 2);
                    var keyA = MatchKeys.Compose(a.Item, fields, schema, sharedOnly: false);
                    var keyB = MatchKeys.Compose(b.Item, fields, schema, sharedOnly: false);
                    Assert.Equal(sameCanonical, string.Equals(keyA, keyB, StringComparison.Ordinal));
                }
            }
        }
    }

    [Fact]
    public void Group_sum_near_decimal_limits_never_overflows_or_commits_drifted_subsets()
    {
        var document = new ProfileDocument
        {
            FormatVersion = 1,
            ProfileId = "t5-decimal-boundary",
            Version = "1.0.0",
            Midpoint = MidpointMode.ToEven,
            Culture = "",
            SourceKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["left"] = "Id", ["right"] = "Id" },
            Fields =
            [
                new FieldDeclaration("Sku", FieldType.Symbol, FieldRole.Key, 0),
                new FieldDeclaration("Quantity", FieldType.Decimal, FieldRole.Economic, 0),
                new FieldDeclaration("Cash", FieldType.Decimal, FieldRole.Economic, 0),
            ],
            Normalization = new Dictionary<string, NormalizationSide>(StringComparer.Ordinal)
            {
                ["left"] = DecimalNormalization(),
                ["right"] = DecimalNormalization(),
            },
            Consolidation = ImmutableDictionary<string, ConsolidationSide>.Empty,
            Limits = new SearchLimits(8, 12, 8, 256),
            Paths =
            [
                new PathDeclaration(
                    "p",
                    new RouteDeclaration("route.all", null, null),
                    [
                        new PassDeclaration("P1", "match.groupSum", [], ["Sku"], ["Quantity", "Cash"], "left", 6),
                    ],
                    null),
            ],
        };

        var profile = Reconciler.Compile(document, RuleRegistry.CreateCore());
        var map = new SourceMap<DecRow>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Text("Quantity", r => r.Quantity.ToString(CultureInfo.InvariantCulture))
            .Text("Cash", r => r.Cash.ToString(CultureInfo.InvariantCulture));

        for (var seed = 0; seed < 40; seed++)
        {
            var random = new Random(seed * 313 + 9);
            var left = GenerateBoundaryCandidates(random);
            var right = GenerateBoundaryTargets(random, left);

            var result = Reconciler.Run(
                profile,
                RecordSources.FromObjects("left", left, map),
                RecordSources.FromObjects("right", right, map),
                ReferenceDataSet.Empty);

            var leftById = left.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var rightById = right.ToDictionary(r => r.Id, StringComparer.Ordinal);

            foreach (var entry in result.Entries.Where(e => e.Alignment is Alignment.Matched))
            {
                var exactQty = entry.Left
                    .Select(m => new BigInteger(leftById[m.RowKey].Quantity))
                    .Aggregate(BigInteger.Zero, (acc, v) => acc + v);
                var exactCash = entry.Left
                    .Select(m => new BigInteger(leftById[m.RowKey].Cash))
                    .Aggregate(BigInteger.Zero, (acc, v) => acc + v);
                var targetRow = rightById[Assert.Single(entry.Right).RowKey];

                Assert.Equal(new BigInteger(targetRow.Quantity), exactQty);
                Assert.Equal(new BigInteger(targetRow.Cash), exactCash);
            }

            Assert.Equal(left.Length + right.Length, result.Rows.Count);
        }
    }

    private static DecRow[] GenerateBoundaryCandidates(Random random)
    {
        var half = decimal.MaxValue / 2m;
        decimal[] pool =
        [
            decimal.MaxValue - random.Next(1, 50),
            decimal.MinValue + random.Next(1, 50),
            half,
            half - random.Next(10, 200),
            -half,
            random.Next(10, 500),
            random.Next(10, 500),
            random.Next(10, 500),
            -random.Next(10, 500),
        ];

        return
        [
            .. Enumerable.Range(0, 7).Select(index => new DecRow(
                "L" + index.ToString(CultureInfo.InvariantCulture),
                "S1",
                pool[random.Next(pool.Length)],
                pool[random.Next(pool.Length)])),
        ];
    }

    private static DecRow[] GenerateBoundaryTargets(Random random, DecRow[] left)
    {
        var indices = Enumerable.Range(0, left.Length).OrderBy(_ => random.Next()).Take(random.Next(2, 4)).ToArray();
        var qtySum = CheckedDecimal.Sum(indices.Select(i => left[i].Quantity));
        var cashSum = CheckedDecimal.Sum(indices.Select(i => left[i].Cash));
        return
        [
            new DecRow(
                "R0",
                "S1",
                qtySum ?? (decimal.MaxValue - 5m),
                cashSum ?? (decimal.MaxValue - 5m)),
        ];
    }

    private static NormalizationSide DecimalNormalization() =>
        new(
            Identify: [Rule("symbol.map", ("from", "Sku"), ("to", "Sku"))],
            Parse:
            [
                Rule("decimal.parse", ("from", "Quantity"), ("to", "Quantity")),
                Rule("decimal.parse", ("from", "Cash"), ("to", "Cash")),
            ],
            Derive: [],
            Validate: []);

    private static RuleCall Rule(string name, params (string Key, string Value)[] strings) =>
        new(
            name,
            null,
            strings.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);

    private static RowContext BuildRow(string id, string scheme, string? refVal, string? bookVal, decimal? qtyVal)
    {
        var pool = InternPool.Empty;
        var values = ImmutableDictionary.CreateBuilder<string, CanonicalCell>(StringComparer.Ordinal);
        if (refVal is not null)
        {
            (pool, var refId) = pool.InternIdentifier(scheme, "", refVal);
            values["Ref"] = CanonicalCell.FromInterned(FieldType.Identifier, refId);
        }

        if (bookVal is not null)
        {
            (pool, var bookId) = pool.InternSymbol(bookVal);
            values["Book"] = CanonicalCell.FromInterned(FieldType.Symbol, bookId);
        }

        if (qtyVal is { } number)
            values["Qty"] = CanonicalCell.FromDecimal(decimal.Round(number, 2) == 0m ? 0.00m : decimal.Round(number, 2));

        return new RowContext
        {
            RowKey = id,
            Side = Side.Left,
            Raw = ImmutableDictionary<string, object?>.Empty,
            Pool = pool,
            Values = values.ToImmutable(),
            Diagnostics = [],
            Traces = [],
        };
    }

    private static string[] RandomTuple(Random random) =>
    [
        .. Enumerable.Range(0, random.Next(1, 6)).Select(_ => RandomToken(random)),
    ];

    private static string RandomToken(Random random) =>
        string.Concat(Enumerable.Range(0, random.Next(0, 4)).Select(_ => Alphabet[random.Next(Alphabet.Length)]));

    private static string[] Unjoin(char separator, string joined)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var escaped = false;
        foreach (var ch in joined)
        {
            if (escaped)
            {
                current.Append(ch);
                escaped = false;
            }
            else if (ch == '\\')
            {
                escaped = true;
            }
            else if (ch == separator)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        parts.Add(current.ToString());
        return [.. parts];
    }

    private sealed record DecRow(string Id, string Sku, decimal Quantity, decimal Cash);
}
