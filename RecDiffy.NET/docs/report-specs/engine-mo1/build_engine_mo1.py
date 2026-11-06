# Builds RecDiffy_Engine_MO1_Control_Totals_fixture-v1.xlsx from the engine's own output.
# Usage: python build_engine_mo1.py fixture-v1-engine-result.json <output.xlsx>   (needs openpyxl)
import sys, json
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side as BSide
from openpyxl.utils import get_column_letter as L
from openpyxl.formatting.rule import CellIsRule, FormulaRule

SRC, OUT = sys.argv[1], sys.argv[2]
R = json.load(open(SRC))
F, INK, MUTED = 'Arial', '1F2937', '6B7280'
FILL = dict(head='1F2937', sub='E5E7EB', brk='FDE2E1', ok='DCFCE7', card='F8FAFC', band='EEF2F7', tot='E5E7EB', input='FEF3C7')
QTY = '#,##0;(#,##0);"-"'
CNT = '#,##0;-#,##0;"-"'
thin = BSide(style='thin', color='D1D5DB')
BOX = Border(left=thin, right=thin, top=thin, bottom=thin)
GREEN = '008000'  # links to another sheet


def fill(c): return PatternFill('solid', start_color=c, end_color=c)


def put(ws, r, c, v, fmt=None, bold=False, color=None, bg=None, italic=False, wrap=False, align=None, size=10, border=True):
    cell = ws.cell(row=r, column=c, value=v)
    cell.font = Font(name=F, size=size, bold=bold, italic=italic, color=color or INK)
    if fmt: cell.number_format = fmt
    if bg: cell.fill = fill(bg)
    if wrap or align: cell.alignment = Alignment(wrap_text=wrap, horizontal=align, vertical='top')
    if border: cell.border = BOX
    return cell


def header(ws, r, cols, start=1):
    for i, h in enumerate(cols):
        c = put(ws, r, start + i, h, bold=True, color='FFFFFF', bg=FILL['head'])
        c.alignment = Alignment(wrap_text=True, vertical='center', horizontal='center' if i else None)
    ws.row_dimensions[r].height = 42


def widths(ws, ws_widths):
    for i, w in enumerate(ws_widths, start=1): ws.column_dimensions[L(i)].width = w


meta, summ, acc = R['meta'], R['summary'], R['acceptance']
rows = R['rows']
by_key = {(x['source'], x['rowKey']): x for x in rows}
split_entries = []
for e in R['entries']:
    if e['alignment'] != 'Matched':
        continue
    lc = {by_key[('left', k)]['currency'] for k in e['left']}
    rc = {by_key[('right', k)]['currency'] for k in e['right']}
    if lc != rc:
        split_entries.append((e, lc, rc))
split_ids = {e['id'] for e, _, _ in split_entries}

wb = Workbook()
wb._named_styles['Normal'].font = Font(name=F, size=10)

# ---------------------------------------------------------------- Engine_Rows (raw engine row results)
er = wb.active
er.title = 'Engine_Rows'
er.sheet_view.showGridLines = False
put(er, 1, 1, 'Engine row results: ReconciliationResult.Rows, one per input row, with canonical values from result.Canonical(row)', bold=True, border=False)
put(er, 2, 1, 'Pair ccy differs is derived here, not by the engine: Y when the row belongs to a matched entry whose internal and street rows carry different currencies.', color=MUTED, italic=True, border=False)
cols = ['Source', 'RowKey', 'Disposition', 'EntryId', 'Counterparty', 'Asset', 'Currency', 'Quantity', 'Pair ccy differs', 'Diagnostics']
header(er, 4, cols)
for i, x in enumerate(rows, start=5):
    vals = [x['source'], x['rowKey'], x['disposition'], x['entryId'], x['counterparty'], x['asset'], x['currency'], x['quantity'],
            'Y' if x['entryId'] in split_ids else 'N', '; '.join(x['diagnostics'])]
    for j, v in enumerate(vals, start=1):
        put(er, i, j, v, fmt=QTY if j == 8 else None)
last = 4 + len(rows)
er.freeze_panes = 'A5'
er.auto_filter.ref = f'A4:J{last}'
widths(er, [9, 12, 14, 30, 14, 12, 10, 14, 10, 20])
RG = lambda col: f"Engine_Rows!${col}$5:${col}${last}"
SRCc, DISP, CP, AS, CC, QT, SPL = RG('A'), RG('C'), RG('E'), RG('F'), RG('G'), RG('H'), RG('I')

