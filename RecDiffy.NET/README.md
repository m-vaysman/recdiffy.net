<p align="center">
  <img alt="RecDiffy.NET" src="resources/logos/columns-dark.jpg" width="640" />
</p>

# RecDiffy.NET

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

Run the existing warehouse example as a focused test:

```powershell
dotnet test src/RecDiffy.Tests/RecDiffy.Tests.csproj --filter FullyQualifiedName~Matches_warehouse_bins_to_book_stock_without_the_trading_pack
```

Expected result: the test passes, asserting one matched entry with `ManyToOne` composition and an `Exact` comparison outcome. All three source rows receive a `Matched` disposition.

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

For the complete working example, see `Matches_warehouse_bins_to_book_stock_without_the_trading_pack` in [VerificationTests.cs](src/RecDiffy.Tests/VerificationTests.cs). That file includes the profile JSON, source mappings, input helpers, and assertions. The outline above is not a standalone program.

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

Compile the profile once, then pass the compiled profile to reconciliation runs. Start with the warehouse profile in the test above before adapting it to your own fields and source mappings.

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
| [docs/ARCHITECTURE-SPEC.md](docs/ARCHITECTURE-SPEC.md) | Detailed architecture and intended behavior; verify version-sensitive details against the code |

See the [Architecture Overview](https://github.com/m-vaysman/recdiffy.net/wiki/Architecture-Overview) for more background.

## License

RecDiffy.NET is licensed under the [MIT License](../LICENSE).
