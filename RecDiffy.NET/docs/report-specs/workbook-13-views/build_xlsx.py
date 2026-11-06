# Builds RecDiffy_Reconciliation_Views_BO_MO.xlsx from the view data in ./data.
# Usage: python build_xlsx.py <output.xlsx>   (needs openpyxl)
import sys, json, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vizcommon import CAT, VIEWS, RES, ASOF, CATS, meta, num, date, steps_for, diff_state, rows_with_cpty, dec
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side as BSide
from openpyxl.utils import get_column_letter as L
from openpyxl.formatting.rule import CellIsRule, FormulaRule, ColorScaleRule, DataBarRule
from openpyxl.workbook.defined_name import DefinedName
from openpyxl.chart import BarChart, Reference

OUT = sys.argv[1]
F = 'Arial'
INK, MUTED = '1F2937', '6B7280'
FILL = dict(head='1F2937', sub='E5E7EB', brk='FDE2E1', tol='FEF3C7', att='F3F4F6', ok='DCFCE7', card='F8FAFC', past='FDE2E1', today='FEF3C7', up='F3F4F6', band='F9FAFB')
TXT = dict(brk='9B1C1C', tol='92400E', att='6B7280', ok='166534')
AMT = '#,##0.00;(#,##0.00);"-"'
QTY = '#,##0;(#,##0);"-"'
CNT = '#,##0;-#,##0;"-"'
PCT = '0.0%'
DATE = 'yyyy-mm-dd'
thin = BSide(style='thin', color='D1D5DB')
BOX = Border(left=thin, right=thin, top=thin, bottom=thin)


def fill(c):
    return PatternFill('solid', start_color=c, end_color=c)


def font(**k):
    k.setdefault('name', F)
    k.setdefault('size', 10)
    return Font(**k)


def put(ws, r, c, v, fmt=None, bold=False, color=None, bg=None, italic=False, wrap=False, align=None, size=10, border=True):
    cell = ws.cell(row=r, column=c, value=v)
    cell.font = font(bold=bold, color=color or INK, italic=italic, size=size)
    if fmt:
        cell.number_format = fmt
    if bg:
        cell.fill = fill(bg)
    if wrap or align:
        cell.alignment = Alignment(wrap_text=wrap, horizontal=align, vertical='top')
    if border:
        cell.border = BOX
    return cell


def header(ws, r, cols, start=1):
    for i, h in enumerate(cols):
        c = put(ws, r, start + i, h, bold=True, color='FFFFFF', bg=FILL['head'], wrap=True)
        c.alignment = Alignment(wrap_text=True, vertical='center')
    ws.row_dimensions[r].height = 30


def value(s):
    """Typed cell value from a data string: number, date, or text."""
    if s is None or s == '':
        return None
    d = date(s)
    if d:
        return d
    n = num(s)
    return n if n is not None else str(s)


def fmt_for(v, kind='amt'):
    if hasattr(v, 'year'):
        return DATE
    if isinstance(v, (int, float)):
        return {'amt': AMT, 'qty': QTY, 'px': 'General'}[kind]
    return None


def widths(ws, ws_widths):
    for i, w in enumerate(ws_widths, start=1):
        ws.column_dimensions[L(i)].width = w