# ---------------------------------------------------------------- Engine_SideBySide (raw view lines)
es = wb.create_sheet('Engine_SideBySide')
es.sheet_view.showGridLines = False
put(es, 1, 1, 'Engine side-by-side: ReconciliationResult.BuildSideBySide(rows [Counterparty], columns [Asset], measure Quantity, filter Currency = <ccy>), one call per currency', bold=True, border=False)
put(es, 2, 1, 'Values exactly as returned. LeftOnly is negated by the engine; Delta = Right − Left = LeftOnly + RightOnly + MatchedDelta. Ambiguous, SearchIncomplete, Quarantined and Suppressed rows are not counted.', color=MUTED, italic=True, border=False)
header(es, 4, ['Currency (filter)', 'Row: Counterparty', 'Column: Asset', 'Left', 'Right', 'Delta', 'LeftOnly', 'RightOnly', 'MatchedDelta'])
lines = []
r = 5
for blk in R['sideBySide']:
    for ln in blk['lines']:
        c = ln['cells']['Quantity']
        vals = [blk['currency'], ln['row'], ln['column'], c['left'], c['right'], c['delta'], c['leftOnly'], c['rightOnly'], c['matchedDelta']]
        for j, v in enumerate(vals, start=1):
            put(es, r, j, v, fmt=QTY if j >= 4 else None)
        lines.append((r, blk['currency'], ln['row'], ln['column']))
        r += 1
es.freeze_panes = 'A5'
widths(es, [16, 18, 16, 16, 16, 16, 16, 16, 16])

# ---------------------------------------------------------------- MO1 view
ws = wb.create_sheet('MO1_Control_Totals', 0)
ws.sheet_view.showGridLines = False
SPAN = 17
put(ws, 1, 1, 'MO1 · Control totals (side-by-side), produced by the RecDiffy engine', bold=True, size=14, border=False)
put(ws, 2, 1, 'Middle office  ·  Users: Product control, reconciliation owners  ·  Question: Do the two books agree in total, and where does each difference come from?  ·  Cadence: Daily, before detail review', color=MUTED, border=False)
put(ws, 3, 1, (f"Data: {meta['workbook']} ({meta['leftRows']} internal rows, {meta['rightRows']} counterparty rows). Engine built from origin/main {meta['sourceCommit'][:7]} and run with the "
               f"profile from RecDiffy.Tests/WorkbookAcceptanceTests.cs; all {acc['cases']} expected-result cases pass. Every green number links to the engine's own output on the Engine_ tabs."), border=False)
put(ws, 4, 1, 'Prompt that produces this layout from the engine output:', bold=True, color=MUTED, border=False)
PROMPT = (
    "You will receive two arrays from a RecDiffy.NET run. sideBySide: one block per currency, each the result of ReconciliationResult.BuildSideBySide with rows [Counterparty], "
    "columns [Asset], measure Quantity and filter Currency = <ccy>; each line has row, column and cells.Quantity {left, right, delta, leftOnly, rightOnly, matchedDelta}, where leftOnly is "
    "already negated and delta = right − left = leftOnly + rightOnly + matchedDelta. rows: one per input row {source (left = ours, right = theirs), rowKey, disposition, entryId, counterparty, asset, currency, quantity}.\n"
    "Build a control-totals sheet. Part 1, row counts by counterparty and asset: rows, matched, unmatched and ambiguous for each side, as COUNTIFS on the rows table, with a check that rows = matched + unmatched + ambiguous. "
    "Part 2, quantity by currency, counterparty and asset: Ours, Theirs, Δ = Theirs − Ours, Unmatched ours, Unmatched theirs, Inside matched, a check that Δ equals the three parts and equals the engine's delta; "
    "split Inside matched into the part caused by matched pairs whose two sides carry different currencies and the rest; then Ambiguous ours and theirs (the side-by-side leaves ambiguous rows out), "
    "file totals from the rows table, and a tie-out that file total = side-by-side total + ambiguous. Group by currency with a subtotal per currency and never add across currencies. "
    "Part 3: list the matched pairs whose currencies differ, since their halves fall into different currency blocks.\n"
    "Formatting: link every engine number to the raw output tab instead of retyping it; quantities with thousands separators, no decimals, negatives in parentheses; shade a non-zero Δ red; checks as ✓ or ✗; "
    "a band label per currency; freeze the header rows.")
