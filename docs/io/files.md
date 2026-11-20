# Read CSV and Excel files

Part of the [I/O section](llms.txt). The `RecDiffy.IO` package turns a CSV file or an Excel sheet into a `RecordSource` for `Reconciler.Run`.

```shell
dotnet add package RecDiffy.IO --prerelease
```

```csharp
using RecDiffy;
using RecDiffy.IO;

var left = FileSources.FromCsv("blotter", "blotter.csv",
    new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma });
var right = FileSources.FromExcel("confirms", "confirms.xlsx",
    new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Named("Confirms") });

var result = Reconciler.Run(profile, left, right, ReferenceDataSet.Empty);
```

Left and right are independent sources, so any mix of formats works.

## FileSources

| Method | What it does |
|---|---|
| `FromCsv(name, path, options)`, `FromCsv(name, stream, options)` | A CSV source. |
| `FromExcel(name, path, options)`, `FromExcel(name, stream, format, options)` | One sheet of a workbook. A path's extension gives the format; a stream needs an `ExcelFormat`: `Xlsx`, `Xlsb` or `Xls`. |
| `ExcelSheetNames(path)` | The sheet names in tab order, hidden sheets included. Sheet number n is item n − 1. |
| `PeekCsv(path or stream, options, sampleSize, seed)`, `PeekExcel(...)` | A `TabularPeek`: `Headers`, a seeded random sample of `Rows` drawn from across the whole file, and `RowCount`. The same file, options, size and seed always give the same sample. |

- **A path is reopened for every run.** A stream is read once and stays open: the caller owns it.
- **Nothing is parsed, detected or repaired here.** CSV cells reach the profile as text; Excel cells as the number, date or text the workbook stores. The profile's rules do the rest.

## CsvOptions

| Property | Default | What it sets |
|---|---|---|
| `HasHeader` | Required | `true`: row 1 holds column names. `false`: columns are named `Column1` to `ColumnN`, and every row is data. |
| `Delimiter` | Required | `CsvDelimiter.Comma`, `Tab`, `Pipe`, `Semicolon`, `CsvDelimiter.Of(char)`, or `CsvDelimiter.DetectAutomatically`, which is used only when you choose it. |
| `Quote` | `"` | The quote character. |
| `Escape` | `"` | The character that escapes a quote inside a quoted field. The default doubles the quote, as RFC 4180 does. |
| `Encoding` | UTF-8 | The text encoding. Its byte-order mark is skipped, no other encoding is detected, and invalid bytes fail the read rather than being replaced. |

## ExcelOptions

| Property | Default | What it sets |
|---|---|---|
| `HasHeader` | Required | `true`: row 1 holds column names. Data starts at cell A1; there is no setting for another header row or start cell. |
| `Sheet` | Required | `ExcelSheet.Named("Tab name")`, or `ExcelSheet.Numbered(n)`, which counts tabs from 1, hidden tabs included. |

- **Excel numbers arrive as the `decimal` the workbook stores.** Only a number no `decimal` holds exactly, too large or past 28 significant digits, stays a `double`, and `decimal.parse` rejects it with `FloatValue`.
- **Excel dates arrive as dates.** `date.parse` reads a `DateOnly` or `DateTime` directly, and a `double` as an Excel date serial.

## Other sources

| Rows come from | Call |
|---|---|
| Objects in memory | `RecordSources.FromObjects(name, rows, new SourceMap<T>().Text(...).Decimal(...).Date(...))`, in `RecDiffy` |
| An `IDataReader` | `RecordSources.FromDataReader(name, () => reader, tabularOptions)` |
| Dictionaries of text | `profile.Push(left, right)`: see [push and snapshot](../engine/push-and-snapshot.md) |

Before reading files, `ProfileCheck.AgainstHeaders(document, leftHeaders, rightHeaders)` lists the columns the profile reads that a file doesn't have, with the closest header as a suggestion. Pass `TradingColumns.Parameters` as the last argument when the profile uses the trading pack.

The code is [RecDiffy.IO](../../RecDiffy.NET/src/RecDiffy.IO), and [FileReaders.cs](../../RecDiffy.NET/src/RecDiffy.Samples/FileReaders.cs) is a runnable sample.
