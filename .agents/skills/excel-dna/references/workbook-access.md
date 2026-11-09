# Workbook access without COM

Every call here needs a macro context: schedule it with `ExcelAsyncUtil.QueueAsMacro` when it starts from WPF, a ribbon callback, or background work. Snapshot plain values inside that context, and do the expensive work outside it.

## Open another workbook in the running Excel instance

Use the C API command `xlcOpen`, then read an explicitly identified range with `ExcelReference.GetValue()`. Don't default to Office interop or an external reader package.

1. **Resolve the full path and check whether that exact workbook is already open.** Compare identity, not just the file name: Excel can refuse to open two different files with the same name.
2. **If it's already open,** preserve its save state and don't take ownership of closing it. Say plainly that reads may include unsaved live values.
3. **Otherwise, open it in a macro context:**

   ```csharp
   XlCall.TryExcel(XlCall.xlcOpen, out var result, path, 0, true);
   ```

   - **Arguments:** the positional arguments are the file name, the update-links setting, and the read-only setting. `0` asks for no updates of external or remote links, and `true` asks for read-only.
   - **Check both results:** the C API return status and the command's result.
   - **Limits:** this doesn't guarantee a silent open, and it doesn't disable macros or events.
   - **Activation:** `xlcOpen` makes the opened workbook active, which changes what the user sees. To avoid flicker, consider turning screen updating off with `xlcEcho` and back on afterwards, inside the same macro context.
4. **Get and verify the workbook and sheet identity** through documented information functions, such as `xlfDocuments` (open workbooks) and `xlfGetWorkbook` (a workbook's sheets). Don't guess numeric selectors.
5. **Resolve the selected sheet to its `SheetId`,** with `xlSheetId` or with the `ExcelReference` constructor that takes a sheet name. Quote and escape names correctly.
6. **Read a bounded rectangle,** using the explicit sheet identity and zero-based row and column indices. See "Finding the bounds" below.
7. **Snapshot values and their source coordinates into plain domain records,** converting them through the startup-registered conversions, as `SKILL.md` describes. Do the expensive matching and comparison outside the Excel context.
8. **Close only a workbook this operation opened and still owns.**
   - As far as I know, `xlcClose` acts on the active window. Activate the workbook you own first (`xlcActivate`), confirm it's now the active one, and only then close it.
   - Never close whichever workbook happens to be active.
   - Restore the user's previous active workbook and sheet where you can.

Cleanup must respect abort and shutdown states. Don't make unsafe Excel calls from an unconditional `finally` block.

## Read a workbook that's already open

Resolve the workbook and sheet identity explicitly, then read bounded ranges through the C API. Keep the meaning of unsaved live data.

A minimal read, assuming a macro context and a valid sheet ID:

```csharp
using ExcelDna.Integration;

static object ReadBlock(IntPtr sheetId, int firstRow, int lastRow, int firstColumn, int lastColumn)
{
    var reference = new ExcelReference(firstRow, lastRow, firstColumn, lastColumn, sheetId);
    return reference.GetValue();
}
```

- **Shape:** a one-cell result may be a scalar, and a multi-cell result is normally `object[,]`. Normalize the shape deliberately.
- **No conversion:** `GetValue()` is a bare `xlCoerce` and applies no registered conversion.
- **Uncalculated cells:** `GetValue()` is reported to fail on a reference that hasn't been calculated yet, for example in manual calculation mode. Handle that failure instead of retrying blindly.
- **Host operations:** building a reference from a sheet name resolves that name through Excel, which is itself a host operation. Holding a reference doesn't make its methods safe on a worker thread.
- **Lifetime:** sheet IDs become invalid when the workbook closes.

## Finding the bounds

Never read a whole sheet or an unbounded range. The options:

- **Prefer a range the user chose.** Let the user pick or confirm the range in the UI, then store it in the setup as an address (see #193).
- **Excel's used-range information:** the macro information function `GET.DOCUMENT` (`XlCall.xlfGetDocument`) reportedly returns the first and last used row and column. But sources disagree about which numbers mean what: some list 9 to 12 as the used-range values, while one old reference lists chart values there. Check the numbers in Excel before relying on them, and record what you found in [source-evidence.md](source-evidence.md).
- **Treat the used range as an upper bound only.** Excel's used range can include cells that were formatted but are empty.

## Excel tables (ListObjects)

The C API's information functions come from the Excel 4 macro language, which predates Excel tables. I found no C API function that lists the tables in a workbook or returns a table's range. Before promising table selection, choose one:

- the user selects the table's range, and the add-in reads it as a range;
- evaluate a known table's structured reference, such as `Table1[#All]`, through `xlfEvaluate`, which is untested, and confirm it in Excel first;
- ask the lead for permission to read tables through COM (`ListObjects`).

## Write to Excel

Prefer bounded bulk `ExcelReference.SetValue()` calls in a macro context. Write to a new sheet or range that the add-in owns. Preserve source data, check outcomes, and avoid loops that write one cell at a time or coerce a whole sheet.

## Read a saved file without opening Excel

Use a managed reader when you need headless processing, bulk streaming, or must avoid the side effects of opening a file in Excel. It's an alternative to the Excel-hosted workflow, not its default.

Reuse existing dependencies first. In this repository, `RecDiffy.IO` already reads CSV through Sylvan.Data.Csv and workbooks through Sylvan.Data.Excel. ExcelDataReader is another option. Neither library is built into Excel-DNA.

What to know about saved-file readers:

- **They read the saved bytes,** not unsaved edits in a workbook.
- **They don't recalculate formulas,** so a formula's cached result may be missing or stale.
- **They need explicit rules** for schema, header, culture, nulls, errors and dates.
- **Keep source coordinates,** and dispose any stream you own.
- **Decide deliberately what happens** with locked, malformed or encrypted files. A file Excel has open may be locked for writing. Open it with `FileShare.ReadWrite` and pass the stream in.

For CSV, use a parser that handles quoting, encoding and culture, never `Split(',')`.
