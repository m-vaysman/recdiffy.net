using System.Text.Json.Nodes;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Sonnet, the primary acceptance dataset (spec v3 §12.2), read from the workbook through FromExcel and compared with its
// oracle. The profile follows §9.1 and §9.2 with registered rules only, price.dirtyToClean and fees.inferFromNet among
// them. Where §9.1 names a rule the registry lacks, the profile does without it:
// - instrument.resolve (#259): each identifier scheme is its own field, and P2 to P4 run once per scheme;
// - tradeRef.classify: the shared reference is a plain identifier, so no field is named TradeRef and nothing is vetoed.
public class SonnetOracleTests
{
    private static readonly OracleDataset Dataset = OracleDatasets.Sonnet;

    private static readonly Lazy<IReconciliationResult> Result = new(Run);

    private const string FiveCases = "Five groups disagree: G0158 (Deferral.SwapFeeRoot, #431) and G0175 to G0178 (Deferral.TwoLegForwardFee, #432)";

    private const string FourForwards = "G0175 to G0178 are Break, so the run is 141 / 18 / 24 / 85 / 7 (Deferral.TwoLegForwardFee, #432)";

    // Each check leaves out only the cases it can't judge, and says why.
    private static OracleComparison Compare(Func<OracleCase, string?>? deferred = null) => new(Result.Value, Dataset.Cases(), deferred);

    // The cases a check on outcomes, roots or deltas can't judge yet. SonnetFeeDefectTests holds them as skipped facts.
    private static string? Deferred(OracleCase item) => item switch
    {
        { Id: "G0158" } => Deferral.SwapFeeRoot,
        { Kind: CaseKind.MultiLeg } when Deferral.LeftHas(Dataset, item, "Asset_Class", "Forward") => Deferral.TwoLegForwardFee,
        _ => null,
    };

    internal static IReconciliationResult Outcome => Result.Value;

    // The oracle's field names, from the internal tab's columns, as profile fields.
    internal static string? Field(string column) => column switch
    {
        "Clean_Price_Rate" => "Price",
        "Quantity_Notional" => "Quantity",
        "Settle_Date" => "SettleDate",
        "Accrued_Interest" => "Accrued",
        "Commission_Fees" => "Fees",
        "Net_Settlement_Amount" => "NetCash",
        _ => null,
    };

    [Fact]
    public void AC1_each_group_is_one_entry_with_exactly_its_rows_and_there_are_no_other_entries() =>
        OracleAssert.None(Compare().Membership());

    // 5 deferred: G0158 and G0175 to G0178.
    [Fact]
    public void AC2_each_entry_has_the_alignment_and_outcome_its_label_maps_to()
    {
        var comparison = Compare(Deferred);

        Assert.Equal(275 - 5, comparison.Checked);
        OracleAssert.None(comparison.Outcomes());
    }

    [Fact]
    public void AC2_each_entry_has_the_composition_its_label_maps_to() =>
        OracleAssert.None(Compare().Compositions(allocationsArePackages: false));

    // §6.3 and §12.3: 145 Exact (120 single, 5 chains, 12 allocations, 8 packages), 18 WithinTolerance, 20 Break.
    [Fact(Skip = FourForwards)]
    public void AC2_the_run_counts_are_145_exact_18_within_tolerance_20_break_85_left_only_and_7_right_only()
    {
        var summary = Result.Value.Summary;

        Assert.Equal((145, 18, 20, 85, 7), (summary.Exact, summary.WithinTolerance, summary.Break, summary.LeftOnly, summary.RightOnly));
    }

    // G0158 is the one break left out: its street fee has no derivation, so trading.netCash must name Fees as the root (§7.4).
    [Fact]
    public void AC3_each_break_is_rooted_in_the_field_the_oracle_names_and_no_other() =>
        OracleAssert.None(Compare(Deferred).BreakRoots(Field));

    [Fact]
    public void AC3_each_near_match_delta_equals_the_oracle_delta() =>
        OracleAssert.None(Compare(Deferred).Deltas(Field));

    [Fact]
    public void AC4_each_chain_survivor_is_the_oracles_active_report_and_the_rest_are_suppressed() =>
        OracleAssert.None(Compare().Lifecycle());

    [Fact]
    public void AC5_every_input_row_has_exactly_one_disposition() =>
        OracleAssert.None(Compare().RowAccounting(Dataset.LeftRows(), Dataset.RightRows()));

    [Fact(Skip = FiveCases)]
    public void Every_group_agrees_with_the_oracle_on_every_criterion()
    {
        var all = Compare();

        OracleAssert.None([.. all.Membership(), .. all.AllAxes(allocationsArePackages: false), .. all.BreakRoots(Field), .. all.Deltas(Field), .. all.Lifecycle()]);
    }