def view_head(ws, vid, span):
    m = meta(vid)
    ws.sheet_view.showGridLines = False
    put(ws, 1, 1, f"{m['id']} · {m['name']}", bold=True, size=14, border=False)
    put(ws, 2, 1, f"{m['office']}  ·  Users: {m['users']}  ·  Question: {m['question']}  ·  Cadence: {m['cadence']}", color=MUTED, border=False)
    put(ws, 3, 1, m['about'], border=False)
    put(ws, 4, 1, 'Prompt that produces this layout from returned rec data (paste the shared data contract from the Prompts tab first):', bold=True, color=MUTED, border=False)
    ws.merge_cells(start_row=5, start_column=1, end_row=5, end_column=span)
    c = put(ws, 5, 1, m['prompt'], wrap=True, bg=FILL['card'], border=False)
    c.alignment = Alignment(wrap_text=True, vertical='top')
    lines = sum(max(1, len(p) // 150 + 1) for p in m['prompt'].split('\n'))
    ws.row_dimensions[5].height = min(409, 14 * lines + 6)
    return 7


wb = Workbook()
wb._named_styles['Normal'].font = Font(name=F, size=10)

# ---------------------------------------------------------------- Index
ix = wb.active
ix.title = 'Index'
ix.sheet_view.showGridLines = False
put(ix, 1, 1, CAT['title'], bold=True, size=16, border=False)
ix.merge_cells('A2:H2'); put(ix, 2, 1, CAT['subtitle'], wrap=True, border=False); ix.row_dimensions[2].height = 30
ix.merge_cells('A3:H3'); put(ix, 3, 1, CAT['dataNote'], wrap=True, color=MUTED, border=False); ix.row_dimensions[3].height = 54
put(ix, 5, 1, 'As-of date', bold=True); put(ix, 5, 2, ASOF, fmt=DATE, bg=FILL['tol'])
put(ix, 5, 3, 'Used by the ageing and settlement-status formulas (BO1, MO5). Change it to re-age the blotter.', color=MUTED, border=False)
wb.defined_names['AsOf'] = DefinedName('AsOf', attr_text='Index!$B$5')
header(ix, 7, ['ID', 'Office', 'View', 'Question it answers', 'Users', 'Cadence', 'Tab', 'What it shows'])
for i, v in enumerate(CAT['views']):
    r = 8 + i
    for j, k in enumerate(['id', 'office', 'name', 'question', 'users', 'cadence']):
        put(ix, r, 1 + j, v[k], wrap=True, bold=(k == 'name'))
    c = put(ix, r, 7, v['sheet'], color='1D4ED8')
    c.hyperlink = f"#'{v['sheet']}'!A1"
    c.font = font(color='1D4ED8', underline='single')
    put(ix, r, 8, v['about'], wrap=True)
    ix.row_dimensions[r].height = 42
r = 8 + len(CAT['views']) + 1
put(ix, r, 1, 'Colour key', bold=True, border=False)
for i, (k, label) in enumerate([('brk', 'Break: root cause'), ('tol', 'Within tolerance'), ('att', 'Consequence of another field (attributed)'), ('ok', 'Agrees / check passes')]):
    put(ix, r + 1 + i, 1, '', bg=FILL[k]); put(ix, r + 1 + i, 2, label, border=False)
put(ix, r + 6, 1, 'Supporting tabs: Prompts (all prompts and the shared data contract), Result_Entries and Result_Rows (the returned rec data the prompts take), Result_JSON (sample entries in the JSON shape), About.', color=MUTED, border=False)
widths(ix, [8, 14, 26, 36, 26, 20, 24, 60])
ix.freeze_panes = 'A8'

# ---------------------------------------------------------------- Result_Entries / Result_Rows (data the prompts take)
re_ = wb.create_sheet('Result_Entries')
cols = ['entryId', 'oracleGroup', 'alignment', 'composition', 'outcome', 'committedBy', 'counterparty', 'instrument', 'assetClass', 'side', 'tradeDate', 'settleDate', 'currency',
        'leftRows', 'rightRows', 'suppressedRows', 'oursQuantity', 'theirsQuantity', 'oursPrice', 'theirsPrice', 'oursNetCash', 'theirsNetCash', 'rootFields', 'diffFields']
header(re_, 1, cols)
for i, e in enumerate(RES['entries']):
    r = 2 + i
    p = e['leftProjection'] or e['rightProjection']
    lp, rp = e['leftProjection'] or {}, e['rightProjection'] or {}
    vals = [e['entryId'], e['oracleGroup'], e['alignment'], e['composition'] or '', e['outcome'], e['committedBy'] or '', p['Counterparty'], e['instrumentLabel'], p['AssetClass'], p['Side'],
            date(p['TradeDate']), date(p['SettleDate']), p['Currency'], '; '.join(e['left']), '; '.join(e['right']), '; '.join(e['suppressedRight']),
            num(lp.get('Quantity')), num(rp.get('Quantity')), num(lp.get('Price')), num(rp.get('Price')), num(lp.get('NetCash')), num(rp.get('NetCash')),
            ', '.join(e.get('rootFields', [])), ', '.join(d['field'] for d in e['diffs'])]
    for j, v in enumerate(vals):
        kind = 'qty' if cols[j].endswith('Quantity') else 'px' if cols[j].endswith('Price') else 'amt'
        put(re_, r, 1 + j, v, fmt=fmt_for(v, kind))
widths(re_, [12, 10, 12, 30, 15, 11, 11, 36, 10, 11, 11, 11, 9, 28, 16, 26, 14, 14, 12, 12, 16, 16, 12, 26])
re_.freeze_panes = 'B2'
re_.auto_filter.ref = f"A1:{L(len(cols))}{1 + len(RES['entries'])}"
NE = 1 + len(RES['entries'])

rr = wb.create_sheet('Result_Rows')
header(rr, 1, ['source', 'rowKey', 'counterparty', 'disposition', 'entryId'])
ROWS = rows_with_cpty()
for i, x in enumerate(ROWS):
    for j, k in enumerate(['source', 'rowKey', 'counterparty', 'disposition', 'entryId']):
        put(rr, 2 + i, 1 + j, x[k])
widths(rr, [9, 16, 13, 22, 12])
rr.freeze_panes = 'A2'
rr.auto_filter.ref = f"A1:E{1 + len(ROWS)}"
NR = 1 + len(ROWS)

# ---------------------------------------------------------------- BO1 blotter
ws = wb.create_sheet(meta('BO1')['sheet'])
r0 = view_head(ws, 'BO1', 19)
put(ws, r0, 1, 'Open items by status', bold=True, border=False)
T0 = r0 + 6  # table header row
data = VIEWS['BO1']
first, last = T0 + 1, T0 + len(data)
for i, s in enumerate(['Past settle date', 'Settles today', 'Upcoming']):
    put(ws, r0 + 1 + i, 1, s); put(ws, r0 + 1 + i, 2, f'=COUNTIF($D${first}:$D${last},A{r0 + 1 + i})', fmt=CNT)
for i, s in enumerate(['Field break', 'Unmatched ours', 'Unmatched theirs']):
    put(ws, r0 + 1 + i, 4, s); put(ws, r0 + 1 + i, 5, f'=COUNTIF($E${first}:$E${last},D{r0 + 1 + i})', fmt=CNT)
put(ws, r0 + 4, 1, 'Total'); put(ws, r0 + 4, 2, f'=SUM(B{r0 + 1}:B{r0 + 3})', fmt=CNT, bold=True)
put(ws, r0 + 4, 4, 'Total'); put(ws, r0 + 4, 5, f'=SUM(E{r0 + 1}:E{r0 + 3})', fmt=CNT, bold=True)
cols = ['Rank', 'Entry', 'Oracle group', 'Status', 'Item type', 'Counterparty', 'Instrument', 'Asset', 'Side', 'Trade date', 'Settle date', 'Age (bus. days)', 'Field', 'Ours', 'Theirs', 'Delta', 'Ccy', 'Impact', 'Suggested action', 'Category']
header(ws, T0, cols)
for i, b in enumerate(data):
    r = T0 + 1 + i
    vals = [b['rank'], b['entry'], b['oracleGroup'], None, b['type'], b['counterparty'], b['instrument'], b['asset'], b['side'], date(b['tradeDate']), date(b['settleDate']), None,
            b['field'], value(b['ours']), value(b['theirs']), value(b['delta']), b['ccy'], num(b['impact']), b['action'], None]
    for j, v in enumerate(vals):
        put(ws, r, 1 + j, v, fmt=fmt_for(v, 'qty' if b['field'] == 'Quantity' and j in (13, 14, 15) else 'px' if b['field'] == 'Price' and j in (13, 14, 15) else 'amt'))
    ws.cell(r, 4).value = f'=IF(K{r}<AsOf,"Past settle date",IF(K{r}=AsOf,"Settles today","Upcoming"))'
    ws.cell(r, 12).value = f'=NETWORKDAYS(J{r},AsOf)-1'
    ws.cell(r, 12).number_format = '0'
    ws.cell(r, 20).value = f'=IF(E{r}="Field break",M{r},E{r})'
rng = f'D{first}:D{last}'
ws.conditional_formatting.add(rng, CellIsRule(operator='equal', formula=['"Past settle date"'], fill=fill(FILL['past']), font=Font(name=F, color=TXT['brk'], bold=True)))
ws.conditional_formatting.add(rng, CellIsRule(operator='equal', formula=['"Settles today"'], fill=fill(FILL['today']), font=Font(name=F, color=TXT['tol'], bold=True)))
ws.conditional_formatting.add(rng, CellIsRule(operator='equal', formula=['"Upcoming"'], fill=fill(FILL['up']), font=Font(name=F, color=MUTED)))
widths(ws, [6, 12, 9, 16, 15, 11, 34, 9, 10, 11, 11, 9, 13, 14, 14, 12, 6, 15, 40, 14])
ws.freeze_panes = ws.cell(T0 + 1, 3)
ws.auto_filter.ref = f'A{T0}:{L(len(cols))}{last}'
put(ws, last + 2, 1, 'Impact: full net cash for a settle-date break or an unmatched item; otherwise the absolute net-cash difference. Ranked within currency only (no FX rates supplied). Status and age are formulas on the as-of date in Index!B5. Category (helper column) feeds MO4 and MO5.', color=MUTED, border=False)
BO1 = dict(sheet=ws.title, first=first, last=last)

# ---------------------------------------------------------------- BO2 side by side
ws = wb.create_sheet(meta('BO2')['sheet'])
FIELDS = ['Quantity', 'Price', 'SettleDate', 'Accrued', 'Fees', 'NetCash']
r0 = view_head(ws, 'BO2', 19)
lg = [('brk', 'Root break'), ('tol', 'Within tolerance'), ('att', 'Consequence of another field'), (None, 'Theirs Fees are derived: their file has no fee column')]
c = 1
for k, label in lg:
    if k:
        put(ws, r0, c, '', bg=FILL[k]); put(ws, r0, c + 1, label, border=False); c += 4
    else:
        put(ws, r0, c, label, color=MUTED, border=False)
H1 = r0 + 2
fixed = ['Entry', 'Oracle group', 'Counterparty', 'Instrument', 'Side', 'Ccy', 'Outcome', 'Pass']
for j, h in enumerate(fixed):
    ws.merge_cells(start_row=H1, start_column=1 + j, end_row=H1 + 1, end_column=1 + j)
    put(ws, H1, 1 + j, h, bold=True, color='FFFFFF', bg=FILL['head'], wrap=True)
    put(ws, H1 + 1, 1 + j, None, bg=FILL['head'])
for k, f in enumerate(FIELDS):
    col = len(fixed) + 1 + 2 * k
    ws.merge_cells(start_row=H1, start_column=col, end_row=H1, end_column=col + 1)
    put(ws, H1, col, f, bold=True, color='FFFFFF', bg=FILL['head'], align='center'); put(ws, H1, col + 1, None, bg=FILL['head'])
    put(ws, H1 + 1, col, 'Ours', bold=True, bg=FILL['sub'], align='center'); put(ws, H1 + 1, col + 1, 'Theirs', bold=True, bg=FILL['sub'], align='center')
dcol = len(fixed) + 1 + 2 * len(FIELDS)
ws.merge_cells(start_row=H1, start_column=dcol, end_row=H1 + 1, end_column=dcol)
put(ws, H1, dcol, 'Δ NetCash (theirs − ours)', bold=True, color='FFFFFF', bg=FILL['head'], wrap=True); put(ws, H1 + 1, dcol, None, bg=FILL['head'])
for i, row in enumerate(VIEWS['BO2']):
    r = H1 + 2 + i
    for j, v in enumerate([row['entry'], row['oracleGroup'], row['counterparty'], row['instrument'], row['side'], row['ccy'], row['outcome'], row['pass_']]):
        c = put(ws, r, 1 + j, v)
    oc = ws.cell(r, 7)
    oc.fill = fill(FILL['brk'] if row['outcome'] == 'Break' else FILL['tol'])
    oc.font = font(bold=True, color=TXT['brk'] if row['outcome'] == 'Break' else TXT['tol'])
    for k, f in enumerate(FIELDS):
        col = len(fixed) + 1 + 2 * k
        cell = row['cells'][f]
        kind = 'qty' if f == 'Quantity' else 'px' if f == 'Price' else 'amt'
        ov, tv = value(cell['ours']), value(cell['theirs'])
        put(ws, r, col, ov, fmt=fmt_for(ov, kind))
        tc = put(ws, r, col + 1, tv, fmt=fmt_for(tv, kind))
        st = cell['state']
        if st == 'break':
            tc.fill = fill(FILL['brk']); tc.font = font(bold=True, color=TXT['brk'])
        elif st == 'tolerance':
            tc.fill = fill(FILL['tol']); tc.font = font(bold=True, color=TXT['tol'])
        elif st == 'attributed':
            tc.fill = fill(FILL['att']); tc.font = font(italic=True, color=TXT['att'])
    oc_n = len(fixed) + 1 + 2 * FIELDS.index('NetCash')
    put(ws, r, dcol, f'={L(oc_n + 1)}{r}-{L(oc_n)}{r}', fmt=AMT, bold=True)
widths(ws, [12, 9, 11, 32, 10, 6, 15, 6] + [13, 13] * len(FIELDS) + [15])
ws.freeze_panes = ws.cell(H1 + 2, 3)
put(ws, H1 + 3 + len(VIEWS['BO2']), 1, 'Theirs Fees are derived from their net cash, principal and accrued, because the counterparty file carries no commission column. Packages are excluded from fee inference.', color=MUTED, border=False)

# ---------------------------------------------------------------- BO3 raw vs normalized
ws = wb.create_sheet(meta('BO3')['sheet'])
r = view_head(ws, 'BO3', 13)
CONCEPTS = ['Line', 'Row ID', 'Counterparty', 'Instrument ID', 'Trade date', 'Settle date', 'Side', 'Quantity', 'Price', 'Accrued', 'Quote convention / perspective', 'Net cash', 'Fees']
for card in VIEWS['BO3']:
    o, t, s, ir = card['ours'], card['theirs'], card['streetRaw'], card['internalRaw']
    ws.merge_cells(start_row=r, start_column=1, end_row=r, end_column=13)
    put(ws, r, 1, f"{card['oracleGroup']} · {card['entry']} · {card['why']} · matched by {card['pass_']} · outcome {card['outcome']}", bold=True, color='FFFFFF', bg=FILL['head'])
    header(ws, r + 1, CONCEPTS)
    ws.row_dimensions[r + 1].height = 18
    lines = [
        ('Ours, as received', [ir['Internal_Trade_ID'], ir['Counterparty_Code'], o['Instrument'], ir['Trade_Date'], ir['Settle_Date'], ir['Side'], ir['Quantity_Notional'], ir['Clean_Price_Rate'], ir['Accrued_Interest'], '(internal: clean price)', ir['Net_Settlement_Amount'], ir['Commission_Fees']]),
        ('Theirs, as received', [s['Cpty_Report_ID'], s['Reporting_Counterparty_Name'], f"{s['Security_ID_Source']} {s['Primary_Security_ID']}", s['Trade_Date'], s['Value_Settle_Date'], s['Dir_Ind'] or '(blank)', s['Reported_Qty_Notional'], s['Exec_Price_Rate'], s['Reported_Accrued_Int'] or '(blank)', f"{s['Price_Quote_Convention']} / {s['Reporting_Perspective']}", s['Net_Cash_Amount'], '(not sent)']),
        ('Theirs, normalized', [s['Cpty_Report_ID'], t['Counterparty'], t['Instrument'], t['TradeDate'], t['SettleDate'], t['Side'], t['Quantity'], t['Price'], t['Accrued'], 'canonical', t['NetCash'], t['Fees'] if t['Fees'] is not None else '(not inferred)']),
    ]
    dmap = {d['field']: d for d in card['diffs']}
    canon_field = {6: 'Side', 7: 'Quantity', 8: 'Price', 9: 'Accrued', 11: 'NetCash', 12: 'Fees', 5: 'SettleDate'}
    for li, (name, vals) in enumerate(lines):
        rr_ = r + 2 + li
        put(ws, rr_, 1, name, bold=True, bg=FILL['sub'] if li < 2 else FILL['card'])
        for j, v in enumerate(vals):
            raw_text = li == 1
            cv = str(v) if raw_text else value(v)
            kind = 'qty' if j == 6 else 'px' if j == 7 else 'amt'
            c = put(ws, rr_, 2 + j, cv, fmt=None if raw_text else fmt_for(cv, kind))
            if raw_text:
                c.font = font(color=MUTED)
            if li == 2 and (j + 1) in canon_field:
                st = diff_state(dmap.get(canon_field[j + 1]))
                if st == 'break':
                    c.fill = fill(FILL['brk']); c.font = font(bold=True, color=TXT['brk'])
                elif st == 'tolerance':
                    c.fill = fill(FILL['tol']); c.font = font(bold=True, color=TXT['tol'])
                elif st == 'attributed':
                    c.fill = fill(FILL['att']); c.font = font(italic=True, color=TXT['att'])
    rr_ = r + 6
    put(ws, rr_, 1, 'Normalization steps', bold=True, border=False)
    for k, (rule, raw, canon) in enumerate(steps_for(card)):
        put(ws, rr_ + 1 + k, 2, rule); ws.merge_cells(start_row=rr_ + 1 + k, start_column=3, end_row=rr_ + 1 + k, end_column=7)
        put(ws, rr_ + 1 + k, 3, f'{raw}  →  {canon}')
    rr_ += 2 + len(steps_for(card))
    put(ws, rr_, 1, 'Differences', bold=True, border=False)
    if not card['diffs']:
        put(ws, rr_ + 1, 2, 'None: exact after normalization', color=TXT['ok'], bg=FILL['ok'])
        rr_ += 2
    else:
        for k, h in enumerate(['Field', 'Ours', 'Theirs', 'Delta', 'Outcome', 'Attributed to']):
            put(ws, rr_ + 1, 2 + k, h, bold=True, bg=FILL['sub'])
        for k, d in enumerate(card['diffs']):
            st = diff_state(d)
            vals = [d['field'], value(d['left']), value(d['right']), value(d['delta']), d['outcome'], ', '.join(d['attributedTo'])]
            for m, v in enumerate(vals):
                c = put(ws, rr_ + 2 + k, 2 + m, v, fmt=fmt_for(v, 'qty' if d['field'] == 'Quantity' else 'px' if d['field'] == 'Price' else 'amt'))
            oc = ws.cell(rr_ + 2 + k, 6)
            oc.fill = fill({'break': FILL['brk'], 'tolerance': FILL['tol'], 'attributed': FILL['att']}[st])
        rr_ += 3 + len(card['diffs'])
    r = rr_ + 1
widths(ws, [20, 24, 15, 22, 13, 13, 11, 14, 12, 12, 30, 14, 14])

# ---------------------------------------------------------------- BO4 lifecycle chains
ws = wb.create_sheet(meta('BO4')['sheet'])
r = view_head(ws, 'BO4', 9)
for ch in VIEWS['BO4']:
    ws.merge_cells(start_row=r, start_column=1, end_row=r, end_column=9)
    put(ws, r, 1, f"{ch['oracleGroup']} · {ch['counterparty']} · {ch['instrument']} · correction type: {ch['bust']}", bold=True, color='FFFFFF', bg=FILL['head'])
    header(ws, r + 1, ['', 'Report', 'Action', 'Target', 'Quantity', 'Price', 'Settle date', 'Net cash', 'Disposition'])
    ws.row_dimensions[r + 1].height = 18
    new, corr = ch['rows'][0], ch['rows'][2]
    changed = {k for k in ('qty', 'price', 'settle', 'net') if new[k] != corr[k]}
    for k, x in enumerate(ch['rows']):
        rr_ = r + 2 + k
        live = x['disposition'].startswith('Survivor')
        vals = ['↳' * (k + 1), x['report'], x['action'], x['target'], num(x['qty']), num(x['price']), date(x['settle']), num(x['net']), x['disposition']]
        for j, v in enumerate(vals):
            c = put(ws, rr_, 1 + j, v, fmt=fmt_for(v, 'qty' if j == 4 else 'px' if j == 5 else 'amt'), bold=live)
            if not live:
                c.font = font(color=MUTED, strike=True)
        if live:
            for j, key in zip([5, 6, 7, 8], ['qty', 'price', 'settle', 'net']):
                if key in changed:
                    ws.cell(rr_, j).fill = fill(FILL['tol'])
    rr_ = r + 5
    put(ws, rr_, 1, 'Matched internal', bold=True, bg=FILL['ok'])
    for j, v in enumerate([ch['matchedInternal'], f"pass {ch['pass_']}", f"outcome {ch['outcome']}", num(ch['internalQty']), num(ch['internalPrice']), None, num(ch['internalNet']), '']):
        put(ws, rr_, 2 + j, v, fmt=fmt_for(v, 'qty' if j == 3 else 'px' if j == 4 else 'amt'), bg=FILL['ok'])
    what = ', '.join(f"{lbl} {new[k]} → {corr[k]}" for k, lbl in (('qty', 'quantity'), ('price', 'price'), ('settle', 'settle date'), ('net', 'net cash')) if k in changed)
    ws.merge_cells(start_row=rr_ + 1, start_column=2, end_row=rr_ + 1, end_column=9)
    put(ws, rr_ + 1, 1, 'What the correction fixed', bold=True, border=False); put(ws, rr_ + 1, 2, what, border=False)
    r = rr_ + 3
widths(ws, [22, 15, 8, 15, 14, 11, 12, 15, 20])

# ---------------------------------------------------------------- BO5 allocations
ws = wb.create_sheet(meta('BO5')['sheet'])
r = view_head(ws, 'BO5', 8)
ws.sheet_properties.outlinePr.summaryBelow = False
for g in VIEWS['BO5']:
    ws.merge_cells(start_row=r, start_column=1, end_row=r, end_column=8)
    put(ws, r, 1, f"{g['oracleGroup']} · {g['counterparty']} · {g['instrument']} · {g['side']}", bold=True, color='FFFFFF', bg=FILL['head'])
    header(ws, r + 1, ['Line', 'Row', 'Book', 'Quantity', 'Price', 'Net cash', 'Quantity check', 'Net cash check'])
    ws.row_dimensions[r + 1].height = 18
    b = r + 2
    for j, v in enumerate(['Street block', g['block']['id'], '', num(g['block']['qty']), num(g['block']['price']), num(g['block']['net'])]):
        put(ws, b, 1 + j, v, fmt=fmt_for(v, 'qty' if j == 3 else 'px' if j == 4 else 'amt'), bold=True, bg=FILL['sub'])
    for k, m in enumerate(g['members']):
        rr_ = b + 1 + k
        for j, v in enumerate(['   allocation', m['id'], m['book'], num(m['qty']), num(m['price']), num(m['net'])]):
            put(ws, rr_, 1 + j, v, fmt=fmt_for(v, 'qty' if j == 3 else 'px' if j == 4 else 'amt'))
        ws.row_dimensions[rr_].outlineLevel = 1
    t = b + 1 + len(g['members'])
    put(ws, t, 1, 'Allocations total', bold=True)
    put(ws, t, 4, f'=SUM(D{b + 1}:D{t - 1})', fmt=QTY, bold=True)
    put(ws, t, 6, f'=SUM(F{b + 1}:F{t - 1})', fmt=AMT, bold=True)
    put(ws, t, 7, f'=IF(D{t}=D{b},"✓ equal","✗ "&TEXT(D{t}-D{b},"#,##0"))', bold=True)
    put(ws, t, 8, f'=IF(ROUND(F{t}-F{b},2)=0,"✓ equal","✗ "&TEXT(F{t}-F{b},"#,##0.00"))', bold=True)
    for c in (7, 8):
        ws.conditional_formatting.add(f'{L(c)}{t}', FormulaRule(formula=[f'LEFT({L(c)}{t},1)="✓"'], fill=fill(FILL['ok']), font=Font(name=F, color=TXT['ok'], bold=True)))
        ws.conditional_formatting.add(f'{L(c)}{t}', FormulaRule(formula=[f'LEFT({L(c)}{t},1)="✗"'], fill=fill(FILL['brk']), font=Font(name=F, color=TXT['brk'], bold=True)))
    r = t + 2
widths(ws, [18, 14, 16, 13, 11, 15, 14, 16])

# ---------------------------------------------------------------- BO6 cash ladder
ws = wb.create_sheet(meta('BO6')['sheet'])
r0 = view_head(ws, 'BO6', 10)
put(ws, r0, 1, 'Sign convention: receive +, pay −. Difference = theirs − ours = unmatched ours + unmatched theirs + differences inside matched entries.', color=MUTED, border=False)
H = r0 + 1
header(ws, H, ['Settle date', 'Ccy', 'Items (ours)', 'Ours: expected cash', 'Theirs: expected cash', 'Difference', 'Unmatched ours', 'Unmatched theirs', 'Inside matched entries', 'Check'])
lad = VIEWS['BO6']
for i, x in enumerate(lad):
    r = H + 1 + i
    vals = [date(x['settleDate']), x['ccy'], x['items'], num(x['ours']), num(x['theirs']), None, num(x['unmatchedOurs']), num(x['unmatchedTheirs']), num(x['inGroup'])]
    for j, v in enumerate(vals):
        put(ws, r, 1 + j, v, fmt=CNT if j == 2 else fmt_for(v))
    put(ws, r, 6, f'=E{r}-D{r}', fmt=AMT, bold=True)
    put(ws, r, 10, f'=IF(ROUND(F{r}-(G{r}+H{r}+I{r}),2)=0,"✓","✗")', align='center')
LF, LL = H + 1, H + len(lad)
ws.conditional_formatting.add(f'A{LF}:A{LL}', FormulaRule(formula=[f'A{LF}<AsOf'], fill=fill(FILL['sub'])))
ws.conditional_formatting.add(f'F{LF}:F{LL}', CellIsRule(operator='notEqual', formula=['0'], font=Font(name=F, color=TXT['brk'], bold=True)))
ws.conditional_formatting.add(f'J{LF}:J{LL}', CellIsRule(operator='equal', formula=['"✗"'], fill=fill(FILL['brk'])))
S = LL + 2
put(ws, S, 1, 'Totals by currency (never added across currencies)', bold=True, border=False)
header(ws, S + 1, ['Ccy', '', 'Items (ours)', 'Ours: expected cash', 'Theirs: expected cash', 'Difference', 'Unmatched ours', 'Unmatched theirs', 'Inside matched entries', 'Check'])
ccys = [c for c in ['USD', 'EUR', 'GBP', 'CHF', 'CAD', 'JPY'] if any(x['ccy'] == c for x in lad)]
for i, c in enumerate(ccys):
    r = S + 2 + i
    put(ws, r, 1, c, bold=True); put(ws, r, 2, '')
    for col in (3, 4, 5, 7, 8, 9):
        put(ws, r, col, f'=SUMIF($B${LF}:$B${LL},$A{r},{L(col)}${LF}:{L(col)}${LL})', fmt=CNT if col == 3 else AMT)
    put(ws, r, 6, f'=E{r}-D{r}', fmt=AMT, bold=True)
    put(ws, r, 10, f'=IF(ROUND(F{r}-(G{r}+H{r}+I{r}),2)=0,"✓","✗")', align='center')
widths(ws, [12, 6, 9, 20, 20, 17, 17, 17, 18, 7])
ws.freeze_panes = ws.cell(H + 1, 3)

# ---------------------------------------------------------------- MO1 control totals
ws = wb.create_sheet(meta('MO1')['sheet'])
r0 = view_head(ws, 'MO1', 13)
mo1 = VIEWS['MO1']
cps, assets = mo1['counterparties'], mo1['assets']
put(ws, r0, 1, 'Part 1 · Row counts after lifecycle collapse: ours | theirs | Δ (theirs − ours)', bold=True, border=False)
H = r0 + 1
put(ws, H, 1, 'Counterparty', bold=True, color='FFFFFF', bg=FILL['head'])
ws.merge_cells(start_row=H, start_column=1, end_row=H + 1, end_column=1)
for k, a in enumerate(assets):
    col = 2 + 3 * k
    ws.merge_cells(start_row=H, start_column=col, end_row=H, end_column=col + 2)
    put(ws, H, col, a, bold=True, color='FFFFFF', bg=FILL['head'], align='center')
    for m, h in enumerate(['Ours', 'Theirs', 'Δ']):
        put(ws, H + 1, col + m, h, bold=True, bg=FILL['sub'], align='center')
for i, c in enumerate(cps):
    r = H + 2 + i
    put(ws, r, 1, c, bold=True)
    for k, a in enumerate(assets):
        col = 2 + 3 * k
        o, t = mo1['counts'][f'{c}|{a}']
        put(ws, r, col, o, fmt=CNT); put(ws, r, col + 1, t, fmt=CNT)
        put(ws, r, col + 2, f'={L(col + 1)}{r}-{L(col)}{r}', fmt='+#,##0;-#,##0;"-"', bold=True)
tr = H + 2 + len(cps)
put(ws, tr, 1, 'Total', bold=True, bg=FILL['sub'])
for col in range(2, 2 + 3 * len(assets)):
    put(ws, tr, col, f'=SUM({L(col)}{H + 2}:{L(col)}{tr - 1})', fmt=CNT if (col - 2) % 3 < 2 else '+#,##0;-#,##0;"-"', bold=True, bg=FILL['sub'])
for k in range(len(assets)):
    col = L(4 + 3 * k)
    ws.conditional_formatting.add(f'{col}{H + 2}:{col}{tr}', CellIsRule(operator='notEqual', formula=['0'], fill=fill(FILL['brk']), font=Font(name=F, color=TXT['brk'], bold=True)))
P2 = tr + 3
put(ws, P2 - 1, 1, 'Part 2 · USD bond face by counterparty, with the difference decomposed', bold=True, border=False)
header(ws, P2, ['Counterparty', 'Rows ours', 'Rows theirs', 'Face ours', 'Face theirs', 'Δ face', 'Unmatched ours', 'Unmatched theirs', 'Inside matched entries', 'Check'])
face = VIEWS['MO1face']
for i, x in enumerate(face):
    r = P2 + 1 + i
    for j, v in enumerate([x['counterparty'], x['rowsOurs'], x['rowsTheirs'], num(x['ours']), num(x['theirs']), None, num(x['leftOnly']), num(x['rightOnly']), num(x['matchedDelta'])]):
        put(ws, r, 1 + j, v, fmt=CNT if j in (1, 2) else QTY if j else None, bold=(j == 0))
    put(ws, r, 6, f'=E{r}-D{r}', fmt=QTY, bold=True)
    put(ws, r, 10, f'=IF(F{r}=G{r}+H{r}+I{r},"✓","✗")', align='center')
ft = P2 + 1 + len(face)
put(ws, ft, 1, 'Total', bold=True, bg=FILL['sub'])
for col in range(2, 10):
    put(ws, ft, col, f'=SUM({L(col)}{P2 + 1}:{L(col)}{ft - 1})', fmt=CNT if col in (2, 3) else QTY, bold=True, bg=FILL['sub'])
put(ws, ft, 10, f'=IF(F{ft}=G{ft}+H{ft}+I{ft},"✓","✗")', align='center', bg=FILL['sub'])
ws.conditional_formatting.add(f'F{P2 + 1}:F{ft - 1}', FormulaRule(formula=[f'ABS(F{P2 + 1})=MAX(ABS($F${P2 + 1}:$F${ft - 1}))'], fill=fill(FILL['brk']), font=Font(name=F, color=TXT['brk'], bold=True)))
widths(ws, [14] + [9] * (3 * len(assets)))
for col, w in zip('DEFGHIJ', [15, 15, 15, 15, 15, 17, 7]):
    ws.column_dimensions[col].width = max(ws.column_dimensions[col].width or 0, w)

# ---------------------------------------------------------------- MO2 scorecard (formulas on Result_Entries / Result_Rows)
ws = wb.create_sheet(meta('MO2')['sheet'])
r0 = view_head(ws, 'MO2', 12)
E_ = lambda col: f"Result_Entries!${col}$2:${col}${NE}"
R_ = lambda col: f"Result_Rows!${col}$2:${col}${NR}"
kpis = [('Internal rows', f'=COUNTIF({R_("A")},"left")', CNT), ('Counterparty rows', f'=COUNTIF({R_("A")},"right")', CNT),
        ('Result entries', f'=COUNTA({E_("A")})', CNT), ('Matched entries', f'=COUNTIF({E_("C")},"Matched")', CNT),
        ('Auto-match rate', f'=COUNTIFS({R_("A")},"left",{R_("D")},"Matched")/COUNTIF({R_("A")},"left")', PCT),
        ('Clean-match rate', f'=(COUNTIF({E_("E")},"Exact")+COUNTIF({E_("E")},"WithinTolerance"))/COUNTIF({E_("C")},"Matched")', PCT),
        ('Breaks', f'=COUNTIF({E_("E")},"Break")', CNT), ('Unmatched ours', f'=COUNTIF({E_("C")},"LeftOnly")', CNT), ('Unmatched theirs', f'=COUNTIF({E_("C")},"RightOnly")', CNT)]
for k, (label, f, fmt) in enumerate(kpis):
    col = 1 + (k % 5) * 2
    rr_ = r0 + (k // 5) * 3
    put(ws, rr_, col, label, color=MUTED, bg=FILL['card'])
    ws.merge_cells(start_row=rr_, start_column=col, end_row=rr_, end_column=col + 1)
    ws.merge_cells(start_row=rr_ + 1, start_column=col, end_row=rr_ + 1, end_column=col + 1)
    c = put(ws, rr_ + 1, col, f, fmt=fmt, bold=True, size=16, bg=FILL['card'])
    ws.row_dimensions[rr_ + 1].height = 26
pr = r0 + 6
put(ws, pr, 1, 'Commits by pass', bold=True, border=False)
for k, p in enumerate(['P1', 'P2', 'P3', 'P4']):
    put(ws, pr + 1, 1 + k, p, bold=True, bg=FILL['sub'], align='center')
    put(ws, pr + 2, 1 + k, f'=COUNTIF({E_("F")},"{p}")', fmt=CNT, align='center')
H = pr + 4
cols = ['Counterparty', 'Internal rows', 'Counterparty rows', 'Matched entries', 'Exact', 'Within tolerance', 'Breaks', 'Unmatched ours', 'Unmatched theirs', 'Auto-match rate', 'Clean-match rate']
header(ws, H, cols)
cps_sorted = [x['counterparty'] for x in sorted(VIEWS['MO2']['byCounterparty'], key=lambda s: -s['matchRate'])]
for i, c in enumerate(cps_sorted):
    r = H + 1 + i
    put(ws, r, 1, c, bold=True)
    put(ws, r, 2, f'=COUNTIFS({R_("A")},"left",{R_("C")},$A{r})', fmt=CNT)
    put(ws, r, 3, f'=COUNTIFS({R_("A")},"right",{R_("C")},$A{r})', fmt=CNT)
    put(ws, r, 4, f'=COUNTIFS({E_("G")},$A{r},{E_("C")},"Matched")', fmt=CNT)
    put(ws, r, 5, f'=COUNTIFS({E_("G")},$A{r},{E_("E")},"Exact")', fmt=CNT)
    put(ws, r, 6, f'=COUNTIFS({E_("G")},$A{r},{E_("E")},"WithinTolerance")', fmt=CNT)
    put(ws, r, 7, f'=COUNTIFS({E_("G")},$A{r},{E_("E")},"Break")', fmt=CNT)
    put(ws, r, 8, f'=COUNTIFS({E_("G")},$A{r},{E_("C")},"LeftOnly")', fmt=CNT)
    put(ws, r, 9, f'=COUNTIFS({E_("G")},$A{r},{E_("C")},"RightOnly")', fmt=CNT)
    put(ws, r, 10, f'=IF(B{r}=0,0,COUNTIFS({R_("A")},"left",{R_("C")},$A{r},{R_("D")},"Matched")/B{r})', fmt=PCT)
    put(ws, r, 11, f'=IF(D{r}=0,0,(E{r}+F{r})/D{r})', fmt=PCT)
SL = H + len(cps_sorted)
ws.conditional_formatting.add(f'J{H + 1}:J{SL}', DataBarRule(start_type='num', start_value=0, end_type='num', end_value=1, color='60A5FA'))
ws.conditional_formatting.add(f'K{H + 1}:K{SL}', CellIsRule(operator='greaterThanOrEqual', formula=['0.95'], fill=fill(FILL['ok']), font=Font(name=F, color=TXT['ok'], bold=True)))
ws.conditional_formatting.add(f'K{H + 1}:K{SL}', CellIsRule(operator='between', formula=['0.85', '0.9499999'], fill=fill(FILL['tol']), font=Font(name=F, color=TXT['tol'], bold=True)))
ws.conditional_formatting.add(f'K{H + 1}:K{SL}', CellIsRule(operator='lessThan', formula=['0.85'], fill=fill(FILL['brk']), font=Font(name=F, color=TXT['brk'], bold=True)))
put(ws, SL + 2, 1, 'Every number on this tab is a formula over Result_Entries and Result_Rows. Auto-match rate = internal rows in matched entries ÷ internal rows. Clean-match rate = (exact + within tolerance) ÷ matched entries.', color=MUTED, border=False)
widths(ws, [14, 12, 13, 12, 10, 12, 10, 13, 13, 14, 14])

# ---------------------------------------------------------------- MO3 funnel
ws = wb.create_sheet(meta('MO3')['sheet'])
r0 = view_head(ws, 'MO3', 7)
H = r0
header(ws, H, ['Stage', 'Ours: change', 'Ours: remaining', 'Theirs: change', 'Theirs: remaining', 'Note'])
stages = VIEWS['MO3']
for i, s in enumerate(stages):
    r = H + 1 + i
    put(ws, r, 1, s['stage'], bold=True)
    if i == 0:
        put(ws, r, 2, None); put(ws, r, 3, s['left'], fmt=CNT, bold=True); put(ws, r, 4, None); put(ws, r, 5, s['right'], fmt=CNT, bold=True)
    elif s['stage'] == 'Residue':
        put(ws, r, 2, None); put(ws, r, 3, f'=COUNTIF({E_("C")},"LeftOnly")', fmt=CNT, bold=True, bg=FILL['tol'])
        put(ws, r, 4, None); put(ws, r, 5, f'=COUNTIF({E_("C")},"RightOnly")', fmt=CNT, bold=True, bg=FILL['tol'])
    else:
        prev = stages[i - 1]
        if s['stage'].startswith('P'):
            dl, dr = s['left'], s['right']
        else:
            dl, dr = s['left'] - prev['left'] if not prev['stage'].startswith('P') else 0, s['right'] - prev['right']
        put(ws, r, 2, dl, fmt='+#,##0;-#,##0;"-"'); put(ws, r, 3, f'=C{r - 1}+B{r}', fmt=CNT, bold=True)
        put(ws, r, 4, dr, fmt='+#,##0;-#,##0;"-"'); put(ws, r, 5, f'=E{r - 1}+D{r}', fmt=CNT, bold=True)
    put(ws, r, 6, s['note'], color=MUTED)
last_pass = H + len(stages) - 1
res = H + len(stages)
put(ws, res + 1, 1, 'Check: remaining after P4 = residue', bold=True)
put(ws, res + 1, 3, f'=IF(C{last_pass}=C{res},"✓","✗")', align='center'); put(ws, res + 1, 5, f'=IF(E{last_pass}=E{res},"✓","✗")', align='center')
ch = BarChart(); ch.type = 'bar'; ch.style = 10; ch.title = 'Items remaining after each stage'
ch.y_axis.title = 'Items'; ch.x_axis.title = None
data = Reference(ws, min_col=3, min_row=H, max_row=res); ch.add_data(data, titles_from_data=True)
data2 = Reference(ws, min_col=5, min_row=H, max_row=res); ch.add_data(data2, titles_from_data=True)
ch.set_categories(Reference(ws, min_col=1, min_row=H + 1, max_row=res))
ch.x_axis.scaling.orientation = 'maxMin'
ch.height, ch.width = 9, 18
ws.add_chart(ch, f'A{res + 4}')
widths(ws, [34, 13, 15, 13, 15, 60])

# ---------------------------------------------------------------- MO4 heatmap (formulas on BO1)
ws = wb.create_sheet(meta('MO4')['sheet'])
r0 = view_head(ws, 'MO4', 9)
mo4 = VIEWS['MO4']
tot = {c: sum(mo4['counts'][f'{c}|{k}'] for k in CATS) for c in mo4['counterparties']}
order = sorted(mo4['counterparties'], key=lambda c: (-tot[c], c))
H = r0
header(ws, H, ['Counterparty'] + CATS + ['Total'])
B1 = f"'{BO1['sheet']}'"
for i, c in enumerate(order):
    r = H + 1 + i
    put(ws, r, 1, c, bold=True)
    for k, cat in enumerate(CATS):
        put(ws, r, 2 + k, f'=COUNTIFS({B1}!$F${BO1["first"]}:$F${BO1["last"]},$A{r},{B1}!$T${BO1["first"]}:$T${BO1["last"]},{L(2 + k)}${H})', fmt=CNT, align='center')
    put(ws, r, 2 + len(CATS), f'=SUM(B{r}:{L(1 + len(CATS))}{r})', fmt=CNT, bold=True, align='center')
tr = H + 1 + len(order)
put(ws, tr, 1, 'Total', bold=True, bg=FILL['sub'])
for col in range(2, 3 + len(CATS)):
    put(ws, tr, col, f'=SUM({L(col)}{H + 1}:{L(col)}{tr - 1})', fmt=CNT, bold=True, bg=FILL['sub'], align='center')
ws.conditional_formatting.add(f'B{H + 1}:{L(1 + len(CATS))}{tr - 1}', ColorScaleRule(start_type='num', start_value=0, start_color='FFFFFF', end_type='max', end_color='C2410C'))
put(ws, tr + 2, 1, 'Counts are COUNTIFS formulas over the BO1 blotter (Counterparty × Category).', color=MUTED, border=False)
widths(ws, [14, 10, 10, 11, 9, 15, 16, 9])

# ---------------------------------------------------------------- MO5 ageing (formulas on BO1)
ws = wb.create_sheet(meta('MO5')['sheet'])
r0 = view_head(ws, 'MO5', 10)
H = r0
ages = ['1', '2', '3', '4+']
stats = ['Past settle date', 'Settles today', 'Upcoming']
header(ws, H, ['Category'] + [f'Age {a} bus. day' + ('' if a == '1' else 's') for a in ages] + stats + ['Total'])
for i, cat in enumerate(CATS):
    r = H + 1 + i
    put(ws, r, 1, cat, bold=True)
    rngC = f'{B1}!$T${BO1["first"]}:$T${BO1["last"]}'
    rngA = f'{B1}!$L${BO1["first"]}:$L${BO1["last"]}'
    rngS = f'{B1}!$D${BO1["first"]}:$D${BO1["last"]}'
    for k, a in enumerate(ages):
        crit = '">=4"' if a == '4+' else a
        put(ws, r, 2 + k, f'=COUNTIFS({rngC},$A{r},{rngA},{crit})', fmt=CNT, align='center')
    for k, s in enumerate(stats):
        put(ws, r, 6 + k, f'=COUNTIFS({rngC},$A{r},{rngS},"{s}")', fmt=CNT, align='center')
    put(ws, r, 9, f'=SUM(B{r}:E{r})', fmt=CNT, bold=True, align='center')
tr = H + 1 + len(CATS)
put(ws, tr, 1, 'Total', bold=True, bg=FILL['sub'])
for col in range(2, 10):
    put(ws, tr, col, f'=SUM({L(col)}{H + 1}:{L(col)}{tr - 1})', fmt=CNT, bold=True, bg=FILL['sub'], align='center')
ws.conditional_formatting.add(f'B{H + 1}:E{tr - 1}', ColorScaleRule(start_type='num', start_value=0, start_color='FFFFFF', end_type='max', end_color='6D28D9'))
ws.conditional_formatting.add(f'F{H + 1}:F{tr - 1}', CellIsRule(operator='greaterThan', formula=['0'], fill=fill(FILL['brk']), font=Font(name=F, color=TXT['brk'], bold=True)))
put(ws, tr + 2, 1, 'Age = business days from trade date to the as-of date (Index!B5), computed in BO1. Change the as-of date to re-age.', color=MUTED, border=False)
widths(ws, [17, 11, 11, 11, 11, 15, 13, 11, 8])

# ---------------------------------------------------------------- MO6 tolerance usage
ws = wb.create_sheet(meta('MO6')['sheet'])
r0 = view_head(ws, 'MO6', 11)
put(ws, r0, 1, 'Tolerances (Sonnet profile): Accrued 1.25 · FX forward rate 0.0001 · Fees 0.05. Boundaries are inclusive.', color=MUTED, border=False)
H = r0 + 1
header(ws, H, ['Entry', 'Oracle group', 'Counterparty', 'Instrument', 'Field', 'Ours', 'Theirs', 'Delta', 'Tolerance', '% of tolerance used', 'Flag'])
for i, t in enumerate(VIEWS['MO6']):
    r = H + 1 + i
    kind = 'px' if t['field'] == 'Price' else 'amt'
    vals = [t['entry'], t['oracleGroup'], t['counterparty'], t['instrument'], t['field'], num(t['ours']), num(t['theirs']), num(t['delta']), num(t['tolerance'])]
    for j, v in enumerate(vals):
        put(ws, r, 1 + j, v, fmt=('General' if kind == 'px' or j == 8 else AMT) if isinstance(v, (int, float)) else None)
    put(ws, r, 10, f'=ABS(H{r})/I{r}', fmt=PCT, bold=True)
    put(ws, r, 11, f'=IF(J{r}>=1,"On the boundary","")', color=TXT['tol'])
TL = H + len(VIEWS['MO6'])
ws.conditional_formatting.add(f'J{H + 1}:J{TL}', DataBarRule(start_type='num', start_value=0, end_type='num', end_value=1, color='F59E0B'))
ws.conditional_formatting.add(f'K{H + 1}:K{TL}', CellIsRule(operator='equal', formula=['"On the boundary"'], fill=fill(FILL['tol'])))
widths(ws, [12, 10, 12, 34, 9, 13, 13, 11, 10, 18, 16])
ws.freeze_panes = ws.cell(H + 1, 2)

# ---------------------------------------------------------------- MO7 audit trail
ws = wb.create_sheet(meta('MO7')['sheet'])
r = view_head(ws, 'MO7', 4)
VC = {'Committed': ('ok', 'ok'), 'Contest': ('tol', 'tol'), 'NoKey': ('att', 'att'), 'NoCandidate': ('att', 'att'), 'Lifecycle': ('sub', None), 'Package': ('sub', None), 'NearestCandidate': ('card', None)}
for t in VIEWS['MO7']:
    ws.merge_cells(start_row=r, start_column=1, end_row=r, end_column=4)
    put(ws, r, 1, f"{t['oracleGroup']} · {t['entry']} · {t['rows']} · {t['result']}", bold=True, color='FFFFFF', bg=FILL['head'])
    header(ws, r + 1, ['Step', 'Verdict', 'Key or detail', ''])
    ws.row_dimensions[r + 1].height = 18
    for k, (step, verdict, detail) in enumerate(t['trail']):
        rr_ = r + 2 + k
        bg, tx = VC.get(verdict, ('card', None))
        put(ws, rr_, 1, step, bold=True)
        put(ws, rr_, 2, verdict, bold=True, bg=FILL[bg], color=TXT.get(tx) if tx else INK, italic=(verdict == 'NearestCandidate'))
        ws.merge_cells(start_row=rr_, start_column=3, end_row=rr_, end_column=4)
        put(ws, rr_, 3, detail, wrap=True, italic=(verdict == 'NearestCandidate'))
    r = r + 3 + len(t['trail'])
widths(ws, [12, 18, 70, 10])

# ---------------------------------------------------------------- Prompts
ws = wb.create_sheet('Prompts')
ws.sheet_view.showGridLines = False
put(ws, 1, 1, 'Prompts', bold=True, size=14, border=False)
put(ws, 2, 1, 'Paste the shared data contract first, then one view prompt, then the returned rec data (JSON in the shape shown on Result_JSON).', color=MUTED, border=False)
put(ws, 4, 1, 'Shared data contract', bold=True, bg=FILL['sub'])
ws.merge_cells('B4:D4')
c = put(ws, 4, 2, CAT['preamble'], wrap=True, bg=FILL['card'])
ws.row_dimensions[4].height = 230
header(ws, 6, ['ID', 'View', 'Office', 'Prompt'])
for i, v in enumerate(CAT['views']):
    r = 7 + i
    put(ws, r, 1, v['id'], bold=True); put(ws, r, 2, v['name'], bold=True, wrap=True); put(ws, r, 3, v['office'])
    put(ws, r, 4, v['prompt'], wrap=True)
    lines = sum(max(1, len(p) // 110 + 1) for p in v['prompt'].split('\n'))
    ws.row_dimensions[r].height = min(409, 13.5 * lines + 6)
widths(ws, [18, 24, 14, 120])
ws.column_dimensions['B'].width = 24

# ---------------------------------------------------------------- Result_JSON
ws = wb.create_sheet('Result_JSON')
ws.sheet_view.showGridLines = False
put(ws, 1, 1, 'Sample entries in the returned JSON shape (one of each kind)', bold=True, size=14, border=False)
put(ws, 2, 1, 'Full data: Result_Entries (one row per entry) and Result_Rows (one row per input row). meta = ' + json.dumps(RES['meta']), color=MUTED, border=False)
kinds = [('Exact', lambda e: e['oracleGroup'] == 'G0011'), ('WithinTolerance', lambda e: e['oracleGroup'] == 'G0127'), ('Break', lambda e: e['oracleGroup'] == 'G0144'),
         ('ManyToOne', lambda e: e['oracleGroup'] == 'G0159'), ('Package', lambda e: e['oracleGroup'] == 'G0171'), ('LifecycleCollapsed', lambda e: e['oracleGroup'] == 'G0179'),
         ('LeftOnly', lambda e: e['oracleGroup'] == 'G0184'), ('RightOnly', lambda e: 'CG-20542470' in e['right'])]
header(ws, 4, ['Kind', 'Entry JSON'])
for i, (k, pred) in enumerate(kinds):
    e = next(e for e in RES['entries'] if pred(e))
    txt = json.dumps(e, indent=1)
    put(ws, 5 + i, 1, k, bold=True)
    put(ws, 5 + i, 2, txt, wrap=True)
    ws.row_dimensions[5 + i].height = min(409, 12 * txt.count('\n') + 10)
widths(ws, [20, 110])

# ---------------------------------------------------------------- About
ws = wb.create_sheet('About')
ws.sheet_view.showGridLines = False
notes = [
    ('Source data', 'test-data/trade-reconciliation-dataset/claude-sonnet-5.5 in m-vaysman/recdiffy.net: 300 internal rows (Internal_Trades), 200 counterparty rows (Counterparty_Trades).'),
    ('How results were produced', 'An analysis script applying the revised RecDiffy specification: lifecycle collapse, two-leg packages, passes P1 shared UTI, P2 natural key with quantity, P3 group sum, P4 reduced key; tolerances accrued 1.25, FX forward rate 0.0001, fees 0.05; half-even rounding. Not output of the RecDiffy engine.'),
    ('Check against the oracle', 'All 275 entries agree with RecDiffy_Hidden_Test_Oracle.json on membership and outcome (145 exact, 18 within tolerance, 20 breaks, 85 unmatched ours, 7 unmatched theirs), and every break has exactly one unattributed root field equal to the oracle field.'),
    ('As-of date', '2025-06-13 (Index!B5). Ageing counts business days from trade date (Monday to Friday, no holiday calendar).'),
    ('Currencies', 'Amounts stay in each trade\'s currency. Nothing is added across currencies, and no FX rates are applied.'),
    ('Fees on the counterparty side', 'The counterparty file has no commission column, so their fee is derived from net cash, principal and accrued. It is not derived for netted two-leg packages, where net cash is a leg difference rather than principal plus accrued.'),
    ('Oracle group column', 'Each entry carries the oracle group it corresponds to (G0001 to G0275) so any sample can be traced back to the test data.'),
]
put(ws, 1, 1, 'About this workbook', bold=True, size=14, border=False)
for i, (k, v) in enumerate(notes):
    put(ws, 3 + i, 1, k, bold=True, wrap=True); put(ws, 3 + i, 2, v, wrap=True); ws.row_dimensions[3 + i].height = 44
widths(ws, [26, 120])

# order: Index, views, Prompts, data, About
order = ['Index'] + [v['sheet'] for v in CAT['views']] + ['Prompts', 'Result_Entries', 'Result_Rows', 'Result_JSON', 'About']
wb._sheets = [wb[n] for n in order]
wb.active = 0
for wsx in wb.worksheets:
    wsx.sheet_view.zoomScale = 100
    wsx.page_setup.orientation = 'landscape'
    wsx.sheet_properties.pageSetUpPr.fitToPage = True
    wsx.page_setup.fitToWidth = 1
    wsx.page_setup.fitToHeight = 0
wb.save(OUT)
print('saved', OUT)
