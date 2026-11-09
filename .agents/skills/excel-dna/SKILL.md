---
name: excel-dna
description: Build, review, and troubleshoot Excel-DNA add-ins. Covers C API workbook access without COM, startup conversion registration, Excel execution context, WPF windows hosted in Excel, and built-in Excel-DNA logging. Use for Excel-DNA code and architecture, including the RecDiffy Excel add-in (#192). Not for ordinary spreadsheet editing, and not for the RecDiffy.Excel report writer, which uses OpenXml.
---

# Excel-DNA engineering

Follow the installed Excel-DNA version's own source and samples. Prefer what Excel-DNA already provides over new dependencies. Keep domain logic independent of Excel, and make conversions and host interactions explicit.

I reviewed this guidance against the Excel-DNA v1.9 source (tag `v1.9.0-Release`) on November 9, 2026. The examples haven't been compiled or run inside Excel. Check them against the project's actual packages and runtime. [references/source-evidence.md](references/source-evidence.md) lists which statements I checked in the source and which ones only come from reports.

Load a reference when the task needs it:

| Reference | Load it for |
|---|---|
| [references/workbook-access.md](references/workbook-access.md) | Opening, reading, writing and closing workbooks; finding a range's bounds; Excel tables; saved-file readers |
| [references/registration.md](references/registration.md) | Worksheet functions and commands, all Excel-DNA attributes, automatic and explicit registration, conversion order |
| [references/logging.md](references/logging.md) | `ExcelDna.Logging`, trace sources, startup diagnostics settings |
| [references/source-evidence.md](references/source-evidence.md) | Where each claim comes from, and what is still unverified |

## COM requires explicit permission

Don't introduce, extend, or run COM automation or interop code without the user's explicit permission for that use. A request for an Excel add-in, file picker, workbook import, ribbon, or WPF window isn't blanket COM permission. In this repository, approved COM uses are listed in `AGENTS.md` under "Excel-DNA add-in". Once a use is listed there, don't ask for it again.

COM includes:

- `ExcelDnaUtil.Application`.
- Office interop `Application`, `Workbook`, `Worksheet`, and `Range`.
- NetOffice.
- `dynamic` or reflection-based automation.
- COM activation.
- Helpers such as `ReferenceToRange`.
- **`ExcelRibbon` and custom task panes.** `ExcelRibbon` is declared as `ExcelComAddIn, IRibbonExtensibility`, so a ribbon is a COM add-in. Custom task panes are hosted through the same mechanism.
- **`ExcelDnaUtil.WindowHandle`, in some cases.** On Excel 2013 and later, it first asks the cached COM `Application` object for its `Hwnd`, through `InvokeMember`. It only falls back to the C API (`xlGetHwnd`) when that object isn't cached. For a window handle with no COM, call `XlCall.Excel(XlCall.xlGetHwnd)` in a macro context.

A wrapper doesn't make COM disappear. Inspect sample code before copying it: official samples sometimes demonstrate COM on purpose.

`ExcelReference`, `XlCall.Excel`, and `XlCall.TryExcel` are C API mechanisms, not COM automation. Excel-DNA itself has internal COM infrastructure, so don't claim a COM-free process just because the application code uses the C API.

If a requested behavior needs COM, finish the work that doesn't. Then name the operation that needs COM and the boundary you'd put around it, and ask for permission before building that boundary. Don't silently switch to another COM wrapper.

If COM is explicitly permitted, keep the bridge narrow and out of domain and view-model code. Follow Excel-DNA's threading and lifetime guidance. Don't add `Marshal.ReleaseComObject` or `FinalReleaseComObject` cleanup to an in-process Excel-DNA add-in. Never start a separate Excel process, and never shut down the host.

### Starting the add-in without COM

| Entry point | COM? | What the user sees |
|---|---|---|
| `[ExcelCommand(MenuName = "…", MenuText = "…")]` | No, it uses the C API | A menu entry on Excel's Add-ins tab |
| `[ExcelCommand(ShortCut = "…")]` | No | A keyboard shortcut |
| `ExcelRibbon` with custom UI XML | **Yes** | A ribbon tab or button. Needs explicit permission. |

When a request asks for a ribbon, offer the Add-ins tab menu as the option without COM, and ask before building the ribbon.

## Choose the native path first

When the add-in runs inside Excel, use the C API for workbooks:

- **Open:** `xlcOpen`, read-only, in a macro context.
- **Identify:** `xlfDocuments` lists open workbooks, `xlfGetWorkbook` lists a workbook's sheets, and `xlSheetId` resolves a sheet.
- **Read and write:** bounded `ExcelReference.GetValue()` and `SetValue()` calls.

Use a saved-file reader only when you need headless processing, bulk streaming, or must avoid side effects of opening a file in Excel. It's an alternative, not the default. For CSV, use a parser that handles quoting, encoding and culture, never `Split(',')`.

[references/workbook-access.md](references/workbook-access.md) has the steps, ownership rules, and the gaps: finding a range's bounds, and Excel tables.

## Execution context and lifetime

Being on Excel's main thread isn't enough. Every C API call also needs a context in which Excel permits it.

- **From WPF, ribbon callbacks or background work**, schedule each Excel operation with `ExcelAsyncUtil.QueueAsMacro`, or the version's task-based equivalent. An ordinary WPF dispatcher callback doesn't give you macro context.
- **Never wait synchronously on Excel's UI thread** for a queued macro.
- **Snapshot plain values** in the Excel-safe context, compute on another thread, then send writes back.
- **Commands, not worksheet functions, change workbooks.** Opening and closing workbooks and writing cells are commands. `IsMacroType=true` doesn't give a worksheet function permission to change workbooks.
- **Stop calling the C API** after an abort, or after an uncalculated result that requires handing control back to Excel.
- **Thread safety:** mark a function thread-safe only if its whole conversion and execution path qualifies.
- **Lifetime:** keep startup quick, initialization deliberate, cleanup scoped, and cancellation honest. Excel owns the process, so never shut it down to dispose the add-in.
- **Compatibility:** check runtime, package and bitness. The .NET Desktop Runtime's architecture must match Excel's. Don't infer that an add-in is compatible from SDK compatibility alone.

## Cell conversion is defined once, at startup

This is a project rule, the lead's decision on #192 and #195. The conversions Excel-DNA registers at startup are the single definition of how Excel cell values become domain values. Every path that reads cells follows them.

- **One class holds every conversion.** Write each conversion from a cell value to a domain value as a public static one-argument method marked `[ExcelParameterConversion]`, all in one static class. Use `[ExcelReturnConversion]` for the way back.
  - Automatic registration finds them at startup, before `AutoOpen`, and applies them to registered functions.
  - Under assembly-wide explicit registration, attribute conversions aren't discovered. Build one `ParameterConversionConfiguration` from the same methods in `AutoOpen` instead.
- **`ExcelReference.GetValue()` applies none of them.** In v1.9 it is a bare `xlCoerce` call that returns `double`, `string`, `bool`, `ExcelError` or `ExcelEmpty`. A reader that calls `GetValue()` must convert each cell by calling the same registered methods. Never add parsing or conversion logic in a reader, view model or adapter.
- **The declared type decides.** Choose each conversion from the field's declared type, never from how the cell looks. A number is a date only when its field is declared as a date.
- **Converters can't see the workbook,** because they take one argument. Pass in anything they need, such as the 1900 or 1904 date system, or record the gap as a known limit. A converter must never guess.
- **A failed conversion becomes a value, not an exception.** An `ExcelError` cell, or a number with no exact decimal, becomes a diagnostic the engine reports. It never becomes text, zero or a silently rounded number.
- **No `xlUDF` round trip.** Don't call a registered function through `XlCall.Excel(XlCall.xlUDF, …)` just to trigger the conversions. Its result travels back through Excel as an Excel value, so domain objects can't return that way.
- **Test both directions:** the reader's output must equal the registered conversions applied to the same raw cells, and no other code may turn cells into those domain values.

## Value and mapping contracts

Keep these boundaries separate:

| Boundary | Responsibility |
|---|---|
| Excel cell values, in either direction | The conversions registered at startup, used by worksheet functions and range readers alike |
| Workbook and CSV columns | Schema, column mappings, provenance |
| Domain normalization | Identifiers, decimals, dates, matching rules |
| WPF binding | Presentation, validation, selected state |

- **Excel's special values:** keep `ExcelEmpty`, `ExcelMissing` and `ExcelError` distinct. Never flatten them into empty strings or zero.
- **Identifiers:** keep leading zeros in text identifiers. Never invent digits Excel has already lost.
- **Numbers:** Excel numbers arrive as binary floating point, and casting to `decimal` can't recover precision that's already gone. Declare the conversion and rounding rules, and keep the source metadata.
- **Dates:** a number becomes a date only through the workbook's date system and the field's declared type. Account for the 1900 and 1904 systems. Don't treat every number as a date, and don't parse localized strings with the ambient culture by accident.
- **Representations:** a formula result, the formula text, the formatted display string and the stored value are four different things. `GetValue()` doesn't provide all four.
- **Side effects of opening:** opening a workbook may recalculate formulas, show prompts, or trigger other Excel behavior. Read-only isn't a macro sandbox. Respect Trust Center settings, and never lower security or dismiss a decision silently.

## WPF hosted in Excel

These points come from reports, not from Excel-DNA's source. Confirm them in Excel during the first spike. [references/source-evidence.md](references/source-evidence.md) has the sources.

- **No application object of your own.** Excel is the host, so there's normally no WPF `Application` of yours, and `Application.Current` may be null. Merge resources at the window level, and don't create a WPF `Application` in Excel's process, which would affect every other add-in there.
- **Owner:** set the window's owner from a handle obtained with `xlGetHwnd` in a macro context (see the COM section above), using `WindowInteropHelper`.
- **Keyboard input in a modeless window.** A modeless WPF window shown on Excel's thread commonly loses keyboard input: text boxes don't accept typing, and Tab doesn't move focus. `ElementHost.EnableModelessKeyboardInterop` is reported not to fix this under Excel.
  - The common workaround is to run the window on its own STA thread, with its own `Dispatcher` and Excel as its owner. Every Excel call from that window then goes through `QueueAsMacro`, and results are sent back to the window's dispatcher.
  - A modal `ShowDialog()` works, but it blocks Excel.
  - A custom task pane also works, but it's COM and needs permission.
- **Faithful MVVM:**
  - Bind view state and commands, and keep view models testable without Excel.
  - Inject services for Excel access, file access, dispatching and logging.
  - Keep code-behind to view-only behavior.
  - Keep Excel types and concrete controls out of view models.
- **Project rules** for #192 live in `AGENTS.md` and the issue: free libraries only, the approved mockup, and the serializable setup (#193). Don't redesign the UI to look like a toolkit's demo.

## Verification

Before finishing, review the change for:

- COM paths nobody approved.
- Registration that's duplicated or in the wrong order.
- Unsafe Excel execution contexts.
- Conversion done anywhere other than the startup-registered conversions.
- Range reads with no bounds.
- Wrong workbook ownership or cleanup.
- Diagnostic settings that have no effect.
- Breaks in the MVVM boundaries.

Test behavior that matters:

- **Registration:** automatic versus explicit discovery, and registering each function exactly once.
- **Values:** omitted arguments, empty and error cells, overflow, culture, and scalar versus array shapes.
- **Workbooks:** identity, unsaved data, failed opens, cancellation, and safe closing.
- **Logging:** thresholds, where warnings and errors go, capture during early startup, paths that can't be written, and duplicate listeners.
- **Real Excel:** operations hosted in Excel, wherever Excel is available.

Report separately what you verified in the source, what you compiled, and what you ran inside Excel. A parser unit test doesn't verify registration. A constant found in the source doesn't prove a complete workbook reader works.

Skill instructions are guidance for agents, not a security sandbox or a guarantee of compliance.

When guidance conflicts, inspect the selected release's source and behavior. Explain version differences rather than silently borrowing an older example or an API from a development branch.
