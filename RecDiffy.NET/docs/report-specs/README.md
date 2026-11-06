# Report specs for RecDiffy.Excel

This folder is the formatting and layout spec for the RecDiffy.Excel report writer. The two workbooks in `reference-workbooks/` show the target output; the Python scripts that built them state every style, column, formula and check exactly. Port the presentation. Do not port any matching: in RecDiffy.Excel, all reconciliation decisions come from the engine's `ReconciliationResult`.

## What's here

| Path | What it is | Use it for |
|---|---|---|
| `reference-workbooks/RecDiffy_Reconciliation_Views_BO_MO.xlsx` | All 13 views (BO1–BO6, MO1–MO7) on the claude-sonnet-5.5 dataset | The look of every report: layout, colours, number formats, wording, checks |
| `reference-workbooks/RecDiffy_Engine_MO1_Control_Totals_fixture-v1.xlsx` | MO1 Control totals built from real engine output on fixture-v1 | How a report links to raw engine tabs, ties out to file totals, and handles ambiguous rows and currency-split pairs |
| `workbook-13-views/build_xlsx.py` | Builds the 13-view workbook | Exact styles (`FILL`, `TXT`, number formats, `put`, `header`, `view_head`), every view's columns, formulas, conditional formatting and checks |
| `workbook-13-views/vizcommon.py` | Shared helpers | Normalization-step labels (`steps_for`), diff-cell state (`diff_state`), category list |
| `workbook-13-views/data/catalogue.json` | View catalogue | Each view's ID, office, name, tab name, users, question, cadence, description and the prompt that produces it, plus the shared data contract (`preamble`) |
| `workbook-13-views/data/views.json` | Per-view data the builder lays out | Expected content of each view for this dataset |
| `workbook-13-views/data/rec_result.json` | Result entries and rows in the shape the prompts assume | Not engine output: see "Provenance" |
| `engine-mo1/build_engine_mo1.py` | Builds the engine MO1 workbook | The pattern for every report: raw engine tabs, green cross-sheet links, ✓/✗ checks, per-currency subtotals, tie-out to files |
| `engine-mo1/fixture-v1-engine-result.json` | Engine output (Entries, Rows, Summary, BuildSideBySide) for fixture-v1 | Input for `build_engine_mo1.py`; a golden source for tests |
| `engine-mo1/harness/` | The console app that produced that JSON | How to call `Reconciler.Compile`, `Reconciler.Run` and `BuildSideBySide` with the fixture-v1 profile from `WorkbookAcceptanceTests` |
| `view-logic-reference/build_views.py` | Reference only, not runnable | The display calculations each view needs: blotter status, age, impact, ranking and suggested action; cash-ladder and face decompositions; funnel stages; heatmap and ageing counts; tolerance usage; audit-trail rows |

## Running the builders

Both need Python 3.10+ and openpyxl, and run from any folder.

```
python workbook-13-views/build_xlsx.py out/RecDiffy_Reconciliation_Views_BO_MO.xlsx
python engine-mo1/build_engine_mo1.py engine-mo1/fixture-v1-engine-result.json out/RecDiffy_Engine_MO1_Control_Totals_fixture-v1.xlsx
```

openpyxl writes formulas without cached values. Excel calculates them on open. To check them headless, recalculate them in LibreOffice with formula recalculation on load set to Always, then read the values back. The reference workbooks recalculate with 0 formula errors and every ✓ check passing.

## Rules the reports follow

- Each view tab opens with its ID, name, office, users, the question it answers, cadence, a one-line description, and the prompt that produces it.
- Numbers on a view tab are formulas that link to a data tab, never retyped values. Links to engine output are green text.
- Every decomposition has a ✓/✗ check, and totals tie back to the input files. `BuildSideBySide` leaves out Ambiguous, SearchIncomplete, Quarantined and Suppressed rows, so add those back explicitly.
- Never add amounts across currencies; group by currency with a subtotal per currency.
- A matched pair whose two sides carry different currencies is reported separately from genuine field differences.
- Colours: root break red fill, within tolerance amber, consequence of another field grey italic, passing checks green.
- Formats: quantities `#,##0;(#,##0);"-"`, amounts `#,##0.00;(#,##0.00);"-"`, dates `yyyy-mm-dd`. Arial 10, frozen headers, autofilters on tables, landscape fitted to one page wide.
- Ageing and settlement status are formulas on a named as-of cell (`AsOf`), so changing it re-ages the report.

## Provenance

- The 13-view workbook's data comes from an analysis script that applied the revised architecture spec to the claude-sonnet-5.5 dataset; it is not engine output. The engine on main cannot reconcile that dataset yet: it has no profile for it and no rules for CUSIP/ticker-to-ISIN resolution, dirty-to-clean price, fee inference or net cash.
- The MO1 workbook comes from the engine built from origin/main at 39ff71f, run on fixture-v1 with the profile from `WorkbookAcceptanceTests`. All 310 expected-result cases pass.
