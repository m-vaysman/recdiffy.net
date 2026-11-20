# Compile a profile and run it

Part of the [engine section](llms.txt). A profile is compiled once and run as often as you like.

```csharp
using RecDiffy;
using RecDiffy.IO;        // FileSources, CsvOptions
using RecDiffy.Results;   // Alignment, FieldOutcome, Disposition

var registry = RuleRegistry.CreateCore();   // TradingRulePack.AddTo(RuleRegistry.CreateCore()) for trading rules
var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText("warehouse.profile.json")), registry);

var csv = new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma };
var result = Reconciler.Run(
    profile,
    FileSources.FromCsv("bins", "bins.csv", csv),
    FileSources.FromCsv("book", "book.csv", csv),
    ReferenceDataSet.Empty);
```

- **`Reconciler.Compile(document, registry)`** checks the whole profile and throws one `ProfileCompileException` whose `Errors` list every `ProfileError(Code, Message)`. Keep the `CompiledProfile` and reuse it for every run. `CompiledProfile.Hash` identifies the profile.
- **`Reconciler.Run`** and **`RunAsync`** take the profile, a left and a right `RecordSource`, a `ReferenceDataSet` (`ReferenceDataSet.Empty` unless a rule looks parties or date formats up), optional `RunOptions` and a `CancellationToken`. A cancelled run throws `OperationCanceledException` and returns nothing.
- **A bad value doesn't stop a run.** Its row is quarantined and the run goes on.
- **Registries.** `RuleRegistry.CreateCore()` holds the core rules. `TradingRulePack.AddTo(registry)`, from `RecDiffy.RulePacks.Trading`, adds the trading rules and attributions.

The warehouse profile and its two CSV files are in the README's [Reconcile two CSV files with a profile](../../README.md#reconcile-two-csv-files-with-a-profile).

## The shape of a JSON profile

Only `formatVersion` (always `1`), `profileId`, `version`, `fields` and `paths` are required.

| Key | What it declares |
|---|---|
| `culture`, `strictDateFormats`, `rounding` | How text is read and how values round. |
| `sources` | Each side's `rowKey` column, and a side's own culture. |
| `fields` | The canonical fields: `name`, `type` (`Decimal`, `Date`, `Symbol`, `Identifier`, `Text`), `role` (`Key`, `Economic`, `Identity`, `Lifecycle`, `Package`, `Text`) and `scale`. |
| `normalization` | Each side's rules, in four phases: `identify`, `parse`, `derive`, `validate`. |
| `consolidation` | Each side's `lifecycle` and `packages`. |
| `paths` | A `route`, the `passes` in order, and `diff.fields` tolerances. |
| `limits` | Bounds on the group search. |

Names and keys are case-sensitive, and an unknown key is silently ignored. Every key is in the [profiles section](../profiles/llms.txt). To build the same document in code, see [ProfileBuilder](../builders/profilebuilder.md); `document.ToJson()` writes any document back to JSON.

## Feed it rows

| Rows come from | Call |
|---|---|
| Objects in memory | `RecordSources.FromObjects(name, rows, new SourceMap<T>().Text(column, row => ...).Decimal(column, row => ...).Date(column, row => ...))`. Also takes an `IAsyncEnumerable<T>`. |
| An `IDataReader` | `RecordSources.FromDataReader(name, () => reader, tabularOptions)` |
| CSV or Excel files | `FileSources.FromCsv` and `FileSources.FromExcel`, in `RecDiffy.IO`. See [files](../io/files.md). |
| Dictionaries of text | `profile.Push(left, right)`. See [push and snapshot](push-and-snapshot.md). |

- **The `name` you give a source is only a label.** In the result, `SourceRef.Source` is always `left` or `right`.
- **What reaches a rule.** From a data reader, whitespace becomes null, text is trimmed, a `DateTime` keeps only its date, integers become decimals, and `float` and `double` pass through so `decimal.parse` can reject them. From objects, each `SourceMap` binder stores what your lambda returns, and `Text` binders trim.
- **Check headers first.** `ProfileCheck.AgainstHeaders(document, leftHeaders, rightHeaders)` lists the columns the profile reads that a file doesn't have, with the closest header as a suggestion.

Next: [read the result](reading-results.md).
