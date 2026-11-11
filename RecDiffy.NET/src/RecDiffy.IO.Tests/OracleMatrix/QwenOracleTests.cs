using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Qwen 3.8, a secondary dataset (spec v3 §12.2): read through FromExcel and compared with its oracle, which §12.1 calls
// unverified. The profile is Sonnet's (§9.1) with Qwen's product codes, its ALLOCATION rows summed as packages, and the same
// stand-ins for the rules the registry lacks: one field and one P2 to P4 pass per identifier scheme (instrument.resolve,
// #259), no TradeRef veto (tradeRef.classify), DIRTY prices read as written (price.dirtyToClean), and no fees
// (fees.inferFromNet).
public class QwenOracleTests
{
    private static readonly OracleDataset Dataset = OracleDatasets.Qwen;

    private static readonly Lazy<ReconciliationResult> Result = new(Run);

    // §12.1 calls this dataset's membership unverified, and §9.2 gives no profile for its conventions. Its outcomes
    // disagree with the oracle for reasons outside the engine, listed in the PR for #39, and for #307 and #308.
    private const string Decoy = "Oracle note: P4's reduced key pairs this orphan with an alleged trade on the same counterparty, instrument, date and side";

    private const string Unverified = "Unverified (spec v3 §12.1): no §9.2 profile, missing rules (price.dirtyToClean, fees.inferFromNet), oracle notes in the PR for #39, and #307, #308, #309";

    private static OracleComparison Compare() => new(Result.Value, Dataset.Cases(), Deferred);

    private static string? Deferred(OracleCase item) => item switch
    {
        { Kind: CaseKind.MultiLeg } => "Qwen's legs carry different notionals, so Same fails with PackageInconsistent; §9.2 has no Qwen leg rule",
        { Id: "cancel_correct_lifecycle_chains[0]" or "cancel_correct_lifecycle_chains[2]" or "cancel_correct_lifecycle_chains[3]" } => Deferral.CancelRunsTradeRules,
        { Id: "unmatched_internal_orphans[1]" or "unmatched_internal_orphans[2]" or "unmatched_internal_orphans[31]" or "unmatched_counterparty_alleged_trades[3]" or "unmatched_counterparty_alleged_trades[5]" or "unmatched_counterparty_alleged_trades[6]" } => Decoy,
        _ => null,
    };

    // The oracle's field names as profile fields. Fees are not compared (no fees.inferFromNet), so a fee break is a
    // net cash break.
    private static string? Field(string name) => name switch
    {
        "reported_accrued_int" => "Accrued",
        "exec_price_rate" => "Price",
        "net_cash_amount" => "NetCash",
        "reported_qty_notional" => "Quantity",
        "value_settle_date" => "SettleDate",
        _ => null,
    };

    [Fact]
    public void AC1_each_case_is_one_entry_with_exactly_its_rows_and_there_are_no_other_entries() =>
        OracleAssert.None(Compare().Membership());

    [Fact(Skip = Unverified)]
    public void AC2_each_entry_has_the_alignment_composition_and_outcome_its_label_maps_to() =>
        OracleAssert.None(Compare().AllAxes(allocationsArePackages: true));

    [Fact]
    public void AC3_each_break_is_rooted_in_the_field_the_oracle_names_and_no_other() =>
        OracleAssert.None(Compare().BreakRoots(Field));

    [Fact]
    public void AC3_every_stated_difference_is_a_field_difference_with_the_oracle_delta() =>
        OracleAssert.None(Compare().Deltas(Field));

    [Fact]
    public void AC4_each_chain_survivor_is_the_oracles_active_report_and_the_rest_are_suppressed() =>
        OracleAssert.None(Compare().Lifecycle());

    [Fact]
    public void AC5_every_input_row_has_exactly_one_disposition() =>
        OracleAssert.None(Compare().RowAccounting(Dataset.LeftRows(), Dataset.RightRows()));

    [Fact]
    public void The_checks_cover_all_but_the_17_deferred_cases() =>
        Assert.Equal(Dataset.Cases().Count - 17, Compare().Checked);

