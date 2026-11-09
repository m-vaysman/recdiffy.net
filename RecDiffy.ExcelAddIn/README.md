# RecDiffy Excel add-in

An Excel-DNA add-in that reconciles a left and a right dataset with RecDiffy.NET (#192). It's a separate solution from `RecDiffy.NET/RecDiffy.slnx`, with its own CI workflow (`.github/workflows/excel-addin.yml`) that runs only when this folder changes.

Agents: read `.agents/skills/excel-dna/SKILL.md` before changing this code. No Excel COM code without the lead's approval; see "Excel-DNA add-in" in `AGENTS.md`.

## Projects

| Project | Target | Holds |
|---|---|---|
| `RecDiffy.ExcelAddIn` | `net10.0-windows`, WPF, Excel-DNA 1.9.0, RecDiffy.IO 1.2.0-preview.2 | The ribbon, the window host, the views, the Excel reader and the CSV reader. The Excel reader uses the C API and the cell conversions registered at startup. The CSV reader uses `RecDiffy.IO`. |
| `RecDiffy.ExcelAddIn.ViewModels` | `net10.0`, RecDiffy 1.2.0-preview.2 | View models (CommunityToolkit.Mvvm) and the serializable reconciliation setup (#193), with no Excel or WPF types |
| `RecDiffy.ExcelAddIn.ViewModels.Tests` | `net10.0` | xunit tests of the view models, which run without Excel |
| `RecDiffy.ExcelAddIn.Tests` | `net10.0-windows` | xunit tests of the cell conversions and the table built from a picked range, which run without Excel |

## Build and test

```
dotnet build RecDiffy.ExcelAddIn/RecDiffy.ExcelAddIn.slnx -c Release -warnaserror
dotnet test RecDiffy.ExcelAddIn/RecDiffy.ExcelAddIn.slnx -c Release
```

The build writes packed add-ins to `src/RecDiffy.ExcelAddIn/bin/Release/net10.0-windows/publish/`: `RecDiffy.ExcelAddIn-AddIn64-packed.xll` for 64-bit Excel, and `RecDiffy.ExcelAddIn-AddIn-packed.xll` for 32-bit Excel.

## Try it in Excel

You need 64-bit Excel and the .NET 10 Desktop Runtime (x64).

1. Open the packed `.xll` in Excel, using File → Open or a double-click.
2. The build is unsigned, so Excel shows a security notice. Choose "Enable this add-in for this session only".
3. A **RecDiffy** tab appears on the ribbon. "Reconcile datasets" opens the window.

**One .NET runtime per Excel process.** Excel-DNA can load only one .NET runtime into an Excel process. If another .NET add-in loads an older runtime first, this add-in won't load. Excel-DNA then reports, for example, "The required .NET Desktop Runtime 10.0.0 x64 is incompatible with the runtime 6.0.36 already loaded in this process". In that case, turn the other add-in off for the session, or have it target .NET 10 or allow `RollForward="LatestMajor"`.
