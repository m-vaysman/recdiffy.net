using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

public class ResultExamplesTests
{
    private const string LedgerProfile = """
        {
          "formatVersion": 1,
          "profileId": "ledger-bank",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] }
          },
          "paths": [ { "id": "payments", "route": { "rule": "route.all" },
            "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": { "Amount": { "tolerance": { "absolute": 0.05 } } } } } ]
        }
        """;

    private const string WarehouseProfile = """
        {
          "formatVersion": 1,
          "profileId": "warehouse",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Bin" }, "right": { "rowKey": "Position" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "bins", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] },
            { "id": "sku", "pass": "match.naturalKey", "key": ["Sku"] }
          ] } ]
        }
        """;

    private const string TradeProfile = """
        {
          "formatVersion": 1,
          "profileId": "trade-events",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
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
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Payment> Payments = new SourceMap<Payment>()
        .Text("Id", row => row.Id).Text("Ref", row => row.Ref).Text("Amount", row => row.Amount);

    private static readonly SourceMap<Stock> Bins = new SourceMap<Stock>()
        .Text("Bin", row => row.Id).Text("Sku", row => row.Sku).Text("Quantity", row => row.Quantity);

    private static readonly SourceMap<Stock> Book = new SourceMap<Stock>()
        .Text("Position", row => row.Id).Text("Sku", row => row.Sku).Text("Quantity", row => row.Quantity);

    private static readonly SourceMap<TradeEvent> Events = new SourceMap<TradeEvent>()
        .Text("Id", row => row.Id).Text("Cpty", _ => "ACME").Text("Ref", row => row.Ref)
        .Text("Action", row => row.Action).Text("Target", row => row.Target).Text("Qty", row => row.Qty);

    private static readonly Payment[] Ledger =
    [
        new("L1", "A-1", "100.00"),
        new("L2", "A-2", "250.00"),
        new("L3", "A-3", "50.00"),
        new("L4", "A-4", ""),
        new("L5", "A-5", "40.00"),
        new("L6", "A-6", "n/a"),
        new("L7", "A-7", "60.00"),
    ];

    private static readonly Payment[] Bank =
    [
        new("B1", "A-1", "100.00"),
        new("B2", "A-2", "245.00"),
        new("B3", "A-3", "50.03"),
        new("B4", "A-4", "0.00"),
        new("B6", "A-6", "80.00"),
        new("B7", "A-7", "60.00"),
        new("B8", "A-7", "60.00"),
        new("B9", "A-9", "30.00"),
    ];

    [Fact]
    public void The_ledger_against_the_bank_gives_one_entry_per_decision()
    {
        var result = LedgerAgainstBank();

        Assert.Equal(
            """
            Matched    OneToOne  Exact            L1 | B1
            Matched    OneToOne  Break            L2 | B2    Amount: 250.00 vs 245.00, delta -5.00
            Matched    OneToOne  WithinTolerance  L3 | B3    Amount: 50.00 vs 50.03, delta 0.03
            Matched    OneToOne  Break            L4 | B4    Amount: (blank) vs 0.00, delta 0
            Ambiguous  None      NotCompared      L7 | B7 B8
            LeftOnly   None      NotCompared      L5 | -
            RightOnly  None      NotCompared      - | B6
            RightOnly  None      NotCompared      - | B9
            """,
            Entries(result),
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void The_ledger_against_the_bank_gives_every_source_row_one_disposition()
    {
        var result = LedgerAgainstBank();

        Assert.Equal(
            """
            left:L1    Matched      Valid
            left:L2    Matched      Valid
            left:L3    Matched      Valid
            left:L4    Matched      Valid
            left:L5    LeftOnly     Valid
            left:L6    Quarantined  Invalid  DecimalParse Invalid on Amount, raw "n/a"
            left:L7    Ambiguous    Valid
            right:B1   Matched      Valid
            right:B2   Matched      Valid
            right:B3   Matched      Valid
            right:B4   Matched      Valid
            right:B6   RightOnly    Valid
            right:B7   Ambiguous    Valid
            right:B8   Ambiguous    Valid
            right:B9   RightOnly    Valid
            """,
            Rows(result),
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void The_summary_counts_entries_except_quarantined_and_suppressed_which_count_rows()
    {
        var result = LedgerAgainstBank();

        Assert.Equal(
            "RunSummary { Matched = 4, Exact = 1, WithinTolerance = 1, Break = 2, LeftOnly = 1, RightOnly = 2, Ambiguous = 1, SearchIncomplete = 0, Quarantined = 1, Suppressed = 0 }",
            result.Summary.ToString());
        Assert.Equal((8, 15), (result.Entries.Count, result.Rows.Count));
    }

    [Fact]
    public void A_blank_amount_against_zero_is_a_break_whose_delta_reads_zero()
    {
        var result = LedgerAgainstBank();

        var entry = Assert.Single(result.Entries, entry => entry.Left is [{ RowKey: "L4" }]);
        var diff = Assert.Single(entry.Diffs);
        Assert.Equal((Alignment.Matched, FieldOutcome.Break), (entry.Alignment, entry.Outcome));
        Assert.Equal((FieldOutcome.Break, true, false, "", "0.00", 0m), (diff.Outcome, diff.Left.IsNull, diff.Right.IsNull, result.Format(diff.Left), result.Format(diff.Right), diff.Delta.Number));
    }

    [Fact]
    public void An_unmatched_or_undecided_entry_compares_nothing()
    {
        var result = LedgerAgainstBank();

        Assert.All(
            result.Entries.Where(entry => entry.Alignment is not Alignment.Matched),
            entry => Assert.Equal((Composition.None, FieldOutcome.NotCompared, 0), (entry.Composition, entry.Outcome, entry.Diffs.Count)));
    }

    [Fact]
    public void An_ambiguous_entry_holds_every_contested_row_and_one_contest_attempt()
    {
        var result = LedgerAgainstBank();

        var entry = Assert.Single(result.Entries, entry => entry.Alignment is Alignment.Ambiguous);
        var attempt = Assert.Single(entry.Explanation.Trail);
        Assert.Equal(("ref", PassVerdict.Contest, "L7 B7 B8"), (attempt.PassId, attempt.Verdict, InOrder(attempt.Candidates)));
    }

    [Fact]
    public void A_matched_entry_records_the_pass_that_committed_it()
    {
        var result = LedgerAgainstBank();

        var entry = Assert.Single(result.Entries, entry => entry.Left is [{ RowKey: "L2" }]);
        var attempt = Assert.Single(entry.Explanation.Trail);
        Assert.Equal(("ref", "ref", PassVerdict.Committed, "L2 B2"), (entry.Explanation.CommittedBy, attempt.PassId, attempt.Verdict, InOrder(attempt.Candidates)));
    }

    [Fact]
    public void A_left_only_or_right_only_entry_names_no_pass()
    {
        var result = LedgerAgainstBank();

        Assert.All(
            result.Entries.Where(entry => entry.Alignment is Alignment.LeftOnly or Alignment.RightOnly),
            entry => Assert.Equal(((string?)null, 0, "payments"), (entry.Explanation.CommittedBy, entry.Explanation.Trail.Count, entry.PathId)));
    }

    [Fact]
    public void A_quarantined_row_has_no_entry_and_its_partner_is_unmatched()
    {
        var result = LedgerAgainstBank();

        var quarantined = Assert.Single(result.Rows, row => row.Row.RowKey == "L6");
        var partner = Assert.Single(result.Rows, row => row.Row.RowKey == "B6");
        Assert.Equal((Disposition.Quarantined, InputValidity.Invalid, (GroupId?)null), (quarantined.Disposition, quarantined.Validity, quarantined.EntryId));
        Assert.Equal(Disposition.RightOnly, partner.Disposition);
        Assert.DoesNotContain(result.Entries, entry => entry.Left.Any(member => member.RowKey == "L6"));
    }

    [Fact]
    public void Each_value_traces_back_to_its_raw_text()
    {
        var result = LedgerAgainstBank();

        Assert.Equal(
            """
            right:B2 #1
              raw Amount "245.00", canonical Amount 245.00
              identifier.map  Ref     "A-2" -> "A-2"
              decimal.parse   Amount  "245.00" -> "245.00"
            left:L4 #3
              raw Amount "", canonical Amount (blank)
              identifier.map  Ref     "A-4" -> "A-4"
              decimal.parse   Amount  (none) -> (none)
            left:L6 #5
              raw Amount "n/a", canonical Amount (blank)
              identifier.map  Ref     "A-6" -> "A-6"
              decimal.parse   Amount  "n/a" -> (none)
            """,
            string.Join("\n", new[] { "B2", "L4", "L6" }.Select(key => Provenance(result, key))),
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void The_run_records_the_profile_hash_reference_data_and_reading()
    {
        var result = LedgerAgainstBank();

        Assert.True(result.Run.ProfileHash is { Length: 64 } hash && hash.All(char.IsAsciiHexDigitLower), result.Run.ProfileHash);
        Assert.Equal(("empty", ""), (result.Run.ReferenceId, result.Run.ReferenceHash));
        Assert.False(string.IsNullOrEmpty(result.Run.EngineVersion));
        var invariant = new SideReading("", ".", ",", CultureOrigin.Run, true);
        Assert.Equal((invariant, invariant), (result.Run.Reading["left"], result.Run.Reading["right"]));
    }

    [Fact]
    public void An_entry_id_depends_on_its_rows_not_on_their_order()
    {
        var forward = LedgerAgainstBank();
        var reversed = Reconciler.Run(
            Compile(LedgerProfile),
            RecordSources.FromObjects("ledger", Enumerable.Reverse(Ledger), Payments),
            RecordSources.FromObjects("bank", Enumerable.Reverse(Bank), Payments),
            ReferenceDataSet.Empty);

        Assert.Equal(Ids(forward), Ids(reversed));
    }

    [Fact]
    public void The_side_by_side_totals_leave_out_rows_the_engine_did_not_decide()
    {
        var result = LedgerAgainstBank();

        var line = Assert.Single(result.BuildSideBySide(new SideBySideSpec([], [], [new MeasureSpec("Amount")], null)).Lines);
        Assert.Equal(new SideBySideCell(440.00m, 505.03m, 65.03m, -40.00m, 110.00m, -4.97m), line.Cells["Amount"]);
    }

    [Fact]
    public void Bins_against_the_book_give_three_entries_for_six_rows()
    {
        var result = Reconciler.Run(
            Compile(WarehouseProfile),
            RecordSources.FromObjects("bins", [new Stock("A1", "WIDGET", "4"), new Stock("A2", "WIDGET", "6"), new Stock("B1", "GADGET", "3"), new Stock("C1", "GIZMO", "2")], Bins),
            RecordSources.FromObjects("book", [new Stock("BOOK-1", "WIDGET", "10"), new Stock("BOOK-2", "GADGET", "5")], Book),
            ReferenceDataSet.Empty);

        Assert.Equal(
            """
            Matched    ManyToOne  Exact            A1 A2 | BOOK-1
            Matched    OneToOne   Break            B1 | BOOK-2  Quantity: 3 vs 5, delta 2
            LeftOnly   None       NotCompared      C1 | -
            """,
            Entries(result, composition: 9, keys: 11),
            ignoreLineEndingDifferences: true);
        var group = result.Entries[0];
        Assert.Equal(("bins", "10", "10"), (group.Explanation.CommittedBy, result.Format(result.Projection(group.Id, Side.Left)["Quantity"]), result.Format(result.Projection(group.Id, Side.Right)["Quantity"])));
        Assert.Equal(3, result.Rows.Count(row => row.EntryId == group.Id));
    }

    [Theory]
    [InlineData(24, "Ambiguous", "bins Contest")]
    [InlineData(25, "SearchIncomplete", "bins SearchIncomplete BlockTooLarge")]
    public void Bins_up_to_the_block_limit_are_searched_and_one_more_is_not(int bins, string alignment, string attempt)
    {
        var result = Reconciler.Run(
            Compile(WarehouseProfile),
            RecordSources.FromObjects("bins", Enumerable.Range(1, bins).Select(index => new Stock($"A{index.ToString(CultureInfo.InvariantCulture)}", "WIDGET", "1")), Bins),
            RecordSources.FromObjects("book", [new Stock("BOOK-1", "WIDGET", "3")], Book),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        var tried = Assert.Single(entry.Explanation.Trail);
        Assert.Equal((alignment, attempt, bins + 1), (entry.Alignment.ToString(), $"{tried.PassId} {tried.Verdict} {tried.Detail}".TrimEnd(), tried.Candidates.Count));
        Assert.All(result.Rows, row => Assert.Equal(alignment, row.Disposition.ToString()));
        Assert.Equal((0, 0, 0), (result.Summary.Matched, result.Summary.LeftOnly, result.Summary.RightOnly));
    }

    [Fact]
    public void A_correction_suppresses_the_trade_it_replaces_and_a_cancel_suppresses_the_whole_chain()
    {
        var result = Reconciler.Run(
            Compile(TradeProfile),
            RecordSources.FromObjects("book", [new TradeEvent("L1", "T-1", "", "", "110"), new TradeEvent("L2", "T-2", "", "", "50")], Events),
            RecordSources.FromObjects(
                "street",
                [
                    new TradeEvent("E1", "T-1", "NEW", "", "100"),
                    new TradeEvent("E2", "T-1", "CORR", "E1", "110"),
                    new TradeEvent("E3", "T-2", "NEW", "", "50"),
                    new TradeEvent("E4", "T-2", "CANC", "E3", "50"),
                    new TradeEvent("E5", "T-3", "CANC", "E9", "70"),
                ],
                Events),
            ReferenceDataSet.Empty);

        Assert.Equal(
            """
            Matched    OneToOne, LifecycleCollapsed  Exact            L1 | E2
            LeftOnly   None                          NotCompared      L2 | -
            """,
            Entries(result, composition: 28),
            ignoreLineEndingDifferences: true);
        var matched = result.Entries[0].Id;
        Assert.Equal(
            """
            left:L1    Matched                              entry L1 | E2
            left:L2    LeftOnly                             entry L2 | -
            right:E1   Suppressed   Superseded              entry L1 | E2
            right:E2   Matched                              entry L1 | E2
            right:E3   Suppressed   FullyCancelled          entry none
            right:E4   Suppressed   FullyCancelled          entry none
            right:E5   Quarantined  MissingLifecycleTarget  entry none
            """,
            string.Join("\n", result.Rows.Select(row => LifecycleRow(result, row))),
            ignoreLineEndingDifferences: true);
        Assert.Equal(matched, Assert.Single(result.Rows, row => row.Row.RowKey == "E1").EntryId);
        Assert.Equal((1, 3), (result.Summary.Quarantined, result.Summary.Suppressed));
    }

    private static ReconciliationResult LedgerAgainstBank() =>
        Reconciler.Run(
            Compile(LedgerProfile),
            RecordSources.FromObjects("ledger", Ledger, Payments),
            RecordSources.FromObjects("bank", Bank, Payments),
            ReferenceDataSet.Empty);

    private static CompiledProfile Compile(string profile) =>
        Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore());

    private static string Entries(ReconciliationResult result, int composition = 8, int keys = 9) =>
        string.Join("\n", result.Entries.Select(entry => Entry(result, entry, composition, keys).TrimEnd()));

    private static string Entry(ReconciliationResult result, ResultEntry entry, int composition, int keys)
    {
        var members = $"{Listed(entry.Left)} | {Listed(entry.Right)}";
        var diffs = string.Join("; ", entry.Diffs.Select(diff => $"{diff.Field}: {Shown(result, diff.Left)} vs {Shown(result, diff.Right)}, delta {diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}"));
        return $"{entry.Alignment,-10} {entry.Composition.ToString().PadRight(composition)}  {entry.Outcome,-16} {members.PadRight(keys)}  {diffs}";
    }

    private static string Rows(ReconciliationResult result) =>
        string.Join("\n", result.Rows.Select(row => $"{Name(row.Row),-10} {row.Disposition,-12} {row.Validity,-8} {Diagnostics(row)}".TrimEnd()));

    private static string Diagnostics(RowResult row) =>
        string.Join("; ", row.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Severity} on {diagnostic.Field}, raw \"{diagnostic.RawText}\""));

    private static string LifecycleRow(ReconciliationResult result, RowResult row)
    {
        var why = row switch
        {
            { Suppression: { } reason } => reason.ToString(),
            { Disposition: Disposition.Quarantined } => string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code)),
            _ => "",
        };
        var entry = row.EntryId is { } id ? Members(Assert.Single(result.Entries, entry => entry.Id == id)) : "none";
        return $"{Name(row.Row),-10} {row.Disposition,-12} {why,-23} entry {entry}";
    }

    private static string Name(SourceRef row) => $"{row.Source}:{row.RowKey}";

    private static string InOrder(IEnumerable<SourceRef> rows) => string.Join(" ", rows.Select(row => row.RowKey));

    private static string Provenance(ReconciliationResult result, string rowKey)
    {
        var row = Assert.Single(result.Rows, row => row.Row.RowKey == rowKey).Row;
        var canonical = result.Canonical(row);
        string[] lines =
        [
            $"{row.Source}:{row.RowKey} #{row.Ordinal.ToString(CultureInfo.InvariantCulture)}",
            $"  raw Amount \"{result.Raw(row, "Amount")}\", canonical Amount {Shown(result, canonical["Amount"])}",
            .. result.Trace(row).Select(step => $"  {step.RuleId,-15} {step.Field,-7} {Quoted(step.Raw)} -> {Quoted(step.Canonical)}"),
        ];
        return string.Join("\n", lines);
    }

    private static string Shown(ReconciliationResult result, CanonicalValue value) =>
        value.IsNull ? "(blank)" : result.Format(value);

    private static string Quoted(string? text) => text is null ? "(none)" : $"\"{text}\"";

    private static string Members(ResultEntry entry) => $"{Listed(entry.Left)} | {Listed(entry.Right)}";

    private static string Listed(IReadOnlyList<SourceRef> rows) => rows.Count is 0 ? "-" : Keys(rows);

    private static string Keys(IEnumerable<SourceRef> rows) => string.Join(" ", rows.Select(row => row.RowKey).Order(StringComparer.Ordinal));

    private static string[] Ids(ReconciliationResult result) =>
        [.. result.Entries.Select(entry => $"{Members(entry)} {entry.Id}").Order(StringComparer.Ordinal)];

    public sealed record Payment(string Id, string Ref, string Amount);

    public sealed record Stock(string Id, string Sku, string Quantity);

    public sealed record TradeEvent(string Id, string Ref, string Action, string Target, string Qty);
}
