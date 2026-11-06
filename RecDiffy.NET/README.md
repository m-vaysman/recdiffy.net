<p align="center">
  <img alt="RecDiffy.NET" src="resources/logos/columns-dark.jpg" width="640" />
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

## Prerequisites

- .NET SDK **10.0.403**, or a compatible version permitted by [global.json](global.json).
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

Run the sample:

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

The complete program for that outline is [FirstReconciliation.cs](src/RecDiffy.Samples/FirstReconciliation.cs). The same behavior is checked by `Matches_warehouse_bins_to_book_stock_without_the_trading_pack` in [VerificationTests.cs](src/RecDiffy.Tests/VerificationTests.cs):

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
- **Normalization:** how source values become canonical values.
- **Consolidation:** optional lifecycle and package processing before matching.
- **Routes:** which matching path receives each row.
- **Matching passes:** ordered strategies for finding corresponding records.
- **Comparison rules:** fields to compare and tolerances to apply.
- **Search limits:** bounds on group matching.

Compile the profile once, then pass the compiled profile to reconciliation runs. Start with the warehouse profile in the sample above before adapting it to your own fields and source mappings.

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

Files are named `<ID>_<ReportName>_<profileId>_<asOf yyyy-MM-dd>.xlsx`. Reports read fields only through `FieldMap`; a report whose required roles are unmapped, or that has nothing to show, is skipped and the reason is returned. `RecDiffyExcel.RunAndWrite` runs the engine and writes in one call, and `WriteReport` writes one report to a stream. `Labels` replaces report wording with your own column names. The layout spec is in [docs/report-specs](docs/report-specs/README.md).

## Read CSV and Excel files

The separate `RecDiffy.IO` package reads delimited text and Excel workbooks (`.xlsx`, `.xlsb`, `.xls`) as record sources. Parsing is done by Mark Pflug's [Sylvan.Data.Csv](https://www.nuget.org/packages/Sylvan.Data.Csv) and [Sylvan.Data.Excel](https://www.nuget.org/packages/Sylvan.Data.Excel) (MIT, no further dependencies); the core `RecDiffy` package stays free of NuGet dependencies.

```csharp
using RecDiffy.IO;

var left = FileSources.FromCsv("book", "book.csv", new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma });
var right = FileSources.FromExcel("street", "street.xlsx", new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Named("Trades") });
var result = Reconciler.Run(profile, left, right, referenceData);
```

**Nothing is guessed.** Input files are expected to be correct; the readers do not detect, infer or repair anything.

- **You state what the file is.** `HasHeader`, the CSV `Delimiter` and the Excel `Sheet` are required, so leaving one out does not compile. Delimiter detection is available only as `CsvDelimiter.DetectAutomatically`, chosen explicitly.
- **With a header,** row 1 names the columns and every later row is data. A header row with an empty or repeated name is refused, with a message naming the columns.
- **Without a header,** columns are named `Column1` to `ColumnN` and every row, including the first, is data, so profiles map columns by position.
- **Excel data starts at cell A1** of the chosen sheet. Blank rows and columns are not skipped to find a table.
- **Values pass through.** CSV cells reach the profile as text, so all parsing stays in the profile as policy. Excel cells keep the type the workbook stores: a number (as a double), a date, or text, so a text ID such as `00123` keeps its leading zeros.
- **Each side is independent.** CSV against Excel, Excel against CSV, and a file against objects or another data reader all work.

**Sheet numbers start at 1.** `ExcelSheet.Numbered(1)` is the first tab and `ExcelSheet.Numbered(2)` the second, because that is how users count tabs in Excel. This is intentional, not an off-by-one error. Hidden tabs count, in workbook order. Sheet 0, a number past the last tab, or a name that does not exist fails with a message that lists the sheets the workbook has; no other sheet is read instead. `FileSources.ExcelSheetNames(path)` lists them in tab order.

A path is reopened for every run. A stream can be read once and stays open, because the caller owns it; give its `ExcelFormat` explicitly. `FileSources.PeekCsv` and `PeekExcel` return the headers and a seeded random sample of rows from across the whole file, the same sample for the same seed.

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| Build cannot find a compatible SDK | Compare `dotnet --list-sdks` with [global.json](global.json). |
| Profile compilation fails | Inspect the errors on `ProfileCompileException`. |
| A row is quarantined | Inspect its diagnostics for missing keys or invalid values. |
| Rows remain unmatched | Check normalized key values, routing, and pass configuration. |
| A component reports `BlockTooLarge` | Inspect its size and blocking fields before increasing limits. |
| Records match but show `Break` | Inspect field differences and configured tolerances. |

`SearchIncomplete` means the search did not establish a complete decision. Do not present it as an ordinary unmatched result.

## Find your way around

| Directory | Purpose |
|---|---|
| [src/RecDiffy](src/RecDiffy) | Core reconciliation library |
| [src/RecDiffy.RulePacks.Trading](src/RecDiffy.RulePacks.Trading) | Trading-specific rules |
| [src/RecDiffy.Tests](src/RecDiffy.Tests) | Behavior tests and usage examples |
| [src/RecDiffy.Benchmarks](src/RecDiffy.Benchmarks) | Performance benchmarks |
| [src/RecDiffy.AotHost](src/RecDiffy.AotHost) | Native AOT host |
| [src/RecDiffy.Samples](src/RecDiffy.Samples) | Runnable demonstration of the application API |
| [src/RecDiffy.Excel](src/RecDiffy.Excel) | Excel report writer (separate package) |
| [src/RecDiffy.IO](src/RecDiffy.IO) | CSV and Excel file readers (separate package) |
| [docs/report-specs](docs/report-specs) | Layout spec and reference workbooks for the Excel reports |
| [docs/ARCHITECTURE-SPEC.md](docs/ARCHITECTURE-SPEC.md) | Detailed architecture and intended behavior; verify version-sensitive details against the code |

See the [Architecture Overview](https://github.com/m-vaysman/recdiffy.net/wiki/Architecture-Overview) for more background.

## License

RecDiffy.NET is licensed under the [MIT License](../LICENSE).
