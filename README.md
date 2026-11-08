<p align="center">
  <img alt="RecDiffy.NET" src="RecDiffy.NET/resources/logos/columns-dark.jpg" width="640" />
</p>

# RecDiffy.NET

[![CI](https://github.com/m-vaysman/recdiffy.net/actions/workflows/ci.yml/badge.svg)](https://github.com/m-vaysman/recdiffy.net/actions/workflows/ci.yml)
[![Quality Gate](https://sonar.cobblerhill.net/api/project_badges/measure?project=m-vaysman_recdiffy.net&metric=alert_status)](https://sonar.cobblerhill.net/dashboard?id=m-vaysman_recdiffy.net) [![Coverage](https://sonar.cobblerhill.net/api/project_badges/measure?project=m-vaysman_recdiffy.net&metric=coverage)](https://sonar.cobblerhill.net/dashboard?id=m-vaysman_recdiffy.net) [![Bugs](https://sonar.cobblerhill.net/api/project_badges/measure?project=m-vaysman_recdiffy.net&metric=bugs)](https://sonar.cobblerhill.net/dashboard?id=m-vaysman_recdiffy.net) [![Vulnerabilities](https://sonar.cobblerhill.net/api/project_badges/measure?project=m-vaysman_recdiffy.net&metric=vulnerabilities)](https://sonar.cobblerhill.net/dashboard?id=m-vaysman_recdiffy.net) [![Code Smells](https://sonar.cobblerhill.net/api/project_badges/measure?project=m-vaysman_recdiffy.net&metric=code_smells)](https://sonar.cobblerhill.net/dashboard?id=m-vaysman_recdiffy.net) [![Maintainability](https://sonar.cobblerhill.net/api/project_badges/measure?project=m-vaysman_recdiffy.net&metric=sqale_rating)](https://sonar.cobblerhill.net/dashboard?id=m-vaysman_recdiffy.net) [![Security](https://sonar.cobblerhill.net/api/project_badges/measure?project=m-vaysman_recdiffy.net&metric=security_rating)](https://sonar.cobblerhill.net/dashboard?id=m-vaysman_recdiffy.net)
[![Benchmarks](https://img.shields.io/badge/benchmarks-Bencher-0b7285)](https://bench.cobblerhill.net/perf/recdiffy)

Reconcile two datasets and explain every match, difference, and unmatched row.

RecDiffy.NET is a general-purpose reconciliation and diffing library for .NET. Use it when records from two systems represent the same underlying activity but differ in identifiers, formatting, or grouping—for example, warehouse bins versus book inventory, or internal trades versus broker records.

A reconciliation runs through:

**Normalize → Consolidate → Match → Compare → Explain**

Matching determines which records belong together. Comparison determines whether their field values agree.

## See it work: two files, two readers, one reconciliation

The broker emails a CSV of confirmations. Your order system exports its blotter to Excel. Read each with any reader you like, then let `RecBuilder` line them up:

![The broker's CSV and the blotter workbook side by side, with the differences highlighted](docs/typed-reconciliation/images/finance-files.svg)

```csharp
var confirms = ReadConfirms("broker-confirms.csv");  // reader 1: Sylvan.Data.Csv
var blotter = ReadBlotter("blotter.xlsx");           // reader 2: Sylvan.Data.Excel

var result = RecBuilder.Between(confirms, blotter)
    .MatchOn(confirm => confirm.TradeRef, trade => trade.TradeId)
    .ThenMatchOn(confirm => confirm.Isin, trade => trade.Isin)
        .And(confirm => confirm.TradeDate, trade => trade.ExecutedOn)
        .And(confirm => confirm.Side, trade => trade.BuySell)
    .Compare(confirm => confirm.Quantity, trade => trade.Qty)
    .Compare(confirm => confirm.Price, trade => trade.AvgPrice, tolerance: 0.0001m)
    .Compare(confirm => confirm.NetAmount, trade => trade.NetConsideration, tolerance: 0.01m)
    .Run();
```

```text
Matched 4 (2 with breaks) | Ambiguous 0 | Left only 1 | Right only 1

  Exact             C-1001 ↔ T-5001
  Within tolerance  C-1002 ↔ T-5002
                      NetAmount: 206050.00 vs 206049.99
  Break             C-1003 ↔ T-5003
                      Quantity: 250 vs 200
                      NetAmount: 61450.00 vs 49160
  Break             C-1004 ↔ T-5004
                      TradeRef: (blank) vs T-5004
  Left only         C-1005
  Right only        T-5006
```

Every bucket holds your own `BrokerConfirm` and `BlotterTrade` objects. RecDiffy doesn't read the files here: [Sylvan](https://github.com/MarkPflug/Sylvan) does, and any reader that gives you objects works. The [full walkthrough](docs/typed-reconciliation/README.md) covers the two readers, the mapping line by line, and examples from logistics, retail and healthcare billing.

## Prerequisites

- .NET SDK **10.0.403**, or a compatible version permitted by [global.json](RecDiffy.NET/global.json).
- Git.

## Build and test

```powershell
git clone https://github.com/m-vaysman/recdiffy.net.git
cd recdiffy.net
git switch dev
cd RecDiffy.NET

dotnet restore
dotnet build
dotnet test
```

Run these commands from the `RecDiffy.NET` directory, which contains the solution and SDK configuration.

## Your first reconciliation

Suppose your warehouse reports two bins:

| Bin | SKU | Quantity |
|---|---|---:|
| A | WIDGET | 4 |
| B | WIDGET | 6 |

Your inventory book reports one position:

| Position | SKU | Quantity |
|---|---|---:|
| BOOK | WIDGET | 10 |

A group-sum matching pass can reconcile the two warehouse rows against the single book row because their quantities total 10.

Run the sample from the `RecDiffy.NET` directory:

```powershell
dotnet run --project src/RecDiffy.Samples/RecDiffy.Samples.csproj
```

The first example prints one matched entry with `ManyToOne` composition and an `Exact` comparison outcome. All three source rows receive a `Matched` disposition. The same program then shows a field break, an unmatched row, a quarantined row, a side-by-side view, an async run, and a trading-pack run that uses reference data.

The application API has three steps:

```csharp
// API outline: profileJson, datasets, and mappings are supplied by your application.

// 1. Compile the reconciliation rules.
var profile = Reconciler.Compile(
    ProfileDocument.Parse(profileJson),
    RuleRegistry.CreateCore());

// 2. Adapt each dataset into a record source.
var left = RecordSources.FromObjects("bins", bins, binMap);
var right = RecordSources.FromObjects("book", positions, positionMap);

// 3. Run the reconciliation.
var result = Reconciler.Run(
    profile,
    left,
    right,
    ReferenceDataSet.Empty);
```

The complete program for that outline is [FirstReconciliation.cs](RecDiffy.NET/src/RecDiffy.Samples/FirstReconciliation.cs). The same behavior is checked by `Matches_warehouse_bins_to_book_stock_without_the_trading_pack` in [VerificationTests.cs](RecDiffy.NET/src/RecDiffy.Tests/VerificationTests.cs):

```powershell
dotnet test src/RecDiffy.Tests/RecDiffy.Tests.csproj --filter FullyQualifiedName~Matches_warehouse_bins_to_book_stock_without_the_trading_pack
```

## Understand the result

These concepts answer different questions:

| Concept | Question | Examples |
|---|---|---|
| Alignment | Did records match across sides? | `Matched`, `LeftOnly`, `RightOnly` |
| Composition | How were records grouped? | `OneToOne`, `ManyToOne` |
| Field outcome | Do the compared values agree? | `Exact`, `WithinTolerance`, `Break` |
| Row disposition | What happened to this input row? | `Matched`, `Quarantined`, `SearchIncomplete` |

**A matched entry can still contain a break.** Matching establishes correspondence; it does not guarantee equal field values.

An entry can also represent multiple source rows. In the warehouse example, one result entry accounts for three input rows. Keep entry counts and source-row counts separate when building reports.

## Configure a reconciliation

A profile declares:

- **Fields:** canonical names, types, and roles.
- **Reading:** the culture each file's text is read with, and whether date formats are strict.
- **Normalization:** how source values become canonical values.
- **Consolidation:** optional lifecycle and package processing before matching.
- **Routes:** which matching path receives each row.
- **Matching passes:** ordered strategies for finding corresponding records.
- **Comparison rules:** fields to compare and tolerances to apply.
- **Search limits:** bounds on group matching.

Compile the profile once, then pass the compiled profile to reconciliation runs. Start with the warehouse profile in the sample above before adapting it to your own fields and source mappings.

## Declare how each file is read

Text in a file is read with a culture. Declare it once for the run and, when one file differs, for that side. Nothing below the declaration picks a culture, and nothing is guessed.

```json
{
  "culture": "en-US",
  "sources": {
    "left":  { "rowKey": "ConfirmId", "culture": "de-DE" },
    "right": { "rowKey": "TradeId" }
  }
}
```

```csharp
builder
    .Culture(CultureInfo.GetCultureInfo("en-US"))                // both files
    .Culture(Side.Left, CultureInfo.GetCultureInfo("de-DE"));    // this file is German
```

| | What happens |
|---|---|
| **Resolution** | A side uses its own culture, else the run's, else the server's culture, read once when the profile is compiled. `"culture": ""` is invariant. |
| **Numbers** | `1.234,56` on a de-DE side and `1,234.56` on an en-US side are both 1234.56, and compare `Exact`. A thousands separator where the culture puts none is `Invalid`: `1,5` on an en-US side is not 15. |
| **Dates** | Formats are strict: `/` and `:` mean exactly those characters, and the culture supplies month and day names. `"strictDateFormats": false`, for the run or one side, restores the .NET behaviour, where they also match the culture's separators. |
| **Floating point** | `float` and `double` are never converted. They are `Invalid` with `FloatValue`, so supply a `decimal` or text. Excel numbers arrive as the decimal the workbook stores. |
| **Text RecDiffy makes** | Identifiers, symbols and row keys made from a number are invariant, so a numeric key matches across sides with different cultures. |
| **Record** | `result.Run.Reading` shows each side's culture, separators, where the culture came from, and whether dates were strict. |

## Write Excel reports

The separate `RecDiffy.Excel` package writes formatted back- and middle-office reports from a `ReconciliationResult`, one `.xlsx` file per report. Numbers on a report tab are formulas that link to the engine's output in the same file, every decomposition has a ✓/✗ check, and ageing is a formula on a named `AsOf` cell.

```csharp
using RecDiffy.Excel;

var options = new ExcelReportOptions
{
    AsOf = new DateOnly(2025, 6, 13),
    Profile = profile, // profile id, tolerances, pass order; also maps report roles to same-named fields
    FieldMap = FieldMap.FromProfile(profile).With(ReportRole.Asset, "AssetClass"), // optional override
};

foreach (var outcome in result.WriteAll("reports", options))
    Console.WriteLine(outcome.Written ? outcome.Path : $"{outcome.FileName} skipped: {outcome.SkipReason}");
```

| Office | Reports |
|---|---|
| Back office | BO1 ExceptionBlotter, BO2 SideBySideCompare, BO3 RawVsNormalized, BO4 LifecycleChains, BO5 Allocations, BO6 CashLadder |
| Middle office | MO1 ControlTotals, MO2 Scorecard, MO3 CascadeFunnel, MO4 BreakHeatmap, MO5 Ageing, MO6 ToleranceUsage, MO7 AuditTrail |

Files are named `<ID>_<ReportName>_<profileId>_<asOf yyyy-MM-dd>.xlsx`. Reports read fields only through `FieldMap`; a report whose required roles are unmapped, or that has nothing to show, is skipped and the reason is returned. `RecDiffyExcel.RunAndWrite` runs the engine and writes in one call, and `WriteReport` writes one report to a stream. `Labels` replaces report wording with your own column names. The layout spec is in [docs/report-specs](RecDiffy.NET/docs/report-specs/README.md).

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| Build cannot find a compatible SDK | Compare `dotnet --list-sdks` with [global.json](RecDiffy.NET/global.json). |
| Profile compilation fails | Inspect the errors on `ProfileCompileException`. |
| A row is quarantined | Inspect its diagnostics for missing keys or invalid values. |
| Rows remain unmatched | Check normalized key values, routing, and pass configuration. |
| A component reports `BlockTooLarge` | Inspect its size and blocking fields before increasing limits. |
| Records match but show `Break` | Inspect field differences and configured tolerances. |
| A row is `Invalid` with `FloatValue` | The source gave a `float` or `double`. Supply a `decimal` or text. |
| A number that looks right is `Invalid` with `DecimalParse` | Check the side's culture in `result.Run.Reading`: `1.5` on a de-DE side is not a number. |
| Compile reports `UnknownCulture` | The name is not a culture this host knows. A host in invariant-globalization mode, such as the AOT host, knows only `""`. |

`SearchIncomplete` means the search did not establish a complete decision. Do not present it as an ordinary unmatched result.

## Find your way around

| Directory | Purpose |
|---|---|
| [src/RecDiffy](RecDiffy.NET/src/RecDiffy) | Core reconciliation library |
| [src/RecDiffy.RulePacks.Trading](RecDiffy.NET/src/RecDiffy.RulePacks.Trading) | Trading-specific rules |
| [src/RecDiffy.Tests](RecDiffy.NET/src/RecDiffy.Tests) | Behavior tests and usage examples |
| [src/RecDiffy.Benchmarks](RecDiffy.NET/src/RecDiffy.Benchmarks) | Performance benchmarks |
| [src/RecDiffy.AotHost](RecDiffy.NET/src/RecDiffy.AotHost) | Native AOT host |
| [src/RecDiffy.Samples](RecDiffy.NET/src/RecDiffy.Samples) | Runnable demonstration of the application API |
| [src/RecDiffy.Excel](RecDiffy.NET/src/RecDiffy.Excel) | Excel report writer (separate package) |
| [docs/report-specs](RecDiffy.NET/docs/report-specs) | Layout spec and reference workbooks for the Excel reports |
| [docs/ARCHITECTURE-SPEC.md](RecDiffy.NET/docs/ARCHITECTURE-SPEC.md) | Detailed architecture and intended behavior; verify version-sensitive details against the code |

See the [Architecture Overview](https://github.com/m-vaysman/recdiffy.net/wiki/Architecture-Overview) for more background.

## License

RecDiffy.NET is licensed under the [MIT License](LICENSE).
