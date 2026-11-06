using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class VerificationTests
{
    [Fact]
    public void Matches_warehouse_bins_to_book_stock_without_the_trading_pack()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Warehouse), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", new[] { Bin("A", "4"), Bin("B", "6") }, BinMap()),
            RecordSources.FromObjects("book", new[] { Bin("BOOK", "10") }, BinMap()),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal(Composition.ManyToOne, entry.Composition);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(10m, result.Projection(entry.Id, Side.Left)["Quantity"].Number);
        Assert.Equal(3, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    [Fact]
    public void Commits_blocks_under_the_limit_and_leaves_an_oversize_block_unresolved()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(LimitedStock), RuleRegistry.CreateCore());
        Dictionary<string, string> Stock(string id, string sku, string quantity) => new(StringComparer.Ordinal)
        {
            ["Id"] = id,
            ["Sku"] = sku,
            ["Quantity"] = quantity,
        };
        var split = Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", new[] { Stock("A1", "A", "1"), Stock("A2", "A", "1"), Stock("B1", "B", "1"), Stock("B2", "B", "1") }, BinMap()),
            RecordSources.FromObjects("book", new[] { Stock("RA", "A", "2"), Stock("RB", "B", "2") }, BinMap()),
            ReferenceDataSet.Empty);
        var over = Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", new[] { Stock("L1", "A", "1"), Stock("L2", "A", "1"), Stock("L3", "A", "1"), Stock("L4", "A", "1") }, BinMap()),
            RecordSources.FromObjects("book", new[] { Stock("R", "A", "4") }, BinMap()),
            ReferenceDataSet.Empty);

        Assert.Equal(2, split.Entries.Count(entry => entry.Alignment == Alignment.Matched));
        Assert.DoesNotContain(over.Entries, entry => entry.Alignment == Alignment.Matched);
        Assert.Contains(over.Entries, entry => entry.Alignment == Alignment.SearchIncomplete);
    }

    [Fact]
    public void Gives_every_input_row_one_disposition()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Pairs), RuleRegistry.CreateCore());
        var left = Enumerable.Range(0, 12).Select(index => Pair(index < 8 ? $"S{index}" : $"L{index}", "10")).ToArray();
        var right = Enumerable.Range(0, 10).Select(index => Pair(index < 8 ? $"S{index}" : $"R{index}", "10")).ToArray();
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", left, PairMap()),
            RecordSources.FromObjects("book", right, PairMap()),
            ReferenceDataSet.Empty);

        Assert.Equal(left.Length + right.Length, result.Rows.Count);
        Assert.Equal(result.Rows.Count, result.Rows.Select(row => row.Row).Distinct().Count());
        var matched = result.Entries.Where(entry => entry.Alignment == Alignment.Matched).SelectMany(entry => entry.Left.Concat(entry.Right)).ToArray();
        Assert.Equal(matched.Length, matched.Distinct().Count());
    }

    [Fact]
    public void Does_not_match_a_blank_or_invalid_quantity_as_zero()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Warehouse), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", new[] { Bin("A", "3"), Bin("B", ""), Bin("C", "bad") }, BinMap()),
            RecordSources.FromObjects("book", new[] { Bin("BOOK", "3") }, BinMap()),
            ReferenceDataSet.Empty);
        var blanks = Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", new[] { Bin("A", ""), Bin("B", "") }, BinMap()),
            RecordSources.FromObjects("book", new[] { Bin("BOOK", "0") }, BinMap()),
            ReferenceDataSet.Empty);

        Assert.DoesNotContain(result.Entries, entry => entry.Alignment == Alignment.Matched && entry.Outcome == FieldOutcome.Exact);
        Assert.DoesNotContain(blanks.Entries, entry => entry.Alignment == Alignment.Matched && entry.Outcome == FieldOutcome.Exact);
        Assert.Contains(result.Rows, row => row.Validity == InputValidity.Invalid);
        Assert.DoesNotContain(result.Rows, row => row.Validity == InputValidity.Invalid && row.Disposition == Disposition.Matched);
    }

    [Fact]
    public void Routes_each_row_by_the_declared_field()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Routes), RuleRegistry.CreateCore());
        var map = new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row["Id"])
            .Text("Asset", row => row["Asset"])
            .Text("Quantity", row => row["Quantity"]);
        Dictionary<string, string> Row(string id, string asset, string quantity) => new(StringComparer.Ordinal)
        {
            ["Id"] = id,
            ["Asset"] = asset,
            ["Quantity"] = quantity,
        };
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", new[] { Row("E", "Equity", "5"), Row("B", "Bond", "5") }, map),
            RecordSources.FromObjects("street", new[] { Row("S", "Bond", "5") }, map),
            ReferenceDataSet.Empty);

        var matched = Assert.Single(result.Entries, entry => entry.Alignment == Alignment.Matched);
        Assert.Equal("bonds", matched.PathId);
        Assert.Contains(result.Rows, row => row.Disposition == Disposition.LeftOnly);
        Assert.DoesNotContain(result.Rows, row => row.Disposition == Disposition.Matched && row.Row.RowKey == "E");
    }

    [Fact]
    public void Quarantines_a_missing_source_key()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Warehouse), RuleRegistry.CreateCore());
        var map = new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row.TryGetValue("Id", out var id) ? id : null)
            .Text("Sku", row => row["Sku"])
            .Text("Quantity", row => row["Quantity"]);
        Dictionary<string, string> Row(string? id, string sku) => new(StringComparer.Ordinal)
        {
            ["Sku"] = sku,
            ["Quantity"] = "3",
        };
        var missing = Row(null, "A");
        var alsoMissing = Row(null, "B");
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", new[] { missing, alsoMissing }, map),
            RecordSources.FromObjects("book", new[] { Bin("BOOK", "3") }, map),
            ReferenceDataSet.Empty);

        Assert.Equal(2, result.Rows.Count(row => row.Disposition == Disposition.Quarantined && row.Diagnostics.Any(diagnostic => diagnostic.Code == "MissingKey")));
        Assert.DoesNotContain(result.Entries, entry => entry.Alignment == Alignment.Matched);
    }

    [Fact]
    public void Stops_a_subset_search_when_the_run_is_cancelled() =>
        Assert.Throws<OperationCanceledException>(() =>
            SubsetSearch.Search([new ConservedClass([1m], [0]), new ConservedClass([2m], [1])], [3m], 8, 250_000, new CancellationToken(true)));

    [Fact]
    public void Agrees_with_brute_force_on_a_twelve_item_block()
    {
        var values = new decimal[] { 1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m, 11m, 12m };
        var classes = values.Select((value, index) => new ConservedClass([value], [index])).ToArray();
        var target = 15m;
        var search = SubsetSearch.Search(classes, [target], 8, 250_000);
        var brute = Brute(values, target, 8);

        Assert.Equal(brute, search.State);
    }

    [Fact]
    public void Breaks_an_accrued_delta_just_outside_the_tolerance()
    {
        var inside = FieldDiffer.Compare(Money(123_252.60m), Money(123_251.35m), AccruedPlan(1.25m));
        var outside = FieldDiffer.Compare(Money(123_252.60m), Money(123_251.34m), AccruedPlan(1.25m));

        Assert.Equal(FieldOutcome.WithinTolerance, Assert.Single(inside).Outcome);
        Assert.Equal(FieldOutcome.Break, Assert.Single(outside).Outcome);
    }

    [Fact]
    public void Compares_accrued_when_only_one_side_is_a_bond_and_does_not_widen_price()
    {
        var diffs = FieldDiffer.Compare(
            Row("Bond", 100m, 1m),
            Row("Equity", 100.0001m, 2m),
            MixedPlan());

        Assert.Equal(FieldOutcome.Break, diffs.Single(diff => diff.Field.Name == "AssetClass").Outcome);
        Assert.Equal(FieldOutcome.WithinTolerance, diffs.Single(diff => diff.Field.Name == "Accrued").Outcome);
        Assert.Equal(FieldOutcome.Break, diffs.Single(diff => diff.Field.Name == "Price").Outcome);
    }

    private static SearchState Brute(decimal[] values, decimal target, int maxGroup)
    {
        var hits = 0;
        var count = 1 << values.Length;
        for (var mask = 1; mask < count; mask++)
        {
            var bits = BitOperations(mask);
            if (bits is < 2 || bits > maxGroup)
                continue;
            decimal sum = 0m;
            for (var index = 0; index < values.Length; index++)
            {
                if ((mask & (1 << index)) != 0)
                    sum += values[index];
            }

            if (sum == target && ++hits == 2)
                return SearchState.Contest;
        }

        return hits == 1 ? SearchState.Unique : SearchState.None;
    }

    private static int BitOperations(int mask)
    {
        var count = 0;
        while (mask > 0)
        {
            count += mask & 1;
            mask >>= 1;
        }

        return count;
    }

    private static DiffPlan AccruedPlan(decimal tolerance) => DiffPlan.From(
        new PathDeclaration("trade", new RouteDeclaration("route.all", null, null), [], new DiffDeclaration(new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)
        {
            ["Accrued"] = new(new ToleranceDeclaration("Absolute", tolerance, null, null, null), null, null),
        })),
        [new FieldDeclaration("Accrued", FieldType.Decimal, FieldRole.Economic, 2)],
        MidpointMode.ToEven);

    private static DiffPlan MixedPlan() => DiffPlan.From(
        new PathDeclaration("trade", new RouteDeclaration("route.all", null, null), [], new DiffDeclaration(new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)
        {
            ["Accrued"] = new(new ToleranceDeclaration("Absolute", 1.25m, null, null, null), null, new WhenClause("AssetClass", null, null, ["Bond", "Swap"])),
            ["Price"] = new(new ToleranceDeclaration("Absolute", 0.0001m, null, null, new WhenClause("AssetClass", "Forward", null, null)), null, null),
        })),
        [
            new FieldDeclaration("AssetClass", FieldType.Symbol, FieldRole.Key, 0),
            new FieldDeclaration("Price", FieldType.Decimal, FieldRole.Economic, 6),
            new FieldDeclaration("Accrued", FieldType.Decimal, FieldRole.Economic, 2),
        ],
        MidpointMode.ToEven);

    private static RowContext Money(decimal accrued)
    {
        var row = RowContext.Create("row", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        return row.Write("Accrued", CanonicalCell.FromDecimal(accrued), "test", null, null);
    }

    private static RowContext Row(string asset, decimal price, decimal accrued)
    {
        var row = RowContext.Create(asset, Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (pool, id) = row.Pool.InternSymbol(asset);
        return row.WithPool(pool)
            .Write("AssetClass", CanonicalCell.FromInterned(FieldType.Symbol, id), "test", null, asset)
            .Write("Price", CanonicalCell.FromDecimal(price), "test", null, null)
            .Write("Accrued", CanonicalCell.FromDecimal(accrued), "test", null, null);
    }

    private static Dictionary<string, string> Bin(string id, string quantity) => new(StringComparer.Ordinal)
    {
        ["Id"] = id,
        ["Sku"] = "SKU-1",
        ["Quantity"] = quantity,
    };

    private static SourceMap<Dictionary<string, string>> BinMap() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row["Id"])
            .Text("Sku", row => row["Sku"])
            .Text("Quantity", row => row["Quantity"]);

    private static Dictionary<string, string> Pair(string id, string quantity) => new(StringComparer.Ordinal)
    {
        ["Id"] = id,
        ["Counterparty"] = "CITI",
        ["Instrument"] = id.StartsWith('S') ? id : "ORPHAN",
        ["TradeDate"] = "2025-06-12",
        ["Side"] = "BUY",
        ["Quantity"] = quantity,
    };

    private static SourceMap<Dictionary<string, string>> PairMap() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row["Id"])
            .Text("Counterparty", row => row["Counterparty"])
            .Text("Instrument", row => row["Instrument"])
            .Text("TradeDate", row => row["TradeDate"])
            .Text("Side", row => row["Side"])
            .Text("Quantity", row => row["Quantity"]);

    private const string Warehouse = """
        {
          "formatVersion": 1,
          "profileId": "warehouse",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] }
          ] } ]
        }
        """;

    private const string LimitedStock = """
        {
          "formatVersion": 1,
          "profileId": "warehouse-limited",
          "version": "1.0.0",
          "limits": { "maxBlockItems": 3 },
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] }
          ] } ]
        }
        """;

    private const string Pairs = """
        {
          "formatVersion": 1,
          "profileId": "pairs",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [
              { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
              { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
              { "rule": "symbol.map", "from": "Side", "to": "Side" }
            ], "parse": [
              { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
              { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }
            ] },
            "right": { "identify": [
              { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
              { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
              { "rule": "symbol.map", "from": "Side", "to": "Side" }
            ], "parse": [
              { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
              { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }
            ] }
          },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] }
          ] } ]
        }
        """;

    private const string Routes = """
        {
          "formatVersion": 1,
          "profileId": "routes",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Asset", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Asset", "to": "Asset" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Asset", "to": "Asset" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [
            { "id": "equity", "route": { "rule": "route.byField", "field": "Asset", "equals": "Equity" }, "passes": [
              { "id": "Pe", "pass": "match.naturalKey", "key": ["Quantity"] }
            ] },
            { "id": "bonds", "route": { "rule": "route.byField", "field": "Asset", "equals": "Bond" }, "passes": [
              { "id": "Pb", "pass": "match.naturalKey", "key": ["Quantity"] }
            ] }
          ]
        }
        """;
}