    private static ReconciliationResult Run() =>
        RunOn(
            FileSources.FromExcel("left", Dataset.Workbook, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }),
            FileSources.FromExcel("right", Dataset.Workbook, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(2) }));

    internal static ReconciliationResult RunOn(RecordSource left, RecordSource right) =>
        Reconciler.Run(Reconciler.Compile(ProfileDocument.Parse(Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore())), left, right, Reference());

    // Reference data from the generator in response.md: each counterparty's two placeholder LEIs.
    private static ReferenceDataSet Reference()
    {
        var leis = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GSCO_LEI_001"] = "GSCO",
            ["GSCO_LEI_002"] = "GSCO",
            ["JPM_LEI_001"] = "JPM",
            ["JPM_LEI_002"] = "JPM",
            ["MSCO_LEI_001"] = "MSCO",
            ["MSCO_LEI_002"] = "MSCO",
            ["BAML_LEI_001"] = "BAML",
            ["BAML_LEI_002"] = "BAML",
            ["CITI_LEI_001"] = "CITI",
            ["CITI_LEI_002"] = "CITI",
            ["BARC_LEI_001"] = "BARC",
            ["BARC_LEI_002"] = "BARC",
            ["UBS_LEI_001"] = "UBS",
            ["UBS_LEI_002"] = "UBS",
            ["CITADEL_LEI_001"] = "CITADEL",
            ["CITADEL_LEI_002"] = "CITADEL",
        };
        var none = new Dictionary<string, string>(StringComparer.Ordinal);
        return ReferenceDataSet.Create("qwen-3.8", new DateOnly(2026, 6, 15), "qwen-3.8", leis, none, none);
    }

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "trade-recon.qwen-3.8",
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
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Identifier", "role": "Lifecycle" },
            { "name": "AllocationLink", "type": "Identifier", "role": "Package" },
            { "name": "LegLink", "type": "Identifier", "role": "Package" },
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
                { "rule": "identifier.map", "from": "Parent_Link_ID", "to": "AllocationLink", "scheme": "LINK", "when": { "field": "Leg_Structure", "in": ["ALLOCATION"] } },
                { "rule": "identifier.map", "from": "Parent_Link_ID", "to": "LegLink", "scheme": "LINK", "when": { "field": "Leg_Structure", "in": ["LEG_1_PAY", "LEG_2_RECEIVE"] } },
                { "rule": "symbol.map", "from": "Leg_Structure", "to": "LegRole" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "Settle_Date", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "Clean_Price_Rate", "to": "Price" },
                { "rule": "decimal.parse", "from": "Accrued_Interest", "to": "Accrued" },
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
                  "map": {"BOND": "Bond", "EQUITY": "Equity", "SWAP": "Swap", "FX_FORWARD": "Forward"} },
                { "rule": "identifier.map", "from": "Primary_Security_ID", "to": "IsinId", "scheme": "ISIN", "when": { "field": "Security_ID_Source", "equals": "ISIN" } },
                { "rule": "identifier.map", "from": "Primary_Security_ID", "to": "CusipId", "scheme": "CUSIP", "when": { "field": "Security_ID_Source", "equals": "CUSIP" } },
                { "rule": "identifier.map", "from": "Primary_Security_ID", "to": "SedolId", "scheme": "SEDOL", "when": { "field": "Security_ID_Source", "equals": "SEDOL" } },
                { "rule": "identifier.map", "from": "Primary_Security_ID", "to": "TickerId", "scheme": "TICKER", "when": { "field": "Security_ID_Source", "equals": "TICKER" } },
                { "rule": "identifier.map", "from": "Shared_Market_Exec_Ref", "to": "ExecRef", "scheme": "REF" },
                { "rule": "symbol.map", "from": "Trans_Lifecycle_Action", "to": "EventAction" },
                { "rule": "identifier.map", "from": "Cancels_Prior_Report_ID", "to": "EventTarget", "scheme": "REPORT" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "Value_Settle_Date", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "Exec_Price_Rate", "to": "Price", "when": { "field": "Price_Quote_Convention", "notEquals": "32NDS_UST" } },
                { "rule": "price.from32nds", "from": "Exec_Price_Rate", "to": "Price", "when": { "field": "Price_Quote_Convention", "equals": "32NDS_UST" } },
                { "rule": "decimal.parse", "from": "Reported_Accrued_Int", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Net_Cash_Amount", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir_Ind", "quantity": "Reported_Qty_Notional", "perspective": "Reporting_Perspective", "invertWhen": "DEALER_BILATERAL_VIEW", "asset": "AssetClass" },
                { "rule": "quantity.absolute", "from": "Reported_Qty_Notional", "to": "Quantity" }
              ]
            }
          },
          "consolidation": {
            "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } },
            "left": { "packages": [
              { "link": "AllocationLink", "role": "LegRole", "aggregate": { "Quantity": "Sum", "Accrued": "Sum", "NetCash": "Sum" } },
              { "link": "LegLink", "role": "LegRole", "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"],
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
              "NetCash": { "tolerance": { "attributed": "trading.netCash" } } } } } ]
        }
        """;
}
