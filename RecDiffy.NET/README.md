![RecDiffy.NET](https://raw.githubusercontent.com/m-vaysman/recdiffy.net/main/RecDiffy.NET/resources/logos/columns-dark.jpg)

# RecDiffy.NET

[![NuGet](https://img.shields.io/nuget/vpre/RecDiffy?label=NuGet)](https://www.nuget.org/packages/RecDiffy)
[![CI](https://github.com/m-vaysman/recdiffy.net/actions/workflows/ci.yml/badge.svg)](https://github.com/m-vaysman/recdiffy.net/actions/workflows/ci.yml)
[![Benchmarks](https://img.shields.io/badge/benchmarks-Bencher-0b7285)](https://bench.cobblerhill.net/perf/recdiffy)

Reconciliation for two datasets: match every record, show every difference, and explain every unmatched row.

> **Prerelease.** Every RecDiffy release is a prerelease for now, and the API may change from one preview to the next. Pin the version you build against.

RecDiffy.NET is a reconciliation and diffing library for .NET. Use it when records from two systems represent the same underlying activity but differ in identifiers, formatting, or grouping—for example, warehouse bins versus book inventory, or internal trades versus broker records.

A reconciliation runs through:

**Normalize → Consolidate → Match → Compare → Explain**

Matching determines which records belong together. Comparison determines whether their field values agree.

## What is reconciliation?

Reconciliation is the accounting and data-engineering control that proves two records of the same activity agree. A ledger and a broker statement, a warehouse count and a stock book, or two database extracts are meant to describe the same events, so every row should have a counterpart and every amount should match. Reconciling is how you find the ones that do not.

A reconciliation answers five questions, and RecDiffy.NET answers each one:

| Question | What you get |
|---|---|
| Which records belong together? | Matching, even when identifiers, formatting or grouping differ: one row to one row, several rows to one, or a subset that adds up. |
| Do their values agree? | Field-by-field comparison at each declared tolerance, so `Exact`, `WithinTolerance` and `Break` are distinct outcomes. |
| Which rows are unmatched, on which side? | Every entry is `Matched`, `LeftOnly`, `RightOnly`, `Ambiguous` or `SearchIncomplete`, and every input row has a disposition. |
| Where is the difference, and how much of it? | Grouping, packages, lifecycle collapse and attributions, with totals and subsets that balance. |
| Can I prove the result to someone else? | A deterministic result with a profile hash and an engine version, and Excel reports that tie every number back to the files. |

That makes it the .NET tool for data reconciliation, trade reconciliation and ledger matching, record linkage, data quality checks, and revenue or billing controls, whether the input is CSV, Excel, or your own objects in memory.

## Install

RecDiffy is on nuget.org. Every release is a prerelease, so `dotnet add package` needs `--prerelease`:

```shell
dotnet add package RecDiffy --prerelease
```

| Package | What it adds |
|---|---|
| [RecDiffy](https://www.nuget.org/packages/RecDiffy) | The engine: profiles, matching, comparison and results |
| [RecDiffy.IO](https://www.nuget.org/packages/RecDiffy.IO) | Reads CSV and Excel files as record sources |
| [RecDiffy.Excel](https://www.nuget.org/packages/RecDiffy.Excel) | Writes formatted Excel reports from a result |
| [RecDiffy.RulePacks.Trading](https://www.nuget.org/packages/RecDiffy.RulePacks.Trading) | Normalization rules for trade reconciliations |

- **Runtimes.** The packages target .NET 8 and .NET 10, so any app on .NET 8 or later can use them.
- **Versions.** The four packages ship together. Use the same version of each.

## Quickstart: your first reconciliation

This builds a console app that reconciles a warehouse's bin counts against its stock book. You need the .NET SDK, version 8 or later, and nothing from the repository. It was checked against `1.2.0-preview.3`.

**1. Create the project.**

```shell
dotnet new console -n Warehouse
cd Warehouse
dotnet add package RecDiffy --version 1.2.0-preview.3
dotnet add package RecDiffy.IO --version 1.2.0-preview.3
```

The versions are pinned because a preview can change the API. Use `--prerelease` instead of `--version` to take the newest one.

**2. Add the two files.** The warehouse counts stock by bin, and the book holds one position per SKU. Save both files next to `Warehouse.csproj`.

`bins.csv`:

```csv
Bin,Sku,Quantity
A1,WIDGET,4
A2,WIDGET,6
B1,GADGET,3
C1,GIZMO,2
```

`book.csv`:

```csv
Position,Sku,Quantity
BOOK-1,WIDGET,10
BOOK-2,GADGET,5
```

**3. Add the profile.** A profile is the policy for a reconciliation: which fields matter, how each file's columns become those fields, and how rows are matched. Save this as `warehouse.profile.json`:

```json
{
  "formatVersion": 1,
  "profileId": "warehouse",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": {
    "left":  { "rowKey": "Bin" },
    "right": { "rowKey": "Position" }
  },
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
  "paths": [
    {
      "id": "stock",
      "route": { "rule": "route.all" },
      "passes": [
        { "id": "bins", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] },
        { "id": "sku", "pass": "match.naturalKey", "key": ["Sku"] }
      ]
    }
  ]
}
```

- **`culture`** says how the text in both files is read, so the result is the same on every machine.
- **`sources`** names the column that identifies each row: `Bin` in the bins file, `Position` in the book.
- **`fields`** declares `Sku` as the key and `Quantity` as an economic field. Economic fields are compared once rows match.
- **`normalization`** maps each file's columns to those fields. Here both files happen to use the field names.
- **`paths`** lists the matching passes, tried in order. The `bins` pass matches several bins whose quantities add up to one book position. The `sku` pass then pairs what is left by SKU, one to one.

**4. Replace `Program.cs`:**

```csharp
using RecDiffy;
using RecDiffy.IO;
using RecDiffy.Results;

// 1. Compile the profile once.
var profile = Reconciler.Compile(
    ProfileDocument.Parse(File.ReadAllText("warehouse.profile.json")),
    RuleRegistry.CreateCore());

// 2. Read each file as a record source.
var csv = new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma };
var bins = FileSources.FromCsv("bins", "bins.csv", csv);
var book = FileSources.FromCsv("book", "book.csv", csv);

// 3. Run the reconciliation and print every entry.
var result = Reconciler.Run(profile, bins, book, ReferenceDataSet.Empty);

var summary = result.Summary;
Console.WriteLine($"Matched {summary.Matched} ({summary.Break} with breaks) | Left only {summary.LeftOnly} | Right only {summary.RightOnly}");

foreach (var entry in result.Entries)
{
    Console.WriteLine($"{entry.Alignment,-9} {entry.Composition,-9} {entry.Outcome,-11} {Keys(entry.Left)} | {Keys(entry.Right)}");
    foreach (var diff in entry.Diffs.Where(diff => diff.Outcome == FieldOutcome.Break))
        Console.WriteLine($"    {diff.Field}: {result.Format(diff.Left)} vs {result.Format(diff.Right)}");
}

static string Keys(IReadOnlyList<SourceRef> rows) =>
    rows.Count == 0 ? "-" : string.Join(" + ", rows.OrderBy(row => row.Ordinal).Select(row => row.RowKey));
```

**5. Run it** from the project folder, where the three files are:

```shell
dotnet run
```

```text
Matched 2 (1 with breaks) | Left only 1 | Right only 0
Matched   ManyToOne Exact       A1 + A2 | BOOK-1
Matched   OneToOne  Break       B1 | BOOK-2
    Quantity: 3 vs 5
LeftOnly  None      NotCompared C1 | -
```

- **A1 + A2 and BOOK-1:** the `bins` pass matched two bins to one position, because 4 + 6 = 10. The quantities agree, so the outcome is `Exact`.
- **B1 and BOOK-2:** no group of bins adds up to 5, so the `sku` pass paired them by SKU. They match, but their quantities differ, so the outcome is `Break`.
- **C1:** no book position holds `GIZMO`, so the bin is left only.

To reconcile objects you already hold in memory, replace the two `FileSources.FromCsv` calls with `RecordSources.FromObjects` and a `SourceMap` for each type. [FirstReconciliation.cs](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/src/RecDiffy.Samples/FirstReconciliation.cs) shows how.

## Documentation

- **[What a result means](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/docs/RESULTS.md):** how to read a result.
- **[Profile reference](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/docs/PROFILES.md):** what a profile can declare.
- **[Support matrix](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/docs/SUPPORT.md):** what each package supports.
- **[Capacity and concurrency](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/docs/CAPACITY.md):** how large a reconciliation can be, and how many can run at once.
- **[Typed reconciliation](https://github.com/m-vaysman/recdiffy.net/blob/main/docs/typed-reconciliation/README.md):** `RecBuilder` with your own objects, step by step.
- **[Excel report layouts](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/docs/report-specs/README.md):** what each Excel report shows.

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
- **Consolidation:** optional lifecycle and package processing before matching. See [Consolidation](https://github.com/m-vaysman/recdiffy.net/blob/main/README.md#consolidation).
- **Routes:** which matching path receives each row.
- **Matching passes:** ordered strategies for finding corresponding records.
- **Comparison rules:** fields to compare and tolerances to apply.
- **Search limits:** bounds on group matching.

Compile the profile once, then pass the compiled profile to reconciliation runs. Start with the warehouse profile in the quickstart above before adapting it to your own fields and source mappings.

## Write a profile without JSON

`ProfileBuilder` builds the same `ProfileDocument` that `ProfileDocument.Parse` reads. Column names are strings; fields, rules, passes and tolerances are methods. Each rule lands in its phase (identify, parse, derive, validate) whatever order you call it in.

```csharp
var built = ProfileBuilder.Create("warehouse", "1.0.0")
    .RowKeys(left: "Id", right: "Id")
    .Key("Sku", FieldType.Symbol)
    .Economic("Quantity", FieldType.Decimal, scale: 0)
    .Left(side => side.MapSymbol("Sku", "Sku").ParseDecimal("Quantity", "Quantity"))
    .Right(side => side.MapSymbol("Sku", "Sku").ParseDecimal("Quantity", "Quantity"))
    .Path("stock", path => path.GroupSum("P3", Side.Left, block: ["Sku"], conserve: ["Quantity"]))
    .Build(RuleRegistry.CreateCore());

if (!built.Succeeded)
{
    foreach (var problem in built.Problems)
        Console.WriteLine($"{problem.Code} at {problem.Location}: {problem.Message}");
    return;
}

var profile = Reconciler.Compile(built.Document, RuleRegistry.CreateCore());
File.WriteAllText("warehouse.profile.json", built.Document.ToJson()); // the reviewed, versioned policy file
```

- **Builders never change.** Every method returns a new builder, so you can share one and branch from it. Each lambda overload also takes a value (`Left(SideBuilder)`, `Path(PathBuilder)`), so a program can drive the builder without lambdas.
- **`Build()` reports problems as data.** Each problem has a stable code from `ProfileProblemCodes`, a message, and a location such as `paths[stock].passes[P3].block[0]`. `Build(registry)` also compiles the document, so a profile that builds also compiles.
- **`ToJson()` writes the saved form.** It writes the keys `Parse` reads in a fixed order, with policy values such as midpoint and limits written out. Parsing it back gives an equal document and the same `ProfileHash`.
- **`ProfileBuilder.From(document)`** loads an existing profile for editing.
- **Trading rules** come from `RecDiffy.RulePacks.Trading` as extension methods (`ResolveLei`, `ResolveAlias`, `ResolveSide`, `AbsoluteQuantity`, `From32nds`). `TradeFields` holds the canonical trade field names.
- **Check a file before reading it.** `ProfileCheck.AgainstHeaders(document, leftHeaders, rightHeaders)` lists every column the profile reads that a file lacks, with the closest header as a suggestion: `'Qty' not found in right; did you mean 'Quantity'?` Pass `TradingColumns.Parameters` when the profile uses trading rules, so their column parameters are checked too.

### Reconcile your own objects

When both sides are lists of your own types, `RecBuilder` reads column names from the members you select and field types from their types, and gives your objects back:

```csharp
var result = RecBuilder.Between(ledger, statement)
    .MatchOn(entry => entry.Ref, line => line.Reference)          // pass P1
    .ThenMatchOn(entry => entry.Date, line => line.Posted)        // pass P2, on what P1 left
    .And(entry => entry.Amount, line => line.Amount)              // P2 matches on date and amount
    .Compare(entry => entry.Amount, line => line.Amount, tolerance: 0.05m)
    .Run();

foreach (var match in result.Breaks)
    Console.WriteLine($"{match.Left[0]} vs {match.Right[0]}");
File.WriteAllText("ledger.profile.json", result.Profile.ToJson());
```

- **Passes:** each `MatchOn` or `ThenMatchOn` is a natural-key pass, tried in order.
- **Field types:** `decimal`, `int` and `long` members are decimals; `DateOnly` and `DateTime` members are dates; a `string` is an identifier in a key and text otherwise. Other types are refused when selected.
- **The result:** `Matched`, `Breaks`, `LeftOnly`, `RightOnly` and `Ambiguous` hold the objects you passed in, and `Result` holds the engine's full result.
- **The profile:** `Profile` is an ordinary `ProfileDocument`. Its JSON, run with `RecordSources.FromObjects` on the same objects, gives the same result.

## Write Excel reports

The separate `RecDiffy.Excel` package writes formatted back- and middle-office reports from an `IReconciliationResult`, one `.xlsx` file per report. Numbers on a report tab are formulas that link to the engine's output in the same file, every decomposition has a ✓/✗ check, and ageing is a formula on a named `AsOf` cell.

It is the third of the four packages, so the quickstart project above does not have it yet. Add it before running this section:

```shell
dotnet add package RecDiffy.Excel --version 1.2.0-preview.3
```

```csharp
using RecDiffy.Excel;

var options = new ExcelReportOptions
{
    AsOf = new DateOnly(2026, 11, 13),
    Profile = profile, // profile id, tolerances, pass order; also maps report roles to fields of the same name
};

foreach (var outcome in result.WriteAll("reports", options))
    Console.WriteLine(outcome.Written ? outcome.Path : $"{outcome.FileName} skipped: {outcome.SkipReason}");
```

| Office | Reports |
|---|---|
| Back office | BO1 ExceptionBlotter, BO2 SideBySideCompare, BO3 RawVsNormalized, BO4 LifecycleChains, BO5 Allocations, BO6 CashLadder |
| Middle office | MO1 ControlTotals, MO2 Scorecard, MO3 CascadeFunnel, MO4 BreakHeatmap, MO5 Ageing, MO6 ToleranceUsage, MO7 AuditTrail |

Files are named `<ID>_<ReportName>_<profileId>_<asOf yyyy-MM-dd>.xlsx`. Reports read fields only through `FieldMap`; a report whose required roles are unmapped, or that has nothing to show, is skipped and the reason is returned. `RecDiffyExcel.RunAndWrite` runs the engine and writes in one call, and `WriteReport` writes one report to a stream. `Labels` replaces report wording with your own column names. The layout spec is in [docs/report-specs](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/docs/report-specs/README.md).

A run writes into the as-of date, so a second run on the same day silently replaces the workbooks already in the folder. Pick a new folder, or a new `AsOf`, when you want to keep both.

### Report roles

`FieldMap.FromProfile` looks for each role's field names in the order below, and maps the role only when the profile declares one. Any other name needs an explicit `With`, which sets the role's field and replaces any field `FromProfile` found: `FieldMap = FieldMap.FromProfile(profile).With(ReportRole.Counterparty, "Broker")`. `With` does not check the name against the profile. A field the profile does not declare still counts as mapped, so the report writes with that role's values empty instead of skipping. A role a report needs, but the map does not supply, skips that report and names it.

| Role | Field names `FromProfile` looks for | Required by |
|---|---|---|
| `Counterparty` | `Counterparty` | BO1, BO2, MO1, MO2, MO4 |
| `Instrument` | `Instrument` | BO1, BO2 |
| `Asset` | `Asset`, then `AssetClass` | MO1 |
| `Side` | `Side` | BO1, BO2, BO6 |
| `TradeDate` | `TradeDate` | BO1, MO5 |
| `SettleDate` | `SettleDate` | BO1, BO6, MO5 |
| `Quantity` | `Quantity` | BO5, MO1 |
| `Price` | `Price` | none; shown by BO2, and by BO5 beside the quantity |
| `Currency` | `Currency` | BO1, BO2, BO6, MO1 |
| `NetCash` | `NetCash` | BO6 |
| `Accrued` | `Accrued` | none; shown by BO2 and BO5 |
| `Fees` | `Fees` | none; shown by BO2 and BO5 |
| `Book` | `Book` | none; one line per allocation on BO5 |
| `LifecycleAction` | `EventAction` | BO4 |
| `LifecycleTarget` | `EventTarget` | BO4 |

- **The quickstart profile maps only `Quantity`.** Run against it with `Profile` set, as the snippet above does, 4 of the 13 reports write: BO3, BO5, MO3 and MO7. The other 9 skip, and each reason names the roles it wanted. Declare the trade-shaped fields in this table to reach the rest.
- **BO3 RawVsNormalized and MO6 ToleranceUsage need the profile, not a role.** BO3 reads each side's original columns, and MO6 reads the tolerance amounts; both come from `ExcelReportOptions.Profile`.
- **BO2 needs at least one compared field mapped:** one of `Quantity`, `Price`, `SettleDate`, `Accrued`, `Fees` or `NetCash`.
- **MO3 CascadeFunnel and MO7 AuditTrail need no role at all.** They report the engine's own counts and explanation trail.
- **A report also needs something to show.** BO3 skips when no one-to-one match broke, landed within tolerance, or had a value changed by normalization. BO5 skips when no entry was matched many-to-one or one-to-many. MO6 skips when no difference landed `WithinTolerance`. MO7 skips when every entry is an exact match with no lifecycle chain or package behind it. Each returns that reason, so a mapped role alone does not produce a file. `SelectedEntries` makes BO3 and MO7 show the entries you name instead.
- **BO6 signs cash by `Side`.** A row whose `Side` is in `ExcelReportOptions.PayingSides` pays, so its net cash counts negative, and every other side receives. The default is `BUY`, `PAY_FIXED` and `PAY`, compared exactly with the canonical value, so a profile whose sides are `B` and `S` needs `PayingSides = ["B"]`, or every row lands as a receipt.

BO4 needs lifecycle consolidation, not just the two roles. It draws the chains that `consolidation.lifecycle` collapsed, so a profile without that block writes no BO4 rows. The block names the fields holding each event's action (`NEW`, `CANC`, `CORR`) and the row key it acts on, and the `scope` field that groups row keys so a target is looked up inside the same counterparty:

```json
"consolidation": {
  "right": {
    "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" }
  }
}
```

The collapsed rows keep a disposition of `Suppressed`, and the survivor carries the `LifecycleCollapsed` composition flag. [Consolidation](https://github.com/m-vaysman/recdiffy.net/blob/main/README.md#consolidation) in the repository README has a worked example, and [`consolidation` in PROFILES.md](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/docs/PROFILES.md#consolidation-lifecycles-and-packages) has every key and diagnostic code.

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
- **Values pass through.** CSV cells reach the profile as text, so all parsing stays in the profile as policy. Excel cells keep the type the workbook stores: a number (as a `decimal`), a date, or text, so a text ID such as `00123` keeps its leading zeros. A number no `decimal` holds exactly, because it is too large or has more than 28 significant digits, stays a `double` and is `Invalid` with `FloatValue`.
- **Each side is independent.** CSV against Excel, Excel against CSV, and a file against objects or another data reader all work.

**Sheet numbers start at 1.** `ExcelSheet.Numbered(1)` is the first tab and `ExcelSheet.Numbered(2)` the second, because that is how users count tabs in Excel. This is intentional, not an off-by-one error. Hidden tabs count, in workbook order. Sheet 0, a number past the last tab, or a name that does not exist fails with a message that lists the sheets the workbook has; no other sheet is read instead. `FileSources.ExcelSheetNames(path)` lists them in tab order.

A path is reopened for every run. A stream can be read once and stays open, because the caller owns it; give its `ExcelFormat` explicitly. `FileSources.PeekCsv` and `PeekExcel` return the headers and a seeded random sample of rows from across the whole file, the same sample for the same seed.

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| `dotnet add package` says there are no stable versions | Every release is a prerelease. Add `--prerelease`, or pin one with `--version`. |
| Build cannot find a compatible SDK | Compare `dotnet --list-sdks` with [global.json](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/global.json). |
| Profile compilation fails | Inspect the errors on `ProfileCompileException`. |
| A row is quarantined | Inspect its diagnostics for missing keys or invalid values. |
| Rows remain unmatched | Check normalized key values, routing, and pass configuration. |
| A component reports `BlockTooLarge` | Inspect its size and blocking fields before increasing limits. |
| Records match but show `Break` | Inspect field differences and configured tolerances. |

`SearchIncomplete` means the search did not establish a complete decision. Do not present it as an ordinary unmatched result.

## Run the samples

The repository has a sample program that goes further than the quickstart: a quarantined row, a side-by-side view, an async run, a trading-pack run that uses reference data, Excel reports, a profile built in code, and `RecBuilder` with typed objects. It needs a clone, and the .NET SDK that [global.json](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/global.json) pins.

```shell
git clone https://github.com/m-vaysman/recdiffy.net.git
cd recdiffy.net/RecDiffy.NET
dotnet run --project src/RecDiffy.Samples/RecDiffy.Samples.csproj
```

The clone checks out `main`, which holds the latest release. The first example reconciles two bins against one book position, built from objects: [FirstReconciliation.cs](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/src/RecDiffy.Samples/FirstReconciliation.cs). `Matches_warehouse_bins_to_book_stock_without_the_trading_pack` in [VerificationTests.cs](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/src/RecDiffy.Tests/VerificationTests.cs) checks the same behavior:

```shell
dotnet test src/RecDiffy.Tests/RecDiffy.Tests.csproj --filter FullyQualifiedName~Matches_warehouse_bins_to_book_stock_without_the_trading_pack
```

## Contribute

To build RecDiffy from source, test it or change it, follow [CONTRIBUTING.md](https://github.com/m-vaysman/recdiffy.net/blob/main/CONTRIBUTING.md). It covers the SDK, the build, the branch to start from, and the rules for a change.

## Find your way around

| Directory | Purpose |
|---|---|
| [src/RecDiffy](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/src/RecDiffy) | Core reconciliation library |
| [src/RecDiffy.RulePacks.Trading](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/src/RecDiffy.RulePacks.Trading) | Trading-specific rules |
| [src/RecDiffy.Tests](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/src/RecDiffy.Tests) | Behavior tests and usage examples |
| [src/RecDiffy.Benchmarks](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/src/RecDiffy.Benchmarks) | Performance benchmarks |
| [src/RecDiffy.AotHost](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/src/RecDiffy.AotHost) | Native AOT host |
| [src/RecDiffy.Samples](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/src/RecDiffy.Samples) | Runnable demonstration of the application API |
| [src/RecDiffy.Excel](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/src/RecDiffy.Excel) | Excel report writer (separate package) |
| [src/RecDiffy.IO](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/src/RecDiffy.IO) | CSV and Excel file readers (separate package) |
| [docs/report-specs](https://github.com/m-vaysman/recdiffy.net/tree/main/RecDiffy.NET/docs/report-specs) | Layout spec and reference workbooks for the Excel reports |
| [docs/SPEC-STATUS.md](https://github.com/m-vaysman/recdiffy.net/blob/main/RecDiffy.NET/docs/SPEC-STATUS.md) | Which architecture spec is in force, and the shipped behavior that amends it; the committed v1 spec is historical |

See the [Architecture Overview](https://github.com/m-vaysman/recdiffy.net/wiki/Architecture-Overview) for more background.

## License

RecDiffy.NET is licensed under the [MIT License](https://github.com/m-vaysman/recdiffy.net/blob/main/LICENSE).
