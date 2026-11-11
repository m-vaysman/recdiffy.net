# RecDiffy Excel add-in

Reconcile two datasets without leaving Excel. Choose the data for each side, from an open workbook, a closed workbook or a CSV file. Say which columns match records and which values must agree. RecDiffy lines the records up and lists every difference.

> **Preview.** The add-in works end to end, but there's no installer yet. To try it, [build it from source](#try-it).

![Reconciling a broker's CSV against a blotter workbook in Excel: open RecDiffy from the ribbon, select the left data, choose the right CSV, pair the columns, run, explain a break, and write the results to a new workbook](docs/readme-flow.gif)

*A broker's confirmations, in a CSV file, against the order system's blotter, in a workbook. It goes from the ribbon to a results worksheet.*

## How it works

### 1. Sources

Choose the data for each side. **Open workbook…** reads a range from a workbook you have open, including changes not yet saved. **Excel file…** opens a closed workbook read-only. Either way, you select the data in Excel, including its header row. **CSV file…** reads a CSV file with the delimiter, encoding and quoting you choose.

Each side has its own culture for text values, so numbers and dates written as text are read the way that side writes them, and never guessed. The preview shows the first rows of each side as RecDiffy reads them.

![The Sources tab: blotter.xlsx on the left and broker-confirms.csv on the right, with a preview of each side's rows](docs/readme-sources.png)

### 2. Rules

Pair a left column with a right column, even when their names differ, and say what each field holds:

- **Key:** matches records.
- **Compared:** a value that must agree once records match.
- **Carried:** a value that comes along unchanged.

Matching passes run in order. A duplicate key is never settled by picking a row: those records show as ambiguous. Each compared field is exact or within a tolerance. Numbers are rounded to their decimal places first, and a tolerance includes its bound.

![The Rules tab: TradeRef is the key, Quantity must be exact, and NetAmount must be within 0.01](docs/readme-rules.png)

### 3. Results

**Run reconciliation** shows the counts, then one row per result, such as matched and exact, matched within tolerance, matched with a break, left only, right only or ambiguous. A match lines records up. It doesn't mean their values agree, so a matched result can still be a break.

Select a result and open **Explain selected result** to see which pass matched it, and how each value was read.

![The Results tab: 3 matched, 1 exact, 1 within tolerance, 1 break, 2 left only and 2 right only, with the break explained](docs/readme-results.png)

### 4. Write to worksheet

**Write to worksheet** puts the results on a new sheet in a new workbook, so nothing you have open changes. The sheet says:

- when the data was read;
- each side's source and row count;
- the profile hash and engine version;
- the counts;
- one row per result, with the rows it came from.

![The results sheet in a new workbook: the run's sources, counts, and one row per result](docs/demo/frames/08-worksheet.png)

### Settings

The **Settings** tab has two switches. One chooses whether exact matches are listed. The counts always include them. The other chooses whether **Explain selected result** is offered. Both are remembered for your Windows account.

## Try it

You need Windows, 64-bit Excel and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).

1. [Build the add-in](#build-and-test). The build writes the packed add-in `RecDiffy.ExcelAddIn-AddIn64-packed.xll` to `src/RecDiffy.ExcelAddIn/bin/Release/net10.0-windows/publish/`.
2. Open the `.xll` in Excel, using File → Open or a double-click.
3. The build is unsigned, so Excel shows a security notice. Choose "Enable this add-in for this session only".
4. A **RecDiffy** tab appears on the ribbon. **Reconcile datasets** opens the window.

To repeat the animation, use the files in [docs/typed-reconciliation/data](../docs/typed-reconciliation/data):

1. Put `blotter.xlsx` on the left and `broker-confirms.csv` on the right.
2. On Rules, pair `Trade ID` with `TradeRef` as the key, `Qty` with `Quantity`, and `Net Consideration` with `NetAmount`.
3. Set `NetAmount` to within 0.01.

## Troubleshooting

**One .NET runtime per Excel process.** Excel-DNA can load only one .NET runtime into an Excel process. If another .NET add-in loads an older runtime first, this add-in won't load. Excel-DNA then reports, for example, "The required .NET Desktop Runtime 10.0.0 x64 is incompatible with the runtime 6.0.36 already loaded in this process". In that case, turn the other add-in off for the session, or have it target .NET 10 or allow `RollForward="LatestMajor"`.

## For contributors

This is an Excel-DNA add-in that reconciles a left and a right dataset with RecDiffy.NET (#192). It's a separate solution from `RecDiffy.NET/RecDiffy.slnx`, with its own CI workflow (`.github/workflows/excel-addin.yml`) that runs only when this folder changes.

Agents: read `.agents/skills/excel-dna/SKILL.md` before changing this code. No Excel COM code without the lead's approval; see "Excel-DNA add-in" in `AGENTS.md`.

### Projects

| Project | Target | Holds |
|---|---|---|
| `RecDiffy.ExcelAddIn` | `net10.0-windows`, WPF, Excel-DNA 1.9.0, RecDiffy.IO 1.2.0-preview.2 | The ribbon, the window host, the views, the Excel reader and the CSV reader. The Excel reader uses the C API and the cell conversions registered at startup. The CSV reader uses `RecDiffy.IO`. |
| `RecDiffy.ExcelAddIn.ViewModels` | `net10.0`, RecDiffy 1.2.0-preview.2 | View models (CommunityToolkit.Mvvm) and the serializable reconciliation setup (#193), with no Excel or WPF types |
| `RecDiffy.ExcelAddIn.ViewModels.Tests` | `net10.0` | xunit tests of the view models, which run without Excel |
| `RecDiffy.ExcelAddIn.Tests` | `net10.0-windows` | xunit tests of the cell conversions and the table built from a picked range, which run without Excel |

### Build and test

```
dotnet build RecDiffy.ExcelAddIn/RecDiffy.ExcelAddIn.slnx -c Release -warnaserror
dotnet test RecDiffy.ExcelAddIn/RecDiffy.ExcelAddIn.slnx -c Release
```

The build writes packed add-ins to `src/RecDiffy.ExcelAddIn/bin/Release/net10.0-windows/publish/`: `RecDiffy.ExcelAddIn-AddIn64-packed.xll` for 64-bit Excel, and `RecDiffy.ExcelAddIn-AddIn-packed.xll` for 32-bit Excel.

### The README's images

The animation is built from the frames in `docs/demo/frames` by `docs/demo/make-gif.ps1`, which needs ffmpeg (`winget install Gyan.FFmpeg`). After a UI change, capture new frames of the same size (1440 × 810), check the click positions in the script's table, and run it:

```
pwsh RecDiffy.ExcelAddIn/docs/demo/make-gif.ps1
```

Before you commit a capture, make sure it shows no account name or initials, no user paths, and no other windows or notifications. The other files in `docs/` are the screenshots from each phase's PR.
