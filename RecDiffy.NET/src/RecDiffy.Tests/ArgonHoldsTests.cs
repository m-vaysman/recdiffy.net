using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #116 "Untested but holds": the claims the sniff left without a committed test, other than the ones the #43 test-gaps
// work covers. Each test pins behaviour that holds today.
public class ArgonHoldsTests
{
    private const string GroupProfile = """
        {
          "formatVersion": 1,
          "profileId": "argon-holds-group-sum",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty"], "conserve": ["Quantity"] } ]
          } ]
        }
        """;

    private const string LifecycleProfile = """
        {
          "formatVersion": 1,
          "profileId": "argon-holds-lifecycle",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" }, { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "text.copy", "from": "Target", "to": "EventTarget" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } } },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Allocation> Allocations = new SourceMap<Allocation>()
        .Text("Id", row => row.Id).Text("Cpty", row => row.Cpty).Text("Qty", row => row.Qty).Text("Cash", row => row.Cash);

    private static readonly SourceMap<Event> Events = new SourceMap<Event>()
        .Text("Id", row => row.Id).Text("Cpty", row => row.Cpty).Text("Ref", row => row.Ref)
        .Text("Action", row => row.Action).Text("Target", row => row.Target).Text("Qty", row => row.Qty);

    // Item 2: summing a group writes its projection, not its members. Twenty blocks on eight workers: each projection
    // holds the block's totals, and each allocation's canonical row still holds its own values.
    [Fact]
    public void Each_allocation_keeps_its_own_values_after_its_group_is_summed_on_eight_workers()
    {
        var blocks = Enumerable.Range(0, 20).Select(block => "C" + block.ToString("00", CultureInfo.InvariantCulture)).ToArray();
        Allocation[] left = [.. blocks.SelectMany(block => new[] { new Allocation(block + "-A1", block, "50", "500"), new Allocation(block + "-A2", block, "50", "400") })];
        Allocation[] right = [.. blocks.Select(block => new Allocation(block + "-B1", block, "100", "900"))];

        var result = RunGroups(left, right, new RunOptions(8));

        Assert.Equal(20, result.Entries.Count(entry => entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact }));
        Assert.All(result.Entries, entry => Assert.Equal((100m, 900m), Values(result.Projection(entry.Id, Side.Left))));
        Assert.All(result.Rows, row => Assert.Equal(Expected(row.Row.RowKey), Values(result.Canonical(row.Row))));
    }

    // Item 2: an identical duplicate collapses into one allocation, so the block conserves 50 + 50. All three left rows
    // are reported, matched in one entry, and each keeps the values it was read with.
    [Fact]
    public void An_identical_duplicate_in_a_summed_group_keeps_its_own_values()
    {
        var result = RunGroups(
            [new("A1", "JPM", "50", "500"), new("A1", "JPM", "50", "500"), new("A2", "JPM", "50", "400")],
            [new("B1", "JPM", "100", "900")],
            new RunOptions(1));

        var lefts = result.Rows.Where(row => row.Row.Source == "left").ToArray();
        Assert.Equal(["A1", "A1", "A2"], lefts.Select(row => row.Row.RowKey).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.All(lefts, row => Assert.Equal(Disposition.Matched, row.Disposition));
        Assert.Single(lefts.Select(row => row.EntryId).Distinct());
        Assert.All(lefts, row => Assert.Equal(Expected(row.Row.RowKey), Values(result.Canonical(row.Row))));
        Assert.Equal((100m, 900m), Values(result.Projection(Assert.Single(result.Entries).Id, Side.Left)));
    }

    // Item 4: the machine's culture does not reach the trading rules. Each side declares en-US. On tr-TR, "i".ToUpper() is
    // "İ", so a culture-sensitive upper-casing would lose "receivefixed" and "payfixed". On de-DE, the comma is the
    // decimal separator, so a culture-sensitive read would take "1,000.50" as a different number.
    [Theory]
    [InlineData("tr-TR", "receivefixed", "100", "REC_FIXED")]
    [InlineData("tr-TR", "payfixed", "100", "PAY_FIXED")]
    [InlineData("tr-TR", "bot", "100", "BUY")]
    [InlineData("tr-TR", "sld", "100", "SELL")]
    [InlineData("de-DE", "", "-1,000.50", "SELL")]
    [InlineData("de-DE", "", "1,000.50", "BUY")]
    public void Side_resolve_reads_the_same_on_a_hostile_server(string server, string code, string quantity, string side) =>
        OnServer(server, () =>
        {
            var row = Apply("side.resolve", Strings(("code", "Dir"), ("quantity", "Qty")), ("Dir", code), ("Qty", quantity));

            Assert.Equal(side, row.Display("Side"));
            Assert.Empty(row.Diagnostics);
        });

    [Theory]
    [InlineData("tr-TR", "101-08", "101.25")]
    [InlineData("de-DE", "101-08", "101.25")]
    [InlineData("tr-TR", "99-16+", "99.5156")]
    [InlineData("de-DE", "99-16+", "99.5156")]
    public void Price_from_32nds_reads_the_same_on_a_hostile_server(string server, string text, string price) =>
        OnServer(server, () =>
        {
            var row = Apply("price.from32nds", Strings(("from", "Px"), ("to", "Parsed")), ("Px", text));

            Assert.Equal(decimal.Parse(price, CultureInfo.InvariantCulture), row.Values["Parsed"].Number);
            Assert.Empty(row.Diagnostics);
        });

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Quantity_absolute_reads_the_same_on_a_hostile_server(string server) =>
        OnServer(server, () =>
        {
            var row = Apply("quantity.absolute", Strings(("from", "Qty"), ("to", "Quantity")), ("Qty", "-1,000.50"));

            Assert.Equal(1000.50m, row.Values["Quantity"].Number);
            Assert.Empty(row.Diagnostics);
        });

    // Item 7: one event of a NEW, CANC, CORR chain arrives twice, identically. The copies collapse into one event, the
    // chain consolidates as it would with one copy, and both copies are reported with the event's disposition.
    [Theory]
    [InlineData("E1")]
    [InlineData("E2")]
    [InlineData("E3")]
    public void An_identical_duplicate_in_a_lifecycle_chain_reports_every_copy(string doubled)
    {
        Event[] chain =
        [
            new("E1", "ACME", "T1", "NEW", "", "100"),
            new("E2", "ACME", "T1", "CANC", "E1", "100"),
            new("E3", "ACME", "T1", "CORR", "E2", "200"),
        ];
        Event[] right = [.. chain, chain.Single(row => row.Id == doubled)];
        string[] expected =
        [
            "left:L1=Matched/",
            .. right.Select(row => row.Id switch
            {
                "E1" => "right:E1=Suppressed/Superseded",
                "E2" => "right:E2=Suppressed/CancelEvent",
                _ => "right:E3=Matched/",
            }),
        ];

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(LifecycleProfile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [new Event("L1", "ACME", "T1", "", "", "200")], Events),
            RecordSources.FromObjects("right", right, Events),
            ReferenceDataSet.Empty);

        Assert.Equal(expected.Order(StringComparer.Ordinal), result.Rows.Select(row => $"{row.Row.Source}:{row.Row.RowKey}={row.Disposition}/{row.Suppression}").Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Single(result.Rows.Where(row => row.Disposition == Disposition.Matched).Select(row => row.EntryId).Distinct());
        Assert.Equal(FieldOutcome.Exact, Assert.Single(result.Entries, entry => entry.Alignment == Alignment.Matched).Outcome);
    }

    private static ReconciliationResult RunGroups(Allocation[] left, Allocation[] right, RunOptions options) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(GroupProfile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Allocations),
            RecordSources.FromObjects("right", right, Allocations),
            ReferenceDataSet.Empty,
            options);

    private static (decimal Quantity, decimal NetCash) Values(CanonicalRow row) => (row["Quantity"].Number, row["NetCash"].Number);

    private static (decimal Quantity, decimal NetCash) Expected(string rowKey) => rowKey[^2..] switch
    {
        "A1" => (50m, 500m),
        "A2" => (50m, 400m),
        _ => (100m, 900m),
    };

    private static RowContext Apply(string ruleId, Dictionary<string, string> strings, params (string Column, object? Value)[] cells)
    {
        var row = RowContext.Create("L1", Side.Left, cells.ToDictionary(cell => cell.Column, cell => cell.Value, StringComparer.Ordinal));
        CultureProfile.Rules("en-US").Build(ruleId, strings).Apply(ref row);
        return row;
    }

    private static Dictionary<string, string> Strings(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    private static void OnServer(string culture, Action test)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    public sealed record Allocation(string Id, string Cpty, string Qty, string Cash);

    public sealed record Event(string Id, string Cpty, string Ref, string Action, string Target, string Qty);
}
