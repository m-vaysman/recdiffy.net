using System.Data;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ArgonSniffTests
{
    // --- BREAKS (failing today) ---

    [Fact]
    public void Packages_Pack_quarantining_a_multi_leg_package_does_not_throw_duplicate_key()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "pkg-inconsistent",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "PackageRef", "type": "Text", "role": "Package" },
                { "name": "LegRole", "type": "Symbol", "role": "Package" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
                    { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" }
                  ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "consolidation": {
                "left": {
                  "packages": [ {
                    "link": "PackageRef",
                    "role": "LegRole",
                    "requiredRoles": ["PAY", "REC"],
                    "aggregate": { "Quantity": "Same" }
                  } ]
                }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Link, string Role, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("PackageRef", r => r.Link)
            .Text("LegRole", r => r.Role)
            .Text("Quantity", r => r.Qty);

        var leftRows = new[]
        {
            ("L1", "PKG-1", "PAY", "100"),
            ("L2", "PKG-1", "REC", "200"),
        };

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", leftRows, map),
            RecordSources.FromObjects("right", Array.Empty<(string, string, string, string)>(), map),
            ReferenceDataSet.Empty);

        Assert.All(result.Rows, row =>
        {
            Assert.Equal(Disposition.Quarantined, row.Disposition);
            Assert.Contains(row.Diagnostics, d => d.Code == "PackageInconsistent");
        });
    }

    [Fact]
    public void DataReaderSource_reads_all_rows_without_consuming_the_first_data_row_as_header()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(SimpleDecimalProfile), RuleRegistry.CreateCore());
        using var table = new DataTable();
        using var idCol = table.Columns.Add("Id", typeof(string));
        using var qtyCol = table.Columns.Add("Quantity", typeof(decimal));
        _ = table.Rows.Add("L1", 10m);
        _ = table.Rows.Add("L2", 20m);

        var left = RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions(0, "Id"));
        var right = RecordSources.FromObjects("right", Array.Empty<Dictionary<string, string>>(), SimpleDecimalMap());

        var result = Reconciler.Run(profile, left, right, ReferenceDataSet.Empty);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["L1", "L2"], result.Rows.Select(r => r.Row.RowKey).OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public void DataReaderSource_and_DecimalParse_do_not_throw_on_NaN_infinity_or_huge_double()
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Quantity"] = double.NaN });
        var rule = new DecimalParseRule("Quantity", "Quantity", 2, MidpointMode.ToEven);
        var updated = row;
        rule.Apply(ref updated);

        Assert.Contains(updated.Diagnostics, d => d.Code == "DecimalParse");
    }

    [Fact]
    public void DateParse_rule_does_not_throw_on_out_of_range_oa_date_double()
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["TradeDate"] = 1e100d });
        var rule = new DateParseRule("TradeDate", "TradeDate", ["yyyy-MM-dd"], null);
        var updated = row;
        rule.Apply(ref updated);

        Assert.Contains(updated.Diagnostics, d => d.Code == "DateParse");
    }

    [Fact]
    public void Decimal_comparison_does_not_overflow_when_comparing_extreme_decimals()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "extreme-decimal",
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
              "paths": [ {
                "id": "stock",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Sku"] } ],
                "diff": { "fields": { "Quantity": { "tolerance": { "absolute": "10" } } } }
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Sku, decimal Qty)>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Decimal("Quantity", r => r.Qty);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "A", decimal.MaxValue)], map),
            RecordSources.FromObjects("right", [("R1", "A", decimal.MinValue)], map),
            ReferenceDataSet.Empty);

        Assert.Equal(FieldOutcome.Break, Assert.Single(result.Entries).Outcome);
    }

    [Fact]
    public void QuantityAbsolute_rule_does_not_overflow_on_decimal_MinValue()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "qty-min",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "derive": [ { "rule": "quantity.absolute", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), TradingRulePack.AddTo(RuleRegistry.CreateCore()));

        var map = new SourceMap<(string Id, decimal Qty)>()
            .Text("Id", r => r.Id)
            .Decimal("Quantity", r => r.Qty);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", decimal.MinValue)], map),
            RecordSources.FromObjects("right", Array.Empty<(string Id, decimal Qty)>(), map),
            ReferenceDataSet.Empty);

        Assert.Single(result.Rows);
    }

    [Fact]
    public void SubsetSearch_finds_valid_subset_when_later_class_is_negative_and_first_class_exceeds_target()
    {
        var classes = new ConservedClass[]
        {
            new([15m], [0]),
            new([10m], [1]),
            new([-5m], [2]),
        };
        var search = SubsetSearch.Search(classes, [10m], 8, 250_000);

        Assert.Equal(SearchState.Unique, search.State);
        Assert.Equal([0, 2], search.DrawnItems);
    }

    [Fact]
    public void SubsetSearch_finds_valid_subset_when_second_conserved_field_has_negative_values()
    {
        var classes = new ConservedClass[]
        {
            new([5m, 15m], [0]),
            new([5m, 10m], [1]),
            new([5m, -5m], [2]),
        };
        var search = SubsetSearch.Search(classes, [10m, 10m], 8, 250_000);

        Assert.Equal(SearchState.Unique, search.State);
        Assert.Equal([0, 2], search.DrawnItems);
    }

    [Fact]
    public void Lifecycle_collapse_does_not_throw_when_conflicting_duplicate_rows_are_lifecycle_events()
    {
        var map = new SourceMap<(string Id, string Action, string? Target, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("EventAction", r => r.Action)
            .Text("EventTarget", r => r.Target)
            .Text("Quantity", r => r.Qty);

        var rightRows = new[]
        {
            ("E1", "NEW", (string?)null, "100"),
            ("E2", "CANC", "E1", "100"),
            ("E2", "CANC", "E1", "200"),
        };

        var rawRight = RecordSources.FromObjects("right", rightRows, map).Materialize("right", "Id", CancellationToken.None);
        var duplicateWithAction = rawRight.Select((r, idx) =>
        {
            var ctx = RowContext.Create(r.Ref.RowKey, Side.Right, r.Cells);
            var (p1, s1) = ctx.Pool.InternSymbol("ACME");
            var (p2, s2) = p1.InternSymbol(idx == 0 ? "NEW" : "CANC");
            ctx = ctx.WithPool(p2)
                .Write("Counterparty", CanonicalCell.FromInterned(FieldType.Symbol, s1), "test", null, "ACME")
                .Write("EventAction", CanonicalCell.FromInterned(FieldType.Symbol, s2), "test", null, idx == 0 ? "NEW" : "CANC");
            if (idx > 0)
                ctx = ctx.Write("EventTarget", CanonicalCell.FromText("E1"), "test", null, "E1");
            return new IntakeRow([r.Ref], ctx, idx > 0);
        }).ToArray();

        var plan = new LifecyclePlan("EventAction", "EventTarget", "Counterparty", "CANC");
        var collapsed = Consolidation.Run(duplicateWithAction, plan, null);

        Assert.All(collapsed, c => Assert.True(c.Quarantined));
    }

    [Fact]
    public void DateParse_rule_with_formatBy_declares_read_dependency_so_compile_rejects_read_before_write()
    {
        var exception = Assert.Throws<ProfileCompileException>(() =>
            Reconciler.Compile(ProfileDocument.Parse("""
                {
                  "formatVersion": 1,
                  "profileId": "date-formatby-read-before-write",
                  "version": "1.0.0",
                  "fields": [
                    { "name": "Counterparty", "type": "Identifier", "role": "Key" },
                    { "name": "TradeDate", "type": "Date", "role": "Key" }
                  ],
                  "normalization": {
                    "left": {
                      "parse": [
                        { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formatBy": "Counterparty" }
                      ]
                    }
                  },
                  "paths": [ { "id": "only", "route": { "rule": "route.all" } } ]
                }
                """), RuleRegistry.CreateCore()));

        Assert.Contains(exception.Errors, error => error.Code == "FieldReadBeforeWrite");
    }

    [Fact]
    public void Invalid_row_from_normalization_receives_Quarantined_disposition()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(SimpleDecimalProfile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "L1", ["Quantity"] = "not-a-number" }], SimpleDecimalMap()),
            RecordSources.FromObjects("right", Array.Empty<Dictionary<string, string>>(), SimpleDecimalMap()),
            ReferenceDataSet.Empty);

        var row = Assert.Single(result.Rows);
        Assert.Equal(InputValidity.Invalid, row.Validity);
        Assert.Equal(Disposition.Quarantined, row.Disposition);
        Assert.Equal(1, result.Summary.Quarantined);
    }

    [Fact]
    public void Lifecycle_quarantine_diagnostic_code_is_preserved_in_row_result_diagnostics()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "life-missing-diag",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
                { "name": "EventTarget", "type": "Text", "role": "Lifecycle" }
              ],
              "normalization": {
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
                    { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" }
                  ]
                }
              },
              "consolidation": {
                "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Action, string Target)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("EventAction", r => r.Action)
            .Text("EventTarget", r => r.Target);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", Array.Empty<(string, string, string)>(), map),
            RecordSources.FromObjects("right", [("C1", "CANC", "MISSING")], map),
            ReferenceDataSet.Empty);

        var row = Assert.Single(result.Rows);
        Assert.Equal(Disposition.Quarantined, row.Disposition);
        Assert.Contains(row.Diagnostics, d => d.Code == "MissingLifecycleTarget");
    }

    [Fact]
    public void Format_resolves_symbols_and_identifiers()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "format-symbol",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Side", "type": "Symbol", "role": "Key" },
                { "name": "Uti", "type": "Identifier", "role": "Identity" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Side", "to": "Side" },
                    { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "UTI" }
                  ]
                }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Side, string Uti)>()
            .Text("Id", r => r.Id)
            .Text("Side", r => r.Side)
            .Text("Uti", r => r.Uti);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "BUY", "UTI-123")], map),
            RecordSources.FromObjects("right", Array.Empty<(string, string, string)>(), map),
            ReferenceDataSet.Empty);

        var canonical = result.Canonical(result.Rows[0].Row);
        Assert.Equal("BUY", result.Format(canonical["Side"]));
        Assert.Equal("UTI-123", result.Format(canonical["Uti"]));
    }

    [Fact]
    public void Date_days_tolerance_allows_dates_within_configured_days()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "date-days-tolerance",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Sku", "type": "Symbol", "role": "Key" },
                { "name": "SettleDate", "type": "Date", "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] } ]
                },
                "right": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] } ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Sku"] } ],
                "diff": { "fields": { "SettleDate": { "tolerance": { "days": 2 } } } }
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Sku, string Date)>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Text("SettleDate", r => r.Date);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "A", "2025-06-12")], map),
            RecordSources.FromObjects("right", [("R1", "A", "2025-06-13")], map),
            ReferenceDataSet.Empty);

        Assert.Equal(FieldOutcome.WithinTolerance, Assert.Single(result.Entries).Outcome);
    }

    [Fact]
    public void SharedRef_pass_ignores_broker_scheme_and_matches_shared_scheme_only()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "shared-ref-broker-scheme",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "TradeRef", "type": "Identifier", "role": "Identity" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "broker" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" }
                  ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.sharedRef" } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Cpty, string Ref)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", r => r.Cpty)
            .Text("TradeRef", r => r.Ref);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "ACME", "REF-1")], map),
            RecordSources.FromObjects("right", [("R1", "ACME", "REF-1")], map),
            ReferenceDataSet.Empty);

        Assert.DoesNotContain(result.Entries, e => e.Alignment == Alignment.Matched);
    }

    [Fact]
    public void ProfileCompile_rejects_package_aggregator_role_not_in_required_roles()
    {
        var exception = Assert.Throws<ProfileCompileException>(() =>
            Reconciler.Compile(ProfileDocument.Parse("""
                {
                  "formatVersion": 1,
                  "profileId": "pkg-bad-role",
                  "version": "1.0.0",
                  "fields": [
                    { "name": "PackageRef", "type": "Text", "role": "Package" },
                    { "name": "LegRole", "type": "Symbol", "role": "Package" },
                    { "name": "Price", "type": "Decimal", "scale": 2, "role": "Economic" }
                  ],
                  "consolidation": {
                    "left": {
                      "packages": [ {
                        "link": "PackageRef",
                        "role": "LegRole",
                        "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"],
                        "aggregate": { "Price": "FromRole(NON_EXISTENT_ROLE)" }
                      } ]
                    }
                  },
                  "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
                }
                """), RuleRegistry.CreateCore()));

        Assert.NotEmpty(exception.Errors);
    }

    // --- UNTESTED BUT HOLDS ---

    [Fact]
    public void Culture_tr_TR_and_de_DE_do_not_change_parsing_or_rules()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "tr-TR", "de-DE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                var profile = Reconciler.Compile(ProfileDocument.Parse("""
                    {
                      "formatVersion": 1,
                      "profileId": "culture-test",
                      "version": "1.0.0",
                      "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
                      "fields": [
                        { "name": "AssetClass", "type": "Symbol", "role": "Key" },
                        { "name": "Side", "type": "Symbol", "role": "Key" },
                        { "name": "TradeDate", "type": "Date", "role": "Key" },
                        { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
                        { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" }
                      ],
                      "normalization": {
                        "left": {
                          "identify": [ { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
                          "parse": [
                            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                            { "rule": "decimal.parse", "from": "Price", "to": "Price" }
                          ],
                          "derive": [
                            { "rule": "side.resolve", "code": "Dir", "quantity": "Qty", "asset": "AssetClass" },
                            { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
                          ]
                        },
                        "right": {
                          "identify": [ { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
                          "parse": [
                            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                            { "rule": "price.from32nds", "from": "Price", "to": "Price" }
                          ],
                          "derive": [
                            { "rule": "side.resolve", "code": "Dir", "quantity": "Qty", "asset": "AssetClass" },
                            { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
                          ]
                        }
                      },
                      "paths": [ {
                        "id": "p",
                        "route": { "rule": "route.all" },
                        "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["AssetClass", "Side", "TradeDate", "Quantity"] } ]
                      } ]
                    }
                    """), TradingRulePack.AddTo(RuleRegistry.CreateCore()));

                var map = new SourceMap<(string Id, string Asset, string Dir, string Qty, string Price, string Date)>()
                    .Text("Id", r => r.Id)
                    .Text("Asset", r => r.Asset)
                    .Text("Dir", r => r.Dir)
                    .Text("Qty", r => r.Qty)
                    .Text("Price", r => r.Price)
                    .Text("TradeDate", r => r.Date);

                var result = Reconciler.Run(
                    profile,
                    RecordSources.FromObjects("left", [("L1", "Bond", "buy", "1000.50", "96.078125", "2025-06-12")], map),
                    RecordSources.FromObjects("right", [("R1", "Bond", "b", "1000.50", "96-02+", "2025-06-12")], map),
                    ReferenceDataSet.Empty);

                var entry = Assert.Single(result.Entries);
                Assert.Equal(Alignment.Matched, entry.Alignment);
                Assert.Equal(FieldOutcome.Exact, entry.Outcome);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task AsyncObjectSource_propagates_exception_thrown_mid_stream()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(SimpleDecimalProfile), RuleRegistry.CreateCore());

        static async IAsyncEnumerable<Dictionary<string, string>> FailingStream()
        {
            await Task.Yield();
            yield return new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "L1", ["Quantity"] = "10" };
            throw new InvalidOperationException("Mid-stream failure");
        }

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Reconciler.RunAsync(
                profile,
                RecordSources.FromObjects("left", FailingStream(), SimpleDecimalMap()),
                RecordSources.FromObjects("right", Array.Empty<Dictionary<string, string>>(), SimpleDecimalMap()),
                ReferenceDataSet.Empty));
    }

    [Fact]
    public async Task AsyncObjectSource_observes_cancellation_mid_stream()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(SimpleDecimalProfile), RuleRegistry.CreateCore());
        using var cts = new CancellationTokenSource();

        async IAsyncEnumerable<Dictionary<string, string>> CancellingStream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "L1", ["Quantity"] = "10" };
            await cts.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            yield return new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "L2", ["Quantity"] = "20" };
        }

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reconciler.RunAsync(
                profile,
                RecordSources.FromObjects("left", CancellingStream(), SimpleDecimalMap()),
                RecordSources.FromObjects("right", Array.Empty<Dictionary<string, string>>(), SimpleDecimalMap()),
                ReferenceDataSet.Empty,
                cancellationToken: cts.Token));
    }

    [Fact]
    public void Negative_zero_and_positive_zero_with_different_scales_match_on_natural_key()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "neg-zero",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Sku", "type": "Symbol", "role": "Key" },
                { "name": "Price", "type": "Decimal", "scale": 2, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Price", "to": "Price" } ]
                },
                "right": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Price", "to": "Price" } ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Sku", "Price"] } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Sku, decimal Price)>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Decimal("Price", r => r.Price);

        var negZero = new decimal(0, 0, 0, isNegative: true, scale: 4);
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "A", negZero)], map),
            RecordSources.FromObjects("right", [("R1", "A", 0m)], map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Fact]
    public void InstrumentMaster_records_conflicts_when_same_scheme_and_value_map_to_two_canonicals()
    {
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["ISIN"] = "US1111111111", ["Ticker"] = "DUP" },
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["ISIN"] = "US2222222222", ["Ticker"] = "DUP" },
        };

        var master = InstrumentMaster.FromRows(rows, ["ISIN", "CUSIP", "SEDOL", "Ticker"]);

        Assert.Contains(("TICKER", "DUP"), master.Conflicts);
        Assert.False(master.Map.ContainsKey(("TICKER", "DUP")));
        Assert.Equal("US1111111111", master.Map[("ISIN", "US1111111111")]);
        Assert.Equal("US2222222222", master.Map[("ISIN", "US2222222222")]);
    }

    [Fact]
    public void Duplicate_collapsed_row_in_group_sum_preserves_canonical_values_for_each_member()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "group-sum-canonical-preserve",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Sku", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                },
                "right": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Sku, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Text("Quantity", r => r.Qty);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "A", "4"), ("L1", "A", "4"), ("L2", "A", "6")], map),
            RecordSources.FromObjects("right", [("R1", "A", "10")], map),
            ReferenceDataSet.Empty);

        foreach (var row in result.Rows.Where(r => r.Row.RowKey == "L1"))
            Assert.Equal(4m, result.Canonical(row.Row)["Quantity"].Number);
    }

    [Fact]
    public void Parallel_run_is_deterministic_when_multiple_groups_share_a_multi_row_left_side()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "parallel-group-sum",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Sku", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                },
                "right": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Sku, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Text("Quantity", r => r.Qty);

        var leftRows = Enumerable.Range(0, 20).SelectMany(i => new[]
        {
            ($"L{i}a", $"SKU-{i}", "4"),
            ($"L{i}b", $"SKU-{i}", "6"),
        }).ToArray();
        var rightRows = Enumerable.Range(0, 20).Select(i => ($"R{i}", $"SKU-{i}", "10")).ToArray();

        var serial = Reconciler.Run(profile, RecordSources.FromObjects("left", leftRows, map), RecordSources.FromObjects("right", rightRows, map), ReferenceDataSet.Empty, new RunOptions(1));
        var parallel = Reconciler.Run(profile, RecordSources.FromObjects("left", leftRows, map), RecordSources.FromObjects("right", rightRows, map), ReferenceDataSet.Empty, new RunOptions(8));

        Assert.Equal(serial.Summary, parallel.Summary);
        foreach (var entry in serial.Entries)
        {
            Assert.Equal(
                serial.Projection(entry.Id, Side.Left)["Quantity"].Number,
                parallel.Projection(entry.Id, Side.Left)["Quantity"].Number);
        }
    }

    [Fact]
    public void Lifecycle_collapse_with_duplicate_collapsed_row_preserves_all_copies()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "dup-collapsed-lifecycle",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
                { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
                    { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" }
                  ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "consolidation": {
                "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Action, string? Target, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("EventAction", r => r.Action)
            .Text("EventTarget", r => r.Target)
            .Text("Quantity", r => r.Qty);

        var rightRows = new[]
        {
            ("E1", "NEW", (string?)null, "100"),
            ("E2", "CANC", "E1", "100"),
            ("E3", "CORR", "E2", "200"),
            ("E3", "CORR", "E2", "200"),
        };

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", Array.Empty<(string, string, string?, string)>(), map),
            RecordSources.FromObjects("right", rightRows, map),
            ReferenceDataSet.Empty);

        Assert.Equal(4, result.Rows.Count);
    }

    private static SourceMap<Dictionary<string, string>> SimpleDecimalMap() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", r => r["Id"])
            .Text("Quantity", r => r["Quantity"]);

    private const string SimpleDecimalProfile = """
        {
          "formatVersion": 1,
          "profileId": "simple-decimal",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
          "normalization": {
            "left": { "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
        }
        """;
}