ws.merge_cells(start_row=5, start_column=1, end_row=5, end_column=SPAN)
c = put(ws, 5, 1, PROMPT, wrap=True, bg=FILL['card'], border=False)
c.alignment = Alignment(wrap_text=True, vertical='top')
ws.row_dimensions[5].height = 150

# Part 1 · counts
r0 = 7
put(ws, r0, 1, 'Part 1 · Row counts by counterparty and asset (COUNTIFS on Engine_Rows)', bold=True, border=False)
header(ws, r0 + 1, ['Counterparty', 'Asset', 'Rows ours', 'Rows theirs', 'Matched ours', 'Matched theirs', 'Unmatched ours', 'Unmatched theirs', 'Ambiguous ours', 'Ambiguous theirs', 'Check'])
pairs = sorted({(x['counterparty'], x['asset']) for x in rows})
r = r0 + 2
p1_first = r
for cp, asset in pairs:
    put(ws, r, 1, cp, bold=True); put(ws, r, 2, asset)
    crit = f'{CP},$A{r},{AS},$B{r}'
    put(ws, r, 3, f'=COUNTIFS({SRCc},"left",{crit})', fmt=CNT)
    put(ws, r, 4, f'=COUNTIFS({SRCc},"right",{crit})', fmt=CNT)
    for col, (side, disp) in zip(range(5, 11), [('left', 'Matched'), ('right', 'Matched'), ('left', 'LeftOnly'), ('right', 'RightOnly'), ('left', 'Ambiguous'), ('right', 'Ambiguous')]):
        put(ws, r, col, f'=COUNTIFS({SRCc},"{side}",{DISP},"{disp}",{crit})', fmt=CNT)
    put(ws, r, 11, f'=IF(AND(C{r}=E{r}+G{r}+I{r},D{r}=F{r}+H{r}+J{r}),"✓","✗")', align='center')
    r += 1
p1_last = r - 1
put(ws, r, 1, 'Total', bold=True, bg=FILL['tot']); put(ws, r, 2, None, bg=FILL['tot'])
for col in range(3, 11):
    put(ws, r, col, f'=SUM({L(col)}{p1_first}:{L(col)}{p1_last})', fmt=CNT, bold=True, bg=FILL['tot'])
put(ws, r, 11, f'=IF(AND(C{r}={meta["leftRows"]},D{r}={meta["rightRows"]},C{r}=E{r}+G{r}+I{r},D{r}=F{r}+H{r}+J{r}),"✓","✗")', align='center', bold=True, bg=FILL['tot'])
p1_tot = r

# Part 2 · quantity side-by-side
r += 2
put(ws, r, 1, 'Part 2 · Quantity side-by-side by currency (engine BuildSideBySide), with the difference decomposed and tied back to the files', bold=True, border=False)
r += 1
cols2 = ['Currency', 'Counterparty', 'Asset', 'Ours', 'Theirs', 'Δ (theirs − ours)', 'Unmatched ours', 'Unmatched theirs', 'Inside matched', 'Check',
         'Inside matched: currency split', 'Inside matched: field differences', 'Ambiguous ours (not in totals)', 'Ambiguous theirs (not in totals)', 'File total ours', 'File total theirs', 'Tie-out']
header(ws, r, cols2)
p2_head = r
r += 1
subtotal_rows = []
by_ccy = {}
for (er_row, ccy, cp, asset) in lines:
    by_ccy.setdefault(ccy, []).append((er_row, cp, asset))
