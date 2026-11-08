using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class ProfileWriterTests
{
    public static TheoryData<string> CorpusIds => [.. ProfileCorpus.Profiles.Keys];

    [Fact]
    public void The_corpus_covers_every_project_that_holds_a_profile()
    {
        var projects = ProfileCorpus.Profiles.Keys.Select(id => id[..id.IndexOf('/', StringComparison.Ordinal)]).ToHashSet(StringComparer.Ordinal);

        Assert.True(ProfileCorpus.Profiles.Count >= 40, $"Found {ProfileCorpus.Profiles.Count} profiles.");
        Assert.Superset(
            new HashSet<string>(["RecDiffy.Tests", "RecDiffy.Excel.Tests", "RecDiffy.Samples", "RecDiffy.AotHost", "RecDiffy.Benchmarks"], StringComparer.Ordinal),
            projects);
    }

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_repository_profile_round_trips(string id)
    {
        var document = ProfileDocument.Parse(ProfileCorpus.Profiles[id]);

        var written = document.ToJson();
        var reread = ProfileDocument.Parse(written);

        Assert.Equal(ProfileShape.Of(document), ProfileShape.Of(reread));
        Assert.Equal(written, reread.ToJson());
        Assert.Equal(written, document.ToJson());
        Assert.Equal(ProfileShape.Of(document), ProfileShape.Of(ProfileDocument.Parse(document.ToJson(indented: false))));
        Assert.Equal(Compiled(document), Compiled(reread));
    }

    [Fact]
    public void Every_key_the_parser_reads_survives_a_round_trip()
    {
        var document = ProfileDocument.Parse(EveryKey);
        var reread = ProfileDocument.Parse(document.ToJson());

        Assert.Equal(ProfileShape.Of(document), ProfileShape.Of(reread));
        Assert.Equal(Compiled(document), Compiled(reread));
        Assert.StartsWith("hash:", Compiled(document), StringComparison.Ordinal);
    }

    [Fact]
    public void The_written_form_of_every_key_matches_its_golden_file()
    {
        var golden = Path.Combine(ProfileCorpus.Source, "RecDiffy.Tests", "Goldens", "every-key.profile.json");

        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), ProfileDocument.Parse(EveryKey).ToJson() + "\n");
    }

    [Fact]
    public void Output_uses_newline_line_endings_and_keeps_decimal_scale()
    {
        var written = ProfileDocument.Parse(EveryKey).ToJson();

        Assert.DoesNotContain('\r', written);
        Assert.Contains("\"absolute\": 1.20", written, StringComparison.Ordinal);
        Assert.Contains("\"relative\": 0.00010", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_output_has_no_whitespace_between_tokens()
    {
        var compact = ProfileDocument.Parse(EveryKey).ToJson(indented: false);

        Assert.DoesNotContain('\n', compact);
        Assert.StartsWith("{\"formatVersion\":1,\"profileId\":\"every-key\"", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void Defaults_are_written_out_so_a_saved_profile_does_not_depend_on_them()
    {
        var written = ProfileDocument.Parse(Minimal).ToJson();

        Assert.Contains("\"midpoint\": \"ToEven\"", written, StringComparison.Ordinal);
        Assert.Contains("\"maxNodesPerTarget\": 250000", written, StringComparison.Ordinal);
        Assert.Contains("\"scale\": 0", written, StringComparison.Ordinal);
    }

    // An empty side is not the same as a missing one: the compiler checks every side present, and an empty
    // consolidation side is part of the hash.
    [Fact]
    public void Empty_sides_are_kept()
    {
        var document = ProfileDocument.Parse(Minimal.Replace("\"paths\"", "\"normalization\": { \"left\": {} }, \"consolidation\": { \"right\": {} }, \"paths\"", StringComparison.Ordinal));
        var written = document.ToJson();

        Assert.Contains("\"left\": {}", written, StringComparison.Ordinal);
        Assert.Contains("\"right\": {}", written, StringComparison.Ordinal);
        Assert.Equal(ProfileShape.Of(document), ProfileShape.Of(ProfileDocument.Parse(written)));
    }

    [Fact]
    public void A_days_tolerance_round_trips()
    {
        var document = ProfileDocument.Parse(Minimal.Replace("\"route\": { \"rule\": \"route.all\" }", "\"route\": { \"rule\": \"route.all\" }, \"diff\": { \"fields\": { \"Day\": { \"tolerance\": { \"days\": 2 } } } }", StringComparison.Ordinal));
        var written = document.ToJson();

        Assert.Contains("\"days\": 2", written, StringComparison.Ordinal);
        Assert.Equal(2, ProfileDocument.Parse(written).Paths[0].Diff!.Fields["Day"].Tolerance.Days);
        Assert.Equal(ProfileShape.Of(document), ProfileShape.Of(ProfileDocument.Parse(written)));
    }

    [Fact]
    public void A_tolerance_whose_kind_disagrees_with_its_values_is_refused()
    {
        var document = ProfileDocument.Parse(Minimal);
        var path = document.Paths[0] with
        {
            Diff = new DiffDeclaration(new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)
            {
                ["Amount"] = new(new ToleranceDeclaration("Absolute", null, null, null, null), null, null),
            }),
        };

        Assert.Throws<ArgumentException>(() => (document with { Paths = [path] }).ToJson());
    }

    [Fact]
    public void A_rule_argument_that_appears_twice_is_refused()
    {
        var document = ProfileDocument.Parse(Minimal);
        var clash = new RuleCall(
            "text.copy",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "A" },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["from"] = ["B"] },
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));
        var side = new NormalizationSide([clash], [], [], []);

        Assert.Throws<ArgumentException>(() => (document with { Normalization = new Dictionary<string, NormalizationSide>(StringComparer.Ordinal) { ["left"] = side } }).ToJson());
    }

    [Fact]
    public void A_null_document_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ProfileWriter.ToJson(null!));
    }

    // The compiled hash when the profile compiles, otherwise its errors, so a round trip keeps either outcome.
    private static string Compiled(ProfileDocument document)
    {
        try
        {
            return "hash:" + Reconciler.Compile(document, RuleRegistry.CreateCore().Let(TradingRulePack.AddTo)).Hash;
        }
        catch (ProfileCompileException exception)
        {
            return "errors:" + string.Join(" / ", exception.Errors.Select(error => $"{error.Code}: {error.Message}"));
        }
    }

    private const string Minimal = """
        {
          "formatVersion": 1,
          "profileId": "minimal",
          "version": "1.0.0",
          "fields": [ { "name": "Amount", "type": "Decimal", "role": "Economic" }, { "name": "Day", "type": "Date", "role": "Economic" } ],
          "paths": [ { "id": "all", "route": { "rule": "route.all" } } ]
        }
        """;

    // Uses every key ProfileParser reads, with values that differ from the parser's defaults where it has one.
    private const string EveryKey = """
        {
          "formatVersion": 1,
          "profileId": "every-key",
          "version": "2.3.4",
          "rounding": { "midpoint": "AwayFromZero" },
          "sources": { "right": { "rowKey": "Cpty_Report_ID" }, "left": { "rowKey": "Internal_Trade_ID" } },
          "fields": [
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "Instrument", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Uti", "type": "Identifier", "role": "Identity" },
            { "name": "Action", "type": "Symbol", "role": "Lifecycle" },
            { "name": "Target", "type": "Text", "role": "Lifecycle" },
            { "name": "Book", "type": "Symbol", "role": "Lifecycle" },
            { "name": "Link", "type": "Text", "role": "Package" },
            { "name": "Leg", "type": "Symbol", "role": "Package" },
            { "name": "Note", "type": "Text", "role": "Text", "scale": 3 }
          ],
          "normalization": {
            "right": {
              "identify": [
                { "rule": "party.resolveLei", "to": "Counterparty", "from": "Counterparty_LEI" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass", "map": { "EQ": "Equity", "BD": "Bond" }, "passthrough": false },
                { "rule": "identifier.map", "from": "Isin", "to": "Instrument", "scheme": "ISIN", "scope": "global" },
                { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "UTI" },
                { "rule": "symbol.map", "from": "Event", "to": "Action" },
                { "rule": "text.copy", "from": "Prior", "to": "Target" },
                { "rule": "symbol.map", "from": "Desk", "to": "Book" },
                { "rule": "text.copy", "from": "Parent", "to": "Link" },
                { "rule": "symbol.map", "from": "LegRole", "to": "Leg" },
                { "rule": "text.copy", "from": "Comment", "to": "Note" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formats": ["dd/MM/yyyy", "yyyy-MM-dd"] },
                { "rule": "price.from32nds", "from": "Px", "to": "Price", "when": { "field": "Convention", "equals": "32NDS" } },
                { "rule": "decimal.parse", "from": "Px", "to": "Price", "when": { "field": "Convention", "notEquals": "32NDS" } },
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Accrued_Int", "to": "Accrued", "when": { "field": "AssetClass", "in": ["Bond", "Swap"] } },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir_Ind", "quantity": "Qty", "invertWhen": "DEALER", "perspective": "View", "asset": "AssetClass" }
              ],
              "validate": [
                { "rule": "required.present", "fields": ["Counterparty", "TradeDate"] }
              ]
            },
            "left": {
              "identify": [
                { "rule": "party.resolveAlias", "from": "Cpty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "identifier.map", "from": "Isin", "to": "Instrument", "scheme": "ISIN" },
                { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "UTI" },
                { "rule": "symbol.map", "from": "Event", "to": "Action" },
                { "rule": "text.copy", "from": "Prior", "to": "Target" },
                { "rule": "symbol.map", "from": "Desk", "to": "Book" },
                { "rule": "text.copy", "from": "Parent", "to": "Link" },
                { "rule": "symbol.map", "from": "LegRole", "to": "Leg" },
                { "rule": "text.copy", "from": "Comment", "to": "Note" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Accrued_Int", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ]
            }
          },
          "consolidation": {
            "right": { "lifecycle": { "action": "Action", "target": "Target", "scope": "Book", "corrPointsAt": "NEW" } },
            "left": { "packages": [
              { "link": "Link", "role": "Leg", "requiredRoles": ["PAY", "REC"],
                "aggregate": { "Quantity": "Same", "NetCash": "Sum", "Price": "FromRole(PAY)", "Accrued": "DifferenceByRole(PAY,REC)", "Fees": "DifferenceByRole(PAY)" } },
              { "link": "Link", "role": "Leg" }
            ] }
          },
          "paths": [
            { "id": "bonds", "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Bond" },
              "passes": [
                { "id": "P1", "pass": "match.sharedRef", "key": ["Uti"] },
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"], "maxGroupSize": 12 },
                { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] }
              ],
              "diff": { "default": "exact", "fields": {
                "Price": { "tolerance": { "absolute": "1.20", "when": { "field": "AssetClass", "equals": "Bond" } } },
                "Quantity": { "tolerance": { "relative": 0.00010 } },
                "Accrued": { "tolerance": { "absolute": 1.25, "relative": 0.001 }, "attributed": "trading.proRataToQuantity", "applicableWhen": { "field": "AssetClass", "in": ["Bond", "Swap"] } },
                "NetCash": { "tolerance": { "attributed": "trading.netCash" } },
                "Fees": { "applicableWhen": { "field": "AssetClass", "notEquals": "Swap" } },
                "TradeDate": { "tolerance": { "when": { "field": "AssetClass", "equals": "Bond" } } }
              } } },
            { "id": "rest", "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Equity" },
              "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] } ],
              "diff": {} }
          ],
          "limits": { "maxGroupSize": 12, "maxBlockItems": 20, "maxTargetsPerComponent": 6, "maxNodesPerTarget": 100000 }
        }
        """;
}
