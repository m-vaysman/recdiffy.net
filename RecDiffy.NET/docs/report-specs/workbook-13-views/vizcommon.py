# Shared helpers for the xlsx and html builders.
import json, datetime, os
from decimal import Decimal as D, InvalidOperation

V = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'data') + os.sep
CAT = json.load(open(V + 'catalogue.json'))
VIEWS = json.load(open(V + 'views.json'))
RES = json.load(open(V + 'rec_result.json'))
ASOF = datetime.date(2025, 6, 13)
CATS = ['Price', 'Quantity', 'SettleDate', 'Fees', 'Unmatched ours', 'Unmatched theirs']
CCY_ORDER = ['USD', 'EUR', 'GBP', 'CHF', 'CAD', 'JPY']


def meta(view_id):
    return next(v for v in CAT['views'] if v['id'] == view_id)


def dec(s):
    try:
        return D(str(s))
    except (InvalidOperation, TypeError):
        return None


def num(s):
    """Decimal string -> int/float for display cells; None if not numeric."""
    d = dec(s)
    if d is None:
        return None
    return int(d) if d == d.to_integral_value() and abs(d) < 10**15 else float(d)


def date(s):
    try:
        return datetime.date.fromisoformat(str(s))
    except ValueError:
        return None


def steps_for(card):
    """Human-readable normalization steps for a BO3 card (raw -> canonical)."""
    s = card['streetRaw']
    t = card['theirs']
    out = []
    if s['Reporting_Counterparty_Name'] != t['Counterparty']:
        out.append(('party.resolveLei', f"{s['Reporting_Counterparty_Name']} / LEI {s['Counterparty_LEI']}", t['Counterparty']))
    if s['Security_ID_Source'] != 'ISIN' or s['Primary_Security_ID'] != t['Instrument']:
        out.append(('instrument.resolve', f"{s['Security_ID_Source']} {s['Primary_Security_ID']}", t['Instrument']))
    for raw_key, canon_key in (('Trade_Date', 'TradeDate'), ('Value_Settle_Date', 'SettleDate')):
        if s[raw_key] != t[canon_key]:
            out.append(('date.parse', f"{raw_key} '{s[raw_key]}'", t[canon_key]))
    for step in card['trace']:
        if step.startswith('dir code'):
            out.append(('side.resolve', f"Dir_Ind '{s['Dir_Ind']}'", step.split('->')[-1]))
        elif step.startswith('blank dir'):
            out.append(('side.resolve', f"blank Dir_Ind, quantity {s['Reported_Qty_Notional']}", step.split('-> ')[-1]))
        elif step == 'dealer flip':
            out.append(('side.resolve (dealer view)', s['Reporting_Perspective'], f"inverted to {t['Side']}"))
    if s['Price_Quote_Convention'] == '32NDS_UST':
        out.append(('price.from32nds', s['Exec_Price_Rate'], t['Price']))
    elif s['Price_Quote_Convention'] == 'DIRTY_PCT_PAR':
        out.append(('price.dirtyToClean', f"dirty {s['Exec_Price_Rate']} less accrued {s['Reported_Accrued_Int']}", t['Price']))
    q = dec(s['Reported_Qty_Notional'])
    if q is not None and q < 0:
        out.append(('quantity.absolute', s['Reported_Qty_Notional'], t['Quantity']))
    if t.get('Fees') is not None:
        out.append(('fees.inferFromNet', f"net {s['Net_Cash_Amount']} (no fee column)", t['Fees']))
    return out


def diff_state(d):
    if d is None:
        return 'same'
    if d['attributedTo']:
        return 'attributed'
    return 'break' if d['outcome'] == 'Break' else 'tolerance'


def rows_with_cpty():
    """Result rows with their counterparty, taken from the entry projections."""
    by_entry = {e['entryId']: e for e in RES['entries']}
    out = []
    for r in RES['rows']:
        e = by_entry[r['entryId']]
        p = (e['leftProjection'] if r['source'] == 'left' else e['rightProjection']) or e['leftProjection'] or e['rightProjection']
        out.append(dict(r, counterparty=p['Counterparty']))
    return out
