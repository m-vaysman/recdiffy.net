# Source evidence

This file lists where each statement in the skill comes from. Source paths are relative to `Source/ExcelDna.Integration/` at tag [`v1.9.0-Release`](https://github.com/Excel-DNA/ExcelDna/tree/v1.9.0-Release), checked on November 9, 2026. None of this was compiled or run inside Excel.

Status labels:

- **Source:** read in the v1.9 source, at the line given.
- **Docs:** stated in Excel-DNA or Microsoft documentation.
- **Reported:** from forums or community answers; confirm it in Excel.
- **Unverified:** carried over from earlier guidance and not yet checked.

## Statements

| Statement | Status | Evidence |
|---|---|---|
| Functions and commands are registered before the add-in's `AutoOpen` runs | Source | `DnaLibrary.cs:322-339`: `RegisterMethods(commands)` and `Registration.Register(functions, …)`, then each add-in's `AutoOpen` |
| `[ExcelParameterConversion]` and `[ExcelReturnConversion]` methods are collected into the registration configuration at load | Source | `DnaLibrary.cs:281-286` |
| Assembly-wide explicit registration skips discovering conversion attributes | Source | `AssemblyLoader.cs:85-88`, `if (!explicitRegistration)` |
| `ExcelReference.GetValue()` applies no registered conversion | Source | `ExcelReference.cs:238-241`: `return XlCall.Excel(XlCall.xlCoerce, this);` |
| `ExcelReference` has constructors taking a sheet ID or a sheet name | Source | `ExcelReference.cs:90` (`IntPtr sheetId`) and `:98` (`string sheetName`) |
| The C API constants used by the skill exist | Source | `XlCall.cs`: `xlSheetId` (60), `xlGetHwnd` (64), `xlfDocuments` (194), `xlfGetDocument` (285), `xlfEvaluate` (343), `xlfGetWorkbook` (354), `xlcOpen` (685), `xlcClose` (700), `xlcActivate` (779), `xlcEcho` (816) |
| `ExcelRibbon` is a COM add-in | Source | `ExcelRibbon.cs:20`: `public class ExcelRibbon : ExcelComAddIn, IRibbonExtensibility` |
| `ExcelDnaUtil.WindowHandle` may go through COM | Source | `Excel.cs:95-140`: on Excel 2013 and later, it calls `InvokeMember("Hwnd")` on the cached COM `Application` when one exists, and falls back to `XlCall.Excel(XlCall.xlGetHwnd)` |
| Parameter conversions are applied in reverse order to how they're identified | Source | `Registration/ParameterConversionRegistration.cs:48`, a source comment |
| The attribute list and each attribute's settings | Source | `ExcelAttributes.cs:14-167`, and `Registration/Attributes.cs:14-19` for `ExcelAsyncFunctionAttribute`, which sets `ExplicitRegistration = true` |
| `ExcelAsyncUtil.QueueAsMacro` exists | Source | `ExcelAsyncUtil.cs:137` and `:143` |
| The `LogDisplay` methods exist | Source | `LogDisplay.cs:220` (`Show`), `:235` (`Hide`), `:250` (`WriteLine`), `:264` (`RecordLine`), `:302` (`Clear`), `:322` (`DisplayOrder`) |
| Diagnostics settings are read from environment variables, then HKCU, then HKLM, and the source level defaults to Warning | Source | `LoggingSettings.cs:17-35` |
| The separate `ExcelDna.Registration` package is deprecated in v1.9 | Docs | [Excel-DNA v1.9 release notes](https://excel-dna.net/docs/release-notes-1-9/) |
| The C API can't be used directly from ribbon callbacks; use `QueueAsMacro` | Reported | [Excel-DNA group: XlCall.Excel failing inside QueueAsMacro](https://groups.google.com/g/exceldna/c/vh4NERhMrws) |
| `GetValue()` fails on a reference that hasn't been calculated yet | Reported | Excel-DNA's author in [ExcelReference.GetValue() throws XlCallException](https://groups.google.com/g/exceldna/c/xm8DZ5Eld9E) |
| `xlcOpen`'s positional arguments are file name, update-links setting and read-only setting | Docs | The `OPEN` command in the Excel 4 macro reference |
| `GET.DOCUMENT` information types 9 to 12 give the first and last used row and column | **Disputed** | Forum answers say so, but an old MSDN Office 95 page lists chart values for 9 to 11. Check in Excel before relying on it. |
| No C API function lists Excel tables (ListObjects) | Reported | No such information function appears in `XlCall.cs`. Not exhaustively checked. |
| `ElementHost.EnableModelessKeyboardInterop` doesn't fix keyboard input for a modeless WPF window in Excel, and a window on its own STA thread does | Reported | [ExcelDNA with WPF: Textbox Input](https://groups.google.com/g/exceldna/c/WV5P4QVzo8M); [Stack Overflow: WPF modeless dialog from an Excel add-in](https://stackoverflow.com/questions/5869359) |
| The `ExcelAddInExplicitRegistration` build property exists | Unverified | Carried over from the earlier draft. Check the v1.9 MSBuild targets. |
| The v1.9 pipeline order: parameter conversions, async, params, handles, execution handlers | Unverified | Carried over from the earlier draft. Check `ExtendedRegistration/Registration.cs`. |
| `LogDisplayTraceListener` opens the window for Error and Critical events and buffers lower levels | Unverified | Carried over from the earlier draft. Check `LogDisplayTraceListener.cs`. |

## Primary references

- [Excel-DNA v1.9 source](https://github.com/Excel-DNA/ExcelDna/tree/v1.9.0-Release)
- [Release notes for v1.9](https://excel-dna.net/docs/release-notes-1-9/)
- [Runtime support](https://excel-dna.net/docs/guides-basic/dotnet-runtime-support/)
- [Logging configuration on modern .NET](https://excel-dna.net/docs/guides-advanced/logging_configuration_netcore/)
- [Excel programming interfaces](https://excel-dna.net/docs/guides-basic/excel-programming-interfaces/introduction/)
- [Performing asynchronous work](https://excel-dna.github.io/docs/guides-advanced/performing-asynchronous-work)
- [Microsoft: programming with the C API in Excel](https://learn.microsoft.com/en-us/office/client-developer/excel/programming-with-the-c-api-in-excel)
- [Official samples](https://github.com/Excel-DNA/Samples)
- Optional saved-file readers: [Sylvan.Data.Excel](https://github.com/MarkPflug/Sylvan.Data.Excel) and [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader)