for ccy, items in by_ccy.items():
    ws.merge_cells(start_row=r, start_column=1, end_row=r, end_column=SPAN)
    put(ws, r, 1, f'{ccy}  ·  totals in {ccy} only', bold=True, bg=FILL['band'])
    r += 1
    first = r
    for er_row, cp, asset in items:
        put(ws, r, 1, ccy); put(ws, r, 2, cp, bold=True); put(ws, r, 3, asset)
        put(ws, r, 4, f'=Engine_SideBySide!D{er_row}', fmt=QTY, color=GREEN)
        put(ws, r, 5, f'=Engine_SideBySide!E{er_row}', fmt=QTY, color=GREEN)
        put(ws, r, 6, f'=E{r}-D{r}', fmt=QTY, bold=True)
        put(ws, r, 7, f'=Engine_SideBySide!G{er_row}', fmt=QTY, color=GREEN)
        put(ws, r, 8, f'=Engine_SideBySide!H{er_row}', fmt=QTY, color=GREEN)
        put(ws, r, 9, f'=Engine_SideBySide!I{er_row}', fmt=QTY, color=GREEN)
        put(ws, r, 10, f'=IF(AND(F{r}=G{r}+H{r}+I{r},F{r}=Engine_SideBySide!F{er_row}),"✓","✗")', align='center')
        crit = f'{CP},$B{r},{AS},$C{r},{CC},$A{r}'
        put(ws, r, 11, f'=SUMIFS({QT},{SRCc},"right",{DISP},"Matched",{SPL},"Y",{crit})-SUMIFS({QT},{SRCc},"left",{DISP},"Matched",{SPL},"Y",{crit})', fmt=QTY)
        put(ws, r, 12, f'=I{r}-K{r}', fmt=QTY)
        put(ws, r, 13, f'=SUMIFS({QT},{SRCc},"left",{DISP},"Ambiguous",{crit})', fmt=QTY)
        put(ws, r, 14, f'=SUMIFS({QT},{SRCc},"right",{DISP},"Ambiguous",{crit})', fmt=QTY)
        put(ws, r, 15, f'=SUMIFS({QT},{SRCc},"left",{crit})', fmt=QTY)
        put(ws, r, 16, f'=SUMIFS({QT},{SRCc},"right",{crit})', fmt=QTY)
        put(ws, r, 17, f'=IF(AND(O{r}=D{r}+M{r},P{r}=E{r}+N{r}),"✓","✗")', align='center')
        r += 1
    last_r = r - 1
    put(ws, r, 1, ccy, bold=True, bg=FILL['tot']); put(ws, r, 2, f'Subtotal {ccy}', bold=True, bg=FILL['tot']); put(ws, r, 3, None, bg=FILL['tot'])
    for col in [4, 5, 6, 7, 8, 9, 11, 12, 13, 14, 15, 16]:
        put(ws, r, col, f'=SUM({L(col)}{first}:{L(col)}{last_r})', fmt=QTY, bold=True, bg=FILL['tot'])
    put(ws, r, 10, f'=IF(F{r}=G{r}+H{r}+I{r},"✓","✗")', align='center', bold=True, bg=FILL['tot'])
    put(ws, r, 17, f'=IF(AND(O{r}=D{r}+M{r},P{r}=E{r}+N{r}),"✓","✗")', align='center', bold=True, bg=FILL['tot'])
    subtotal_rows.append(r)
    r += 1
p2_last = r - 1
put(ws, r, 1, 'Quantities are summed within a currency only. Units differ by asset (face for bonds, shares for equity, notional for FX and swaps), so compare a line across its own row, not down the column.', color=MUTED, italic=True, border=False)

# Part 3 · currency-split pairs
r += 2
put(ws, r, 1, 'Part 3 · Matched pairs whose two sides carry different currencies (their halves sit in different currency blocks above)', bold=True, border=False)
r += 1
header(ws, r, ['Entry', 'Internal row', 'Street row', 'Counterparty', 'Asset', 'Ours currency', 'Theirs currency', 'Quantity ours', 'Quantity theirs', 'Matched by', 'Outcome', 'Break fields'])
r += 1
for e, lc, rc in split_entries:
    lrow = by_key[('left', e['left'][0])]; rrow = by_key[('right', e['right'][0])]
    vals = [e['id'][:12], ', '.join(e['left']), ', '.join(e['right']), lrow['counterparty'], lrow['asset'], ', '.join(sorted(lc)), ', '.join(sorted(rc)),
            lrow['quantity'], rrow['quantity'], e['committedBy'], e['outcome'], ', '.join(d['field'] for d in e['diffs'])]
    for j, v in enumerate(vals, start=1):
        put(ws, r, j, v, fmt=QTY if j in (8, 9) else None, color='9B1C1C' if j in (6, 7) else None)
    r += 1
r += 1
notes = [
    'Reading the engine view:',
    '• Each row is placed in the currency block of its own currency. A matched pair booked in USD but reported in GBP puts the internal quantity in the USD block and the street quantity in the GBP block, so both blocks show an "inside matched" amount that is really one currency break. Column K separates that from genuine field differences.',
    '• The engine leaves Ambiguous rows out of BuildSideBySide, so its Ours and Theirs do not equal the file totals. Columns M to Q add them back and tie each line to the files.',
]
for n in notes:
    ws.merge_cells(start_row=r, start_column=1, end_row=r, end_column=SPAN)
    put(ws, r, 1, n, wrap=True, bold=n.endswith(':'), border=False)
    ws.row_dimensions[r].height = 28 if not n.endswith(':') else 15
    r += 1