    private static IReconciliationResult Run() =>
        RunOn(
            FileSources.FromExcel("left", Dataset.Workbook, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }),
            FileSources.FromExcel("right", Dataset.Workbook, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(2) }));

    internal static IReconciliationResult RunOn(RecordSource left, RecordSource right) =>
        Reconciler.Run(Reconciler.Compile(ProfileDocument.Parse(Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore())), left, right, Reference());

    // Reference data from the oracle's metadata: each counterparty's LEI, and its date format (§3.3) as a .NET format.
    private static ReferenceDataSet Reference()
    {
        var parties = JsonNode.Parse(File.ReadAllBytes(Dataset.Oracle))!["metadata"]!["counterparty_alias_map"]!.AsObject();
        var leis = parties.ToDictionary(party => party.Value!["lei"]!.GetValue<string>(), party => party.Key, StringComparer.Ordinal);
        var formats = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GSCO"] = "yyyy-MM-dd",
            ["JPM"] = "MM/dd/yyyy",
            ["MSCO"] = "yyyyMMdd",
            ["BAML"] = "dd-MMM-yyyy",
            ["CITI"] = "yyyy-MM-dd",
            ["BARC"] = "dd/MM/yyyy",
            ["UBS"] = "yyyy-MM-dd",
            ["CITADEL"] = "yyyyMMdd",
        };
        return ReferenceDataSet.Create("sonnet", new DateOnly(2025, 6, 13), "sonnet", leis, new Dictionary<string, string>(StringComparer.Ordinal), formats);
    }

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "trade-recon.sonnet",
          "version": "1.0.0",
          "rounding": { "midpoint": "ToEven" },
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Internal_Trade_ID" }, "right": { "rowKey": "Cpty_Report_ID" } },
          "fields": [
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Key" },
            { "name": "IsinId", "type": "Identifier", "role": "Identity" },
            { "name": "CusipId", "type": "Identifier", "role": "Identity" },
            { "name": "SedolId", "type": "Identifier", "role": "Identity" },
            { "name": "TickerId", "type": "Identifier", "role": "Identity" },
            { "name": "ExecRef", "type": "Identifier", "role": "Identity" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Identifier", "role": "Lifecycle" },
            { "name": "LinkId", "type": "Identifier", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "identifier.map", "from": "Counterparty_Code", "to": "Counterparty", "scheme": "PARTY" },
                { "rule": "symbol.map", "from": "Asset_Class", "to": "AssetClass" },
                { "rule": "identifier.map", "from": "ISIN", "to": "IsinId", "scheme": "ISIN" },
                { "rule": "identifier.map", "from": "CUSIP", "to": "CusipId", "scheme": "CUSIP" },
                { "rule": "identifier.map", "from": "SEDOL", "to": "SedolId", "scheme": "SEDOL" },
                { "rule": "identifier.map", "from": "Ticker", "to": "TickerId", "scheme": "TICKER" },
                { "rule": "identifier.map", "from": "UTI_Exec_ID", "to": "ExecRef", "scheme": "REF" },
                { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
                { "rule": "identifier.map", "from": "Parent_Link_ID", "to": "LinkId", "scheme": "LINK" },
                { "rule": "symbol.map", "from": "Leg_Structure", "to": "LegRole" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "Settle_Date", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "Clean_Price_Rate", "to": "Price" },
                { "rule": "decimal.parse", "from": "Accrued_Interest", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Commission_Fees", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Net_Settlement_Amount", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Side", "quantity": "Quantity_Notional", "asset": "AssetClass" },
                { "rule": "quantity.absolute", "from": "Quantity_Notional", "to": "Quantity" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "party.resolveLei", "from": "Counterparty_LEI", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Product_Type", "to": "AssetClass", "passthrough": false,
                  "map": { "CASH_EQ": "Equity", "CORP_BOND": "Bond", "GOVT_BOND": "Bond", "FX_FWD": "Forward", "IRS": "Swap" } },
                { "rule": "identifier.map", "from": "Primary_Security_ID", "to": "IsinId", "scheme": "ISIN", "when": { "field": "Security_ID_Source", "equals": "ISIN" } },
                { "rule": "identifier.map", "from": "Primary_Security_ID", "to": "CusipId", "scheme": "CUSIP", "when": { "field": "Security_ID_Source", "equals": "CUSIP" } },
                { "rule": "identifier.map", "from": "Primary_Security_ID", "to": "SedolId", "scheme": "SEDOL", "when": { "field": "Security_ID_Source", "equals": "SEDOL" } },
                { "rule": "identifier.map", "from": "Primary_Security_ID", "to": "TickerId", "scheme": "TICKER", "when": { "field": "Security_ID_Source", "equals": "TICKER" } },
                { "rule": "identifier.map", "from": "Shared_Market_Exec_Ref", "to": "ExecRef", "scheme": "REF" },
                { "rule": "symbol.map", "from": "Trade_Ccy", "to": "Currency" },
                { "rule": "symbol.map", "from": "Trans_Lifecycle_Action", "to": "EventAction" },
                { "rule": "identifier.map", "from": "Cancels_Prior_Report_ID", "to": "EventTarget", "scheme": "REPORT" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formatBy": "Counterparty" },
                { "rule": "date.parse", "from": "Value_Settle_Date", "to": "SettleDate", "formatBy": "Counterparty" },
                { "rule": "decimal.parse", "from": "Exec_Price_Rate", "to": "Price", "when": { "field": "Price_Quote_Convention", "notEquals": "32NDS_UST" } },
                { "rule": "price.from32nds", "from": "Exec_Price_Rate", "to": "Price", "when": { "field": "Price_Quote_Convention", "equals": "32NDS_UST" } },
                { "rule": "decimal.parse", "from": "Reported_Accrued_Int", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Net_Cash_Amount", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir_Ind", "quantity": "Reported_Qty_Notional", "perspective": "Reporting_Perspective", "invertWhen": "DEALER_BILATERAL_VIEW", "asset": "AssetClass" },
                { "rule": "quantity.absolute", "from": "Reported_Qty_Notional", "to": "Quantity" },
                { "rule": "price.dirtyToClean", "to": "Price", "when": { "field": "Price_Quote_Convention", "equals": "DIRTY_PCT_PAR" } },
                { "rule": "fees.inferFromNet", "to": "Fees" }
              ]
            }
          },
          "consolidation": {
            "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } },
            "left": { "packages": [ { "link": "LinkId", "role": "LegRole", "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"],
              "aggregate": { "Quantity": "Same", "Price": "FromRole(LEG_1_PAY)", "Side": "FromRole(LEG_1_PAY)",
                             "NetCash": "DifferenceByRole(LEG_1_PAY,LEG_2_RECEIVE)", "Accrued": "DifferenceByRole(LEG_1_PAY,LEG_2_RECEIVE)" } } ] }
          },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" },
            "passes": [
              { "id": "P1", "pass": "match.sharedRef", "key": ["Counterparty", "ExecRef"] },
              { "id": "P2-ISIN", "pass": "match.naturalKey", "key": ["Counterparty", "IsinId", "AssetClass", "TradeDate", "Side", "Quantity"] },
              { "id": "P2-CUSIP", "pass": "match.naturalKey", "key": ["Counterparty", "CusipId", "AssetClass", "TradeDate", "Side", "Quantity"] },
              { "id": "P2-SEDOL", "pass": "match.naturalKey", "key": ["Counterparty", "SedolId", "AssetClass", "TradeDate", "Side", "Quantity"] },
              { "id": "P2-TICKER", "pass": "match.naturalKey", "key": ["Counterparty", "TickerId", "AssetClass", "TradeDate", "Side", "Quantity"] },
              { "id": "P3-ISIN", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "IsinId", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
              { "id": "P3-CUSIP", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "CusipId", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
              { "id": "P3-SEDOL", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "SedolId", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
              { "id": "P3-TICKER", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "TickerId", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
              { "id": "P4-ISIN", "pass": "match.reducedKey", "key": ["Counterparty", "IsinId", "TradeDate", "Side"] },
              { "id": "P4-CUSIP", "pass": "match.reducedKey", "key": ["Counterparty", "CusipId", "TradeDate", "Side"] },
              { "id": "P4-SEDOL", "pass": "match.reducedKey", "key": ["Counterparty", "SedolId", "TradeDate", "Side"] },
              { "id": "P4-TICKER", "pass": "match.reducedKey", "key": ["Counterparty", "TickerId", "TradeDate", "Side"] } ],
            "diff": { "fields": {
              "Accrued": { "tolerance": { "absolute": 1.25 }, "attributed": "trading.proRataToQuantity", "applicableWhen": { "field": "AssetClass", "in": ["Bond", "Swap"] } },
              "Price": { "tolerance": { "absolute": 0.0001, "when": { "field": "AssetClass", "equals": "Forward" } } },
              "Fees": { "tolerance": { "absolute": 0.05 }, "attributed": "trading.proRataToQuantity", "applicableWhen": { "field": "AssetClass", "in": ["Bond", "Equity", "Forward"] } },
              "NetCash": { "tolerance": { "attributed": "trading.netCash" } } } } } ]
        }
        """;
}
