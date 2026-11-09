# Built-in logging

Prefer Excel-DNA's own logging before adding Serilog, NLog, log4net or another framework. If the project already has a logging abstraction, adapt it rather than adding a second pipeline.

The public namespace is `ExcelDna.Logging`.

- **`LogDisplay.RecordLine(format, args)`** records a line without forcing the window open.
- **`LogDisplay.WriteLine(format, args)`** records a line and shows the window.
- **`LogDisplay.Show()`, `Hide()`, `Clear()` and `DisplayOrder`** control the window.
- **`LogDisplayTraceListener`** connects a `System.Diagnostics.TraceSource` to the window. Error and Critical events that pass its filter open the window, and lower levels are buffered.

The window is a bounded diagnostic history, not a durable audit store. A WPF window can expose it through an injected diagnostics service and a command.

Don't call the internal `Logger` or `TraceLogger` classes. Creating `new TraceSource("ExcelDna.Integration")` doesn't give you Excel-DNA's already-configured internal source.

Logging directly to the window:

```csharp
using ExcelDna.Logging;

LogDisplay.RecordLine("Import completed: {0} rows; {1} invalid.", count, invalid);
```

For leveled logging, create and own a separate source once:

```csharp
using System.Diagnostics;
using ExcelDna.Logging;

var applicationTrace = new TraceSource("Reconciliation.AddIn", SourceLevels.Information);
applicationTrace.Listeners.Add(new LogDisplayTraceListener());
applicationTrace.TraceEvent(TraceEventType.Information, 1001, "Reconciliation started.");
```

Rules for that source:

- **Keep it behind an application logging service.** Don't recreate the source or add listeners on each call.
- **Define `TRACE`** wherever conditional trace calls must run.
- **Flush and close only the sources and listeners you own,** never Excel-DNA's internal source.
- **No inheritance:** direct window writes and your own source don't pick up Excel-DNA's file logging or diagnostic settings.

## Startup diagnostics on modern .NET

Configure logging before Excel starts or loads the add-in, especially for failures that happen before your `AutoOpen`.

`LoggingSettings.cs` reads each setting from an environment variable first, then from `HKCU\Software\ExcelDna\Diagnostics`, then from `HKLM\Software\ExcelDna\Diagnostics`:

| Environment variable | Registry value | Example |
|---|---|---|
| `EXCELDNA_DIAGNOSTICS_SOURCE_LEVEL` | `SourceLevel` | `Verbose`. The default is `Warning`. |
| `EXCELDNA_DIAGNOSTICS_LOGDISPLAY_LEVEL` | `LogDisplayLevel` | `Warning` |
| `EXCELDNA_DIAGNOSTICS_DEBUGGER_LEVEL` | `DebuggerLevel` | `Verbose` |
| `EXCELDNA_DIAGNOSTICS_FILE_LEVEL` | `FileLevel` | `Verbose` |
| `EXCELDNA_DIAGNOSTICS_FILE_NAME` | `FileName` | An explicit, writable absolute path |

- **Both thresholds matter.** A Verbose listener can't receive events that a Warning source threshold has already filtered out.
- **Two different enums.** The source level is a `SourceLevels` value, and the other levels are `TraceEventType` values. Use values the installed implementation accepts.
- **Prefer temporary settings** for the process that launches Excel. Don't permanently change user or machine configuration as a side effect of building a feature.
- **Shared log files:** several Excel instances may write to the same file.
- **.NET Framework examples don't apply.** Examples using `.xll.config` and `system.diagnostics` belong to the .NET Framework path. Don't assume they configure a modern .NET add-in.
- **IntelliSense is separate.** It has its own diagnostic settings, so investigate it on its own when it's the failing component.

## What to log

- **Log** operations, outcomes, counts, timings and sanitized errors.
- **Don't log by default** secrets, workbook contents or sensitive paths.
- **Keep three things separate:** diagnostics, the domain audit history, and messages the user can act on.
- **Never hide a failure.** Don't swallow a boundary failure and report success.