widths(ws, [14, 16, 12, 15, 15, 16, 15, 15, 15, 11, 15, 15, 15, 15, 15, 15, 8])
ws.freeze_panes = f'A{p1_first}'
red = FormulaRule(formula=[f'AND(ISNUMBER(F{p2_head + 1}),F{p2_head + 1}<>0)'], fill=fill(FILL['brk']), font=Font(name=F, color='9B1C1C', bold=True))
ws.conditional_formatting.add(f'F{p2_head + 1}:F{p2_last}', red)
for rng in (f'K{p1_first}:K{p1_tot}', f'J{p2_head + 1}:J{p2_last}', f'Q{p2_head + 1}:Q{p2_last}'):
    ws.conditional_formatting.add(rng, CellIsRule(operator='equal', formula=['"✓"'], font=Font(name=F, color='166534', bold=True)))
    ws.conditional_formatting.add(rng, CellIsRule(operator='equal', formula=['"✗"'], fill=fill(FILL['brk']), font=Font(name=F, color='9B1C1C', bold=True)))
splitrule = FormulaRule(formula=[f'AND(ISNUMBER(K{p2_head + 1}),K{p2_head + 1}<>0)'], fill=fill('FEF3C7'), font=Font(name=F, color='92400E', bold=True))
ws.conditional_formatting.add(f'K{p2_head + 1}:K{p2_last}', splitrule)
ws.page_setup.orientation = 'landscape'
ws.page_setup.fitToWidth = 1
ws.sheet_properties.pageSetUpPr.fitToPage = True
ws.page_setup.fitToHeight = 0

# ---------------------------------------------------------------- Engine_Run
run = wb.create_sheet('Engine_Run', 1)
run.sheet_view.showGridLines = False
put(run, 1, 1, 'How this was produced', bold=True, size=14, border=False)
kv = [
    ('Produced by', meta['producedBy']),
    ('Source', f"m-vaysman/recdiffy.net, origin/main at {meta['sourceCommit']}"),
    ('Build', '.NET SDK 10.0.114 (Ubuntu package; the repo pins 10.0.403 with latestFeature roll-forward, so src/RecDiffy and src/RecDiffy.RulePacks.Trading were copied unchanged to a folder outside the repo and built there). Release build, 0 warnings, 0 errors.'),
    ('Harness', 'A console app that calls Reconciler.Compile, Reconciler.Run and ReconciliationResult.BuildSideBySide, and writes the result to JSON. The profile, reference data and xlsx reader are copied verbatim from RecDiffy.Tests (WorkbookAcceptanceTests.cs and Acceptance/XlsxSheet.cs). It contains no matching logic.'),
    ('Input workbook', f"RecDiffy.NET/test-data/trade-reconciliation-fixtures/{meta['workbook']}"),
    ('Profile hash', meta['profileHash']),
    ('Reference data', meta['referenceId']),
    ('Engine version', meta['engineVersion']),
    ('Acceptance check', f"{acc['passed']} of {acc['cases']} Expected Results cases pass (the same checks as Accepts_fixture_v1_membership, counted instead of asserted because xunit could not be restored offline)."),
    ('Not used', 'The claude-sonnet-5.5 dataset behind the 13-view workbook. main has no profile for it and no rules for CUSIP/ticker to ISIN resolution, dirty-to-clean price, fee inference or net cash, so the engine cannot reconcile it yet.'),
]
for i, (k, v) in enumerate(kv, start=3):
    put(run, i, 1, k, bold=True, bg=FILL['sub']); c = put(run, i, 2, v, wrap=True); run.row_dimensions[i].height = 15 * max(1, len(str(v)) // 95 + 1)
r = 3 + len(kv) + 1
put(run, r, 1, 'RunSummary', bold=True, border=False)
r += 1
header(run, r, ['Field', 'Value'])
for k, v in summ.items():
    r += 1
    put(run, r, 1, k[0].upper() + k[1:]); put(run, r, 2, v, fmt=CNT)
widths(run, [20, 110])

wb.save(OUT)
print('saved', OUT, 'lines', len(lines), 'split entries', len(split_entries))
