using RecDiffy.RulePacks.Trading;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class IntakeTests
{
    private static readonly ReferenceDataSet Reference = ReferenceDataSet.Create(
        "sample",
        new DateOnly(2025, 6, 11),
        "hash",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["EL495T1V227XABYD2Q18"] = "BAML" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["BOFA_MLPF&S"] = "BAML" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["BARC"] = "dd/MM/yyyy" });

    [Fact]
    public void Does_not_treat_a_separator_inside_a_cell_as_another_cell()
    {
        var split = new RawRecord(new SourceRef("left", "1", 0), new Dictionary<string, object?> { ["A"] = "x\u001fB=y" });
        var pair = new RawRecord(new SourceRef("left", "2", 0), new Dictionary<string, object?> { ["A"] = "x", ["B"] = "y" });

        Assert.NotEqual(split.Fingerprint(), pair.Fingerprint());
    }

    [Fact]
    public void Collapses_identical_duplicates_and_quarantines_conflicts()
    {
        var profile = Profile();
        var rows = new[]
        {
            Row("A", "1"),
            Row("A", "1"),
            Row("B", "1"),
            Row("B", "2"),
        };
        var (left, _) = Intake.Normalize(profile, rows, [], Reference);

        var collapsed = Assert.Single(left, row => row.Members[0].RowKey == "A");
        Assert.Equal(2, collapsed.Members.Count);
        Assert.Contains(collapsed.Context.Diagnostics, diagnostic => diagnostic.Code == "DuplicateRowCollapsed");
        Assert.Equal(2, left.Count(row => row.Quarantined && row.Context.Diagnostics.Any(diagnostic => diagnostic.Code == "DuplicateConflict")));
    }

    [Fact]
    public void Parses_treasury_32nds_and_a_signed_sell()
    {
        var profile = TradeProfile();
        var raw = new RawRecord(
            new SourceRef("right", "BA-18777889", 0),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Exec_Price_Rate"] = "96-02+",
                ["Dir_Ind"] = "S",
                ["Reported_Qty_Notional"] = -3_000_000m,
                ["Asset_Class"] = "Bond",
                ["Counterparty_LEI"] = "EL495T1V227XABYD2Q18",
            });
        var (_, right) = Intake.Normalize(profile, [], [raw], Reference);
        var row = Assert.Single(right);
        Assert.Equal(96.078125m, row.Context.Values["Price"].Number);
        Assert.Equal("SELL", row.Context.Display("Side"));
        Assert.Equal(3_000_000m, row.Context.Values["Quantity"].Number);
        Assert.Equal("BAML", row.Context.Display("Counterparty"));
    }

    [Fact]
    public void Quarantines_buy_with_a_negative_quantity()
    {
        var profile = TradeProfile();
        var raw = new RawRecord(
            new SourceRef("right", "X", 0),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Dir_Ind"] = "B",
                ["Reported_Qty_Notional"] = -500_000m,
                ["Asset_Class"] = "Equity",
                ["Counterparty_LEI"] = "EL495T1V227XABYD2Q18",
            });
        var (_, right) = Intake.Normalize(profile, [], [raw], Reference);
        Assert.Contains(Assert.Single(right).Context.Diagnostics, diagnostic => diagnostic.Code == "SideSignConflict");
    }

    private static RawRecord Row(string key, string quantity) => new(
        new SourceRef("left", key, 0),
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["Quantity"] = quantity });

    private static CompiledProfile Profile() => Reconciler.Compile(ProfileDocument.Parse("""
        {
          "formatVersion": 1,
          "profileId": "dup",
          "version": "1.0.0",
          "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
          "normalization": { "left": { "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] } },
          "paths": [ { "id": "only", "route": { "rule": "route.all" } } ]
        }
        """), RuleRegistry.CreateCore());

    private static CompiledProfile TradeProfile() => Reconciler.Compile(ProfileDocument.Parse("""
        {
          "formatVersion": 1,
          "profileId": "trade",
          "version": "1.0.0",
          "fields": [
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" }
          ],
          "normalization": { "right": {
            "identify": [
              { "rule": "party.resolveLei", "from": "Counterparty_LEI", "to": "Counterparty" },
              { "rule": "symbol.map", "from": "Asset_Class", "to": "AssetClass" }
            ],
            "parse": [
              { "rule": "price.from32nds", "from": "Exec_Price_Rate", "to": "Price", "when": { "field": "Exec_Price_Rate", "notEquals": "" } }
            ],
            "derive": [
              { "rule": "side.resolve", "code": "Dir_Ind", "quantity": "Reported_Qty_Notional", "asset": "AssetClass" },
              { "rule": "quantity.absolute", "from": "Reported_Qty_Notional", "to": "Quantity" }
            ]
          } },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" } } ]
        }
        """), RuleRegistry.CreateCore().Let(TradingRulePack.AddTo));
}

internal static class RegistryPipe
{
    public static RuleRegistry Let(this RuleRegistry registry, Func<RuleRegistry, RuleRegistry> add) => add(registry);
}
