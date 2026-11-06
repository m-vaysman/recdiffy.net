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
| [docs/report-specs](docs/report-specs) | Layout spec and reference workbooks for the Excel reports |
| [docs/ARCHITECTURE-SPEC.md](docs/ARCHITECTURE-SPEC.md) | Detailed architecture and intended behavior; verify version-sensitive details against the code |

See the [Architecture Overview](https://github.com/m-vaysman/recdiffy.net/wiki/Architecture-Overview) for more background.

## License

RecDiffy.NET is licensed under the [MIT License](../LICENSE).
