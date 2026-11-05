I can't execute code or attach files in this chat, so here is the complete, self-contained generator script. Running it locally produces both files. It is deterministic (seeded), and it asserts the row counts (300 / 200 / 120) and oracle integrity before saving.

**Run it:**
```bash
pip install openpyxl
python generate_recdiffy_dataset.py
```

```python
#!/usr/bin/env python3
"""
RecDiffy.NET - institutional trade reconciliation test dataset generator.
Outputs:
  RecDiffy_Trade_Reconciliation_Dataset.xlsx  (Internal_Trades / Counterparty_Trades / Known_Exact_Matches)
  RecDiffy_Hidden_Test_Oracle.json            (ground truth for ALL 300 internal / 200 counterparty rows)
All security identifiers, LEIs and IDs are synthetic (valid check digits, not real instruments).
"""
import copy, json, random, string, datetime as dt
from collections import Counter
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment
from openpyxl.utils import get_column_letter

SEED = 20250613
rng = random.Random(SEED)
D = dt.date
XLSX_OUT = "RecDiffy_Trade_Reconciliation_Dataset.xlsx"
JSON_OUT = "RecDiffy_Hidden_Test_Oracle.json"

TRADE_DATES = [D(2025, 6, 10), D(2025, 6, 11), D(2025, 6, 12)]
HOLIDAYS = {D(2025, 6, 19), D(2025, 7, 4)}
MON = ["JAN","FEB","MAR","APR","MAY","JUN","JUL","AUG","SEP","OCT","NOV","DEC"]
BUY_LIKE = {"BUY", "PAY_FIXED"}      # cash-out side for net settlement math
OUT_SIDES = {"SELL", "PAY_FIXED"}    # "negative" side for signed-quantity brokers

# ----------------------------------------------------------------------------- calendars
def is_bd(d): return d.weekday() < 5 and d not in HOLIDAYS
def add_bd(d, n):
    step = 1 if n >= 0 else -1
    while n != 0:
        d += dt.timedelta(days=step)
        if is_bd(d): n -= step
    return d
def roll_fwd(d):
    while not is_bd(d): d += dt.timedelta(days=1)
    return d
def iso(d): return d.isoformat() if d else None

# ----------------------------------------------------------------------------- identifier helpers
def rnd_alnum(n): return "".join(rng.choice(string.digits + string.ascii_uppercase) for _ in range(n))
def cusip_check(c8):
    s = 0
    for i, ch in enumerate(c8):
        v = int(ch) if ch.isdigit() else ord(ch.upper()) - 55
        if i % 2 == 1: v *= 2
        s += v // 10 + v % 10
    return str((10 - s % 10) % 10)
def make_cusip(prefix6=None):
    c8 = (prefix6 or rnd_alnum(6)) + rnd_alnum(2)
    return c8 + cusip_check(c8)
def isin_check(body):
    digits = "".join(str(int(c, 36)) for c in body)
    s = 0
    for i, ch in enumerate(reversed(digits)):
        n = int(ch)
        if i % 2 == 0: n *= 2
        s += n // 10 + n % 10
    return str((10 - s % 10) % 10)
def make_isin(country, nsin9):
    body = country + nsin9
    return body + isin_check(body)
SEDOL_CH = "0123456789BCDFGHJKLMNPQRSTVWXYZ"
def make_sedol():
    base = "".join(rng.choice(SEDOL_CH) for _ in range(6))
    w = [1, 3, 1, 7, 3, 9]
    s = sum(w[i] * int(c, 36) for i, c in enumerate(base))
    return base + str((10 - s % 10) % 10)
def make_lei():
    base = rnd_alnum(18)
    s = "".join(str(int(c, 36)) for c in base + "00")
    return base + f"{98 - int(s) % 97:02d}"

# ----------------------------------------------------------------------------- counterparties
CPTY = {
 "GSCO":    dict(legal="Goldman Sachs & Co. LLC", aliases=["GS_NY_PRIME","Goldman Sachs International","GOLDMAN SACHS & CO LLC"],
                 style="BSPR",         pref=["CUSIP","ISIN","TICKER"],          datefmt="%Y-%m-%d", prefix="GS"),
 "JPM":     dict(legal="J.P. Morgan Securities LLC", aliases=["JPMS_CLEAR","J.P. MORGAN SECURITIES LLC","JPMorgan Chase Bank NA"],
                 style="BLANK_SIGNED", pref=["ISIN","CUSIP","TICKER"],          datefmt="%m/%d/%Y", prefix="JP"),
 "MSCO":    dict(legal="Morgan Stanley & Co. LLC", aliases=["MS_PB_NY","Morgan Stanley & Co International plc","MORGAN STANLEY & CO LLC"],
                 style="BOTH",         pref=["SEDOL","ISIN","TICKER"],          datefmt="%Y%m%d",   prefix="MS"),
 "BAML":    dict(legal="BofA Securities, Inc.", aliases=["BOFA_MLPF&S","Merrill Lynch Pierce Fenner & Smith","BofA Securities Inc"],
                 style="BOTSLD",       pref=["CUSIP","ISIN","TICKER"],          datefmt="DMONY",    prefix="BA"),
 "CITI":    dict(legal="Citigroup Global Markets Inc.", aliases=["CGMI_NY","CITIGROUP GLOBAL MKTS INC","Citibank NA London"],
                 style="LOWER",        pref=["ISIN","SEDOL","TICKER"],          datefmt="%Y-%m-%d", prefix="CG"),
 "BARC":    dict(legal="Barclays Capital Inc.", aliases=["BARCAP_LDN","Barclays Bank PLC","BARCLAYS CAPITAL INC"],
                 style="BSPR",         pref=["SEDOL","ISIN","TICKER"],          datefmt="%d/%m/%Y", prefix="BC"),
 "UBS":     dict(legal="UBS Securities LLC", aliases=["UBS_SEC_STFD","UBS AG London Branch","UBS SECURITIES LLC"],
                 style="BOTH",         pref=["ISIN","CUSIP","TICKER"],          datefmt="%Y-%m-%d", prefix="UB"),
 "CITADEL": dict(legal="Citadel Securities LLC", aliases=["CITSEC_MM","Citadel Securities LLC","CITADEL SECURITIES (PRINCIPAL)"],
                 style="BLANK_SIGNED", pref=["TICKER"],                         datefmt="%Y%m%d",   prefix="CS"),
}
for _c in CPTY.values(): _c["lei"] = make_lei()
CPTY_CODES = list(CPTY)
OTC_CPTY = [c for c in CPTY_CODES if c != "CITADEL"]

# ----------------------------------------------------------------------------- security universe
EQ_RAW = [("AAPL","Apple Inc",195),("MSFT","Microsoft Corp",425),("NVDA","NVIDIA Corp",135),("AMZN","Amazon.com Inc",205),
 ("GOOGL","Alphabet Inc Cl A",175),("META","Meta Platforms Inc",640),("TSLA","Tesla Inc",330),("JPM","JPMorgan Chase & Co",265),
 ("XOM","Exxon Mobil Corp",108),("JNJ","Johnson & Johnson",152),("PG","Procter & Gamble Co",165),("V","Visa Inc Cl A",340),
 ("HD","Home Depot Inc",370),("CVX","Chevron Corp",142),("KO","Coca-Cola Co",70),("PEP","PepsiCo Inc",132),
 ("MRK","Merck & Co Inc",80),("ABBV","AbbVie Inc",190),("COST","Costco Wholesale Corp",1000),("WMT","Walmart Inc",97),
 ("BAC","Bank of America Corp",44),("DIS","Walt Disney Co",112),("CSCO","Cisco Systems Inc",65),("INTC","Intel Corp",21),
 ("AMD","Advanced Micro Devices",118),("NFLX","Netflix Inc",1200),("CRM","Salesforce Inc",265),("ORCL","Oracle Corp",170),
 ("CAT","Caterpillar Inc",390),("BA","Boeing Co",205),("GE","GE Aerospace",245),("UNH","UnitedHealth Group",300),
 ("LLY","Eli Lilly & Co",770),("MCD","McDonald's Corp",300),("NKE","Nike Inc Cl B",62),("T","AT&T Inc",27),
 ("VZ","Verizon Communications",43),("PFE","Pfizer Inc",25),("SBUX","Starbucks Corp",90)]
EQUITIES = []
for tk, nm, px in EQ_RAW:
    cu = make_cusip()
    EQUITIES.append(dict(kind="EQ", ticker=tk, desc=f"{nm.upper()} COM USD", cusip=cu, isin=make_isin("US", cu),
                         sedol=make_sedol(), px=px))

BONDS, _seen = [], set()
while sum(b["kind"] == "UST" for b in BONDS) < 24:
    cpn = rng.choice([2.75,3.0,3.5,3.875,4.0,4.125,4.25,4.375,4.5,4.625,4.75,5.0])
    mat = D(rng.randint(2026, 2055), rng.choice([2,5,8,11]), 15)
    tk = f"T {cpn:g} {mat:%m/%d/%y}"
    if tk in _seen: continue
    _seen.add(tk)
    cu = make_cusip("91282C")
    BONDS.append(dict(kind="UST", ticker=tk, desc=f"US TREASURY N/B {cpn:.3f}% {mat:%m/%d/%Y}", cusip=cu,
                      isin=make_isin("US", cu), sedol=None, coupon=cpn, maturity=mat))
ISSUERS = [("Apple Inc","AAPL"),("Microsoft Corp","MSFT"),("Ford Motor Credit Co","F"),("Verizon Communications","VZ"),
 ("AT&T Inc","T"),("Boeing Co","BA"),("Bank of America Corp","BAC"),("JPMorgan Chase & Co","JPM"),("Comcast Corp","CMCSA"),
 ("Oracle Corp","ORCL"),("Pfizer Inc","PFE"),("Exxon Mobil Corp","XOM"),("Goldman Sachs Group","GS"),("Citigroup Inc","C"),
 ("Walmart Inc","WMT"),("Amazon.com Inc","AMZN"),("Intel Corp","INTC"),("Charter Comm Operating","CHTR"),
 ("Occidental Petroleum","OXY"),("General Motors Financial","GM")]
while sum(b["kind"] == "CORP" for b in BONDS) < 40:
    nm, tk0 = rng.choice(ISSUERS)
    cpn = rng.randrange(20, 61) * 0.125
    mat = D(rng.randint(2027, 2052), rng.randint(1, 12), rng.randint(1, 28))
    tk = f"{tk0} {cpn:g} {mat:%m/%d/%y}"
    if tk in _seen: continue
    _seen.add(tk)
    cu = make_cusip()
    BONDS.append(dict(kind="CORP", ticker=tk, desc=f"{nm.upper()} {cpn:.3f}% DUE {mat:%m/%d/%Y}", cusip=cu,
                      isin=make_isin("US", cu), sedol=None, coupon=cpn, maturity=mat))

FX_RAW = [("EURUSD","EUR","USD",1.0850,5),("GBPUSD","GBP","USD",1.2700,5),("AUDUSD","AUD","USD",0.6500,5),
          ("NZDUSD","NZD","USD",0.6050,5),("USDJPY","USD","JPY",143.50,3),("USDCAD","USD","CAD",1.3650,5),
          ("USDCHF","USD","CHF",0.8200,5),("EURGBP","EUR","GBP",0.8540,5)]
FX = [dict(kind="FX", ticker=p, desc=f"FX FORWARD {b}/{q}", ccy=q, spot=s, dec=d, isin=None, cusip=None, sedol=None)
      for p, b, q, s, d in FX_RAW]
FX_SMALL = [f for f in FX if f["ticker"] in ("EURUSD", "GBPUSD", "AUDUSD")]
SWAP_CCY = [("USD","SOFR",3.8,4.4),("EUR","ESTR",2.2,2.8),("GBP","SONIA",3.8,4.3)]
DEFAULT_BOOKS = {"Bond":["MACRO_RATES","CREDIT_OPP"], "Equity":["STAT_ARB_EQ","LS_EQUITY_CORE"],
                 "Forward":["GLOBAL_MACRO_FX","MACRO_RATES"], "Swap":["MACRO_RATES","RATES_RV"]}

# ----------------------------------------------------------------------------- id factories
USED_INT, USED_RPT, USED_KEYS = set(), set(), set()
def new_internal_id():
    while True:
        i = f"OMS{rng.randint(1_000_000, 9_999_999)}"
        if i not in USED_INT: USED_INT.add(i); return i
def new_report_id(cp):
    while True:
        i = f"{cp['prefix']}-{rng.randint(10_000_000, 99_999_999)}"
        if i not in USED_RPT: USED_RPT.add(i); return i
def new_uti(code): return CPTY[code]["lei"] + "".join(rng.choice("0123456789ABCDEF") for _ in range(10))
def new_ext_ref(cp): return f"{cp['prefix']}X{rng.randint(10**9, 10**10 - 1)}"

# ----------------------------------------------------------------------------- economics
def accrued_for(t, q):
    return round(q * t["_coupon"] / 100 * t["_days"] / 360, 2) if t["Asset_Class"] == "Bond" else 0.0
def fee_for(t, q): return round(q * t["_fpu"], 2)
def principal_for(t, q, p):
    a = t["Asset_Class"]
    if a == "Bond": return round(q * p / 100, 2)
    if a in ("Equity", "Forward"): return round(q * p, 2)
    return round(q * t["_upf"], 2)                      # Swap: upfront
def calc_net(side, principal, accrued, fees):
    return round(principal + accrued + fees, 2) if side in BUY_LIKE else round(principal + accrued - fees, 2)
def economics(t, qty=None, price=None, accrued=None, fees=None):
    q = t["Quantity_Notional"] if qty is None else qty
    p = t["Clean_Price_Rate"] if price is None else price
    acc = accrued_for(t, q) if accrued is None else accrued
    fee = fee_for(t, q) if fees is None else fees
    pr = principal_for(t, q, p)
    return q, p, acc, fee, pr, calc_net(t["Side"], pr, acc, fee)
def fill_amounts(t):
    q, p, acc, fee, pr, net = economics(t)
    t.update(Principal_Gross=pr, Accrued_Interest=acc, Commission_Fees=fee, Net_Settlement_Amount=net)

# ----------------------------------------------------------------------------- internal trade factory
def _make_trade(asset, cpty=None, sec=None, side=None, qty=None, td=None, book=None, bond_kind=None):
    cpty = cpty or rng.choice(OTC_CPTY if asset in ("Forward", "Swap") else CPTY_CODES)
    td = td or rng.choice(TRADE_DATES)
    days, upf, coupon = 0, 0.0, None
    if asset == "Bond":
        pool = BONDS if bond_kind is None else [b for b in BONDS if b["kind"] == bond_kind]
        sec = sec or rng.choice(pool)
        side = side or rng.choice(["BUY", "SELL"])
        qty = qty or rng.randrange(250_000, 10_000_001, 250_000)
        price = rng.randint(95 * 64, 104 * 64) / 64 if sec["kind"] == "UST" else round(rng.uniform(88, 108), 3)
        settle, ccy, coupon, mat = add_bd(td, 1), "USD", sec["coupon"], sec["maturity"]
        days = rng.randint(3, 175)
        fpu = rng.choice([0.05, 0.1, 0.15, 0.2, 0.25]) / 1000
    elif asset == "Equity":
        sec = sec or rng.choice(EQUITIES)
        side = side or rng.choice(["BUY", "SELL"])
        qty = qty or rng.randrange(5, 501) * 100
        price = round(sec["px"] * rng.uniform(0.97, 1.03), 2)
        settle, ccy, mat = add_bd(td, 1), "USD", None
        fpu = rng.choice([0.002, 0.003, 0.005])
    elif asset == "Forward":
        sec = sec or rng.choice(FX)
        side = side or rng.choice(["BUY", "SELL"])
        qty = qty or rng.randrange(1_000_000, 25_000_001, 500_000)
        price = round(sec["spot"] * (1 + rng.uniform(-0.004, 0.004)), sec["dec"])
        settle = roll_fwd(add_bd(td, 2) + dt.timedelta(days=rng.choice([7, 14, 30, 60, 90, 180])))
        ccy, mat, fpu = sec["ccy"], settle, 0.0
    else:  # Swap
        ccy, idx, lo, hi = rng.choice(SWAP_CCY)
        tenor = rng.choice([2, 3, 5, 7, 10, 30])
        sec = dict(kind="IRS", ticker=f"{ccy}_{idx}_{tenor}Y", desc=f"{ccy} IRS {tenor}Y FIXED VS {idx}",
                   isin=None, cusip=None, sedol=None)
        side = side or rng.choice(["PAY_FIXED", "REC_FIXED"])
        qty = qty or rng.randrange(5, 101) * 1_000_000
        price = round(rng.uniform(lo, hi), 4)
        coupon = price
        settle = add_bd(td, 2)
        mat = roll_fwd(D(settle.year + tenor, settle.month, min(settle.day, 28)))
        upf = round(rng.uniform(0.0002, 0.004), 7)
        fpu = rng.choice([5, 7.5, 10]) / 1e6
    t = {"Internal_Trade_ID": None, "UTI_Exec_ID": new_uti(cpty), "Parent_Link_ID": None, "Leg_Structure": "SINGLE",
         "Portfolio_Book": book or rng.choice(DEFAULT_BOOKS[asset]), "Trade_Date": td, "Settle_Date": settle,
         "Asset_Class": asset, "Counterparty_Code": cpty, "Counterparty_Legal_Name": CPTY[cpty]["legal"], "Side": side,
         "Ticker": sec["ticker"], "ISIN": sec.get("isin"), "CUSIP": sec.get("cusip"), "SEDOL": sec.get("sedol"),
         "Instrument_Description": sec["desc"], "Quantity_Notional": qty, "Clean_Price_Rate": price, "Currency": ccy,
         "Principal_Gross": None, "Accrued_Interest": None, "Commission_Fees": None, "Net_Settlement_Amount": None,
         "Coupon_Rate": coupon, "Maturity_Date": mat,
         "_sec": sec, "_coupon": coupon or 0.0, "_days": days, "_fpu": fpu, "_upf": upf}
    fill_amounts(t)
    return t

def new_trade(asset, **kw):
    for _ in range(1000):
        t = _make_trade(asset, **kw)
        key = (t["Counterparty_Code"], t["Ticker"], t["Trade_Date"], t["Side"], t["Quantity_Notional"])
        if key not in USED_KEYS:
            USED_KEYS.add(key)
            t["Internal_Trade_ID"] = new_internal_id()
            return t
    raise RuntimeError("could not generate unique trade")

# ----------------------------------------------------------------------------- counterparty row factory
def fmt_date(d, fmt):
    if d is None: return None
    return f"{d.day:02d}-{MON[d.month-1]}-{d.year}" if fmt == "DMONY" else d.strftime(fmt)
def to_32nds(p):
    t64 = round(p * 64)
    assert abs(t64 / 64 - p) < 1e-12, "price not on a 1/64 grid"
    whole, rem = divmod(t64, 64)
    return f"{whole}-{rem // 2:02d}{'+' if rem % 2 else ''}"
def from_32nds(s):
    whole, frac = s.split("-")
    return int(whole) + (int(frac.rstrip("+")) + (0.5 if frac.endswith("+") else 0.0)) / 32
def encode_dir(style, side, qty, invert=False):
    out = side in OUT_SIDES
    if invert: out = not out
    swap = side in ("PAY_FIXED", "REC_FIXED")
    if style in ("BSPR", "BOTH"):   code = ("P" if out else "R") if swap else ("S" if out else "B")
    elif style == "BOTSLD":         code = ("PAY" if out else "REC") if swap else ("SLD" if out else "BOT")
    elif style == "LOWER":          code = ("pay" if out else "rec") if swap else ("sell" if out else "buy")
    else:                           code = None
    q = -qty if (style in ("BLANK_SIGNED", "BOTH") and out) else qty
    return code, q
def pick_security_id(cp, t):
    ids = {"ISIN": t["ISIN"], "CUSIP": t["CUSIP"], "SEDOL": t["SEDOL"], "TICKER": t["Ticker"]}
    for src in cp["pref"]:
        if ids.get(src): return ids[src], src
    return t["Ticker"], "TICKER"
def noisy_text(t):
    d, v = t["Instrument_Description"], rng.randrange(4)
    if v == 0: return d.upper()[:32]
    if v == 1: return d.title()
    if v == 2: return f"{t['Ticker']} {'BOND' if t['Asset_Class']=='Bond' else t['Asset_Class'].upper()}"
    return d.replace(" Inc", "").replace(" Corp", "")
PRODUCT = {"Equity": "CASH_EQ", "Forward": "FX_FWD", "Swap": "IRS"}
CONV = {"Bond": "CLEAN_PCT_PAR", "Equity": "PER_SHARE", "Forward": "FX_OUTRIGHT_RATE", "Swap": "FIXED_RATE_PCT"}

def build_cpty(t, *, shared=True, action="NEW", cancels_prior=None, qty=None, price=None, accrued=None, fees=None,
               settle=None, dirty=False, ust32=False, invert=False, report_id=None, exec_ref=None, net_override=None):
    cp, asset = CPTY[t["Counterparty_Code"]], t["Asset_Class"]
    q, p, acc, fee, pr, net = economics(t, qty, price, accrued, fees)
    if net_override is not None: net = net_override
    fmt = cp["datefmt"]
    if ust32:   pf, conv = to_32nds(p), "32NDS_UST"
    elif dirty: pf, conv = round(p + acc / q * 100, 6), "DIRTY_PCT_PAR"
    else:       pf, conv = p, CONV[asset]
    dir_ind, sq = encode_dir(cp["style"], t["Side"], q, invert)
    sid, ssrc = pick_security_id(cp, t)
    ref = exec_ref or (t["UTI_Exec_ID"] if shared else new_ext_ref(cp))
    alias = rng.choice(cp["aliases"])
    product = PRODUCT.get(asset) or ("GOVT_BOND" if t["_sec"]["kind"] == "UST" else "CORP_BOND")
    rid = report_id or new_report_id(cp)
    row = {"Cpty_Report_ID": rid, "Shared_Market_Exec_Ref": ref, "Trans_Lifecycle_Action": action,
           "Cancels_Prior_Report_ID": cancels_prior, "Reporting_Counterparty_Name": alias,
           "Counterparty_LEI": cp["lei"],
           "Reporting_Perspective": "DEALER_BILATERAL_VIEW" if invert else "PB_CLIENT_VIEW",
           "Trade_Date": fmt_date(t["Trade_Date"], fmt), "Value_Settle_Date": fmt_date(settle or t["Settle_Date"], fmt),
           "Product_Type": product, "Dir_Ind": dir_ind, "Primary_Security_ID": sid, "Security_ID_Source": ssrc,
           "Security_Text": noisy_text(t), "Reported_Qty_Notional": sq, "Exec_Price_Rate": pf,
           "Price_Quote_Convention": conv, "Trade_Ccy": t["Currency"],
           "Reported_Accrued_Int": acc if asset in ("Bond", "Swap") else None, "Net_Cash_Amount": net,
           "Coupon": t["Coupon_Rate"], "Maturity": fmt_date(t["Maturity_Date"], fmt)}
    meta = {"canonical_counterparty": t["Counterparty_Code"], "reported_name_alias": alias, "dir_style": cp["style"],
            "dir_ind": dir_ind, "signed_qty": sq, "abs_qty": q, "price_decimal": p, "reported_price_field": pf,
            "price_convention": conv, "accrued": acc, "fees": fee, "net_cash": net, "security_id_source": ssrc,
            "security_id": sid, "perspective": row["Reporting_Perspective"], "direction_inverted": invert,
            "shares_exec_id": bool(t["UTI_Exec_ID"]) and ref == t["UTI_Exec_ID"], "date_format": fmt}
    return row, meta

# ----------------------------------------------------------------------------- collectors
INTERNAL, CPTY_ROWS, GROUPS, KNOWN = [], [], [], []
def add_group(scenario, outcome, internal_ids, cpty_ids, **details):
    GROUPS.append({"group_id": f"G{len(GROUPS)+1:04d}", "scenario": scenario, "expected_outcome": outcome,
                   "internal_trade_ids": internal_ids, "cpty_report_ids": cpty_ids, "details": details})
def price_dec(t): return {"Bond": 3, "Equity": 2}.get(t["Asset_Class"], t["_sec"].get("dec", 4))

# =============================================================================
# A. 120 KNOWN EXACT MATCHES
# =============================================================================
plan = ([("Bond","UST32")]*12 + [("Bond","DIRTY")]*8 + [("Bond","PLAIN")]*25 +
        [("Equity","PLAIN")]*35 + [("Forward","PLAIN")]*22 + [("Swap","PLAIN")]*18)
rng.shuffle(plan)
otc_idx = [i for i, (a, _) in enumerate(plan) if a in ("Forward", "Swap")]
invert_set = set(rng.sample(otc_idx, 15))
shared_set = set(rng.sample(range(120), 60))
for i, (asset, kind) in enumerate(plan):
    kw = {"bond_kind": "UST"} if kind == "UST32" else {"bond_kind": "CORP"} if kind == "DIRTY" else {}
    t = new_trade(asset, **kw)
    row, meta = build_cpty(t, shared=(i in shared_set), ust32=(kind == "UST32"), dirty=(kind == "DIRTY"),
                           invert=(i in invert_set))
    # self-checks: exact economic identity after normalization
    assert meta["net_cash"] == t["Net_Settlement_Amount"] and meta["abs_qty"] == t["Quantity_Notional"]
    assert meta["accrued"] == t["Accrued_Interest"] and meta["fees"] == t["Commission_Fees"]
    if kind == "UST32": assert from_32nds(row["Exec_Price_Rate"]) == t["Clean_Price_Rate"]
    if kind == "DIRTY": assert abs(row["Exec_Price_Rate"] - t["Accrued_Interest"] / t["Quantity_Notional"] * 100
                                   - t["Clean_Price_Rate"]) < 1e-6
    tags = ["NAME_ALIAS", f"DIR_STYLE_{meta['dir_style']}", f"SEC_ID_{meta['security_id_source']}",
            "SHARED_EXEC_REF" if meta["shares_exec_id"] else "NO_SHARED_EXEC_REF"]
    if kind == "UST32": tags.append("UST_32NDS_PRICE")
    if kind == "DIRTY": tags.append("DIRTY_PRICE")
    if i in invert_set: tags.append("DEALER_PERSPECTIVE_INVERSION")
    INTERNAL.append(t); CPTY_ROWS.append(row); KNOWN.append(dict(t=t, row=row, meta=meta, tags=tags, kind=kind))
    add_group("KNOWN_EXACT", "EXACT_MATCH_AFTER_NORMALIZATION", [t["Internal_Trade_ID"]], [row["Cpty_Report_ID"]],
              asset_class=asset, tags=tags, cpty=meta)

# =============================================================================
# B. 18 WITHIN-TOLERANCE NEAR MATCHES
# =============================================================================
near_types = ["BOND_ACCRUED"]*6 + ["FX_RATE"]*6 + ["FEE_ROUNDING"]*6
near_shared = set(rng.sample(range(18), 9))
for i, typ in enumerate(near_types):
    sh = i in near_shared
    if typ == "BOND_ACCRUED":
        t = new_trade("Bond", bond_kind="CORP")
        cv = round(t["Accrued_Interest"] + rng.choice([-1, 1]) * round(rng.uniform(0.40, 1.20), 2), 2)
        row, meta = build_cpty(t, shared=sh, accrued=cv); field, iv = "Accrued_Interest", t["Accrued_Interest"]
    elif typ == "FX_RATE":
        t = new_trade("Forward", sec=rng.choice(FX_SMALL), qty=rng.randrange(250_000, 1_000_001, 50_000))
        cv = round(t["Clean_Price_Rate"] + rng.choice([-1, 1]) * 0.0001, 5)
        row, meta = build_cpty(t, shared=sh, price=cv); field, iv = "Clean_Price_Rate", t["Clean_Price_Rate"]
    else:
        t = new_trade("Equity")
        cv = round(t["Commission_Fees"] + rng.choice([-1, 1]) * rng.choice([0.01, 0.02, 0.03]), 2)
        row, meta = build_cpty(t, shared=sh, fees=cv); field, iv = "Commission_Fees", t["Commission_Fees"]
    INTERNAL.append(t); CPTY_ROWS.append(row)
    add_group("NEAR_MATCH_WITHIN_TOLERANCE", "MATCH_WITHIN_TOLERANCE", [t["Internal_Trade_ID"]], [row["Cpty_Report_ID"]],
              asset_class=t["Asset_Class"], variation=typ, field=field, internal_value=iv, cpty_value=cv,
              delta=round(cv - iv, 6), net_cash_delta=round(meta["net_cash"] - t["Net_Settlement_Amount"], 2),
              shares_exec_id=meta["shares_exec_id"], cpty=meta)

# =============================================================================
# C. 20 TRUE ECONOMIC BREAKS
# =============================================================================
break_plan = ([("PRICE", a) for a in ("Bond","Bond","Equity","Equity","Forward")] +
              [("QTY", a) for a in ("Bond","Bond","Equity","Equity","Forward")] +
              [("SETTLE", a) for a in ("Bond","Bond","Equity","Equity","Swap")] +
              [("FEE", a) for a in ("Bond","Bond","Equity","Equity","Swap")])
brk_shared = set(rng.sample(range(20), 10))
for i, (typ, asset) in enumerate(break_plan):
    sh = i in brk_shared
    kw = {}
    if typ == "QTY":
        kw["qty"] = {"Bond": rng.randrange(1, 11) * 1_000_000, "Equity": rng.randrange(10, 200) * 500,
                     "Forward": rng.randrange(1, 6) * 5_000_000}[asset]
    t = new_trade(asset, **kw)
    if typ == "PRICE":
        p = t["Clean_Price_Rate"]
        mag = rng.uniform(0.004, 0.01) if asset == "Forward" else rng.uniform(0.015, 0.05)
        cv = round(p * (1 + rng.choice([-1, 1]) * mag), price_dec(t)); iv, field = p, "Clean_Price_Rate"
        row, meta = build_cpty(t, shared=sh, price=cv)
    elif typ == "QTY":
        iv, field, cv = t["Quantity_Notional"], "Quantity_Notional", int(round(t["Quantity_Notional"] * 0.8))
        row, meta = build_cpty(t, shared=sh, qty=cv)
    elif typ == "SETTLE":
        iv, field = t["Settle_Date"], "Settle_Date"
        cv = add_bd(t["Settle_Date"], -1 if asset == "Swap" else 1)
        row, meta = build_cpty(t, shared=sh, settle=cv); iv, cv = iso(iv), iso(cv)
    else:
        iv, field, cv = t["Commission_Fees"], "Commission_Fees", round(t["Commission_Fees"] + 450.00, 2)
        row, meta = build_cpty(t, shared=sh, fees=cv)
    INTERNAL.append(t); CPTY_ROWS.append(row)
    add_group("TRUE_ECONOMIC_BREAK", "BREAK_1_TO_1", [t["Internal_Trade_ID"]], [row["Cpty_Report_ID"]],
              asset_class=asset, break_type=typ, field=field, internal_value=iv, cpty_value=cv,
              net_cash_delta=round(meta["net_cash"] - t["Net_Settlement_Amount"], 2),
              shares_exec_id=meta["shares_exec_id"], cpty=meta)

# =============================================================================
# D. 12 N-TO-1 ALLOCATION SPLITS (36 internal -> 12 cpty)
# =============================================================================
BOOKS3 = ["MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP"]
SPLITS = [(0.5,0.3,0.2),(0.4,0.35,0.25),(0.6,0.25,0.15),(0.45,0.35,0.2),(0.5,0.25,0.25)]
alloc_shared = set(rng.sample(range(12), 6))
for g in range(12):
    asset = "Equity" if g < 6 else "Bond"
    Q, lot = (rng.randrange(6, 60) * 1000, 100) if asset == "Equity" else (rng.randrange(3, 31) * 1_000_000, 100_000)
    base = new_trade(asset, qty=Q, bond_kind="CORP")
    f = list(rng.choice(SPLITS)); rng.shuffle(f)
    a = int(round(Q * f[0] / lot)) * lot; b = int(round(Q * f[1] / lot)) * lot; c = Q - a - b
    assert c > 0
    books = BOOKS3[:]; rng.shuffle(books)
    sh = g in alloc_shared
    ids, sum_acc, sum_fee, sum_net, allocs = [], 0.0, 0.0, 0.0, []
    for qty_i, bk in zip([a, b, c], books):
        x = copy.deepcopy(base)
        x.update(Quantity_Notional=qty_i, Portfolio_Book=bk, Internal_Trade_ID=new_internal_id(),
                 UTI_Exec_ID=base["UTI_Exec_ID"] if sh else None)
        fill_amounts(x)
        sum_acc += x["Accrued_Interest"]; sum_fee += x["Commission_Fees"]; sum_net += x["Net_Settlement_Amount"]
        INTERNAL.append(x); ids.append(x["Internal_Trade_ID"]); allocs.append({"book": bk, "qty": qty_i})
    row, meta = build_cpty(base, shared=sh, qty=Q, accrued=round(sum_acc, 2), fees=round(sum_fee, 2),
                           net_override=round(sum_net, 2))
    CPTY_ROWS.append(row)
    add_group("N_TO_1_ALLOCATION", "MANY_TO_ONE_MATCH", ids, [row["Cpty_Report_ID"]], asset_class=asset,
              block_qty=Q, allocations=allocs, sum_alloc_qty=a + b + c, sum_alloc_net=round(sum_net, 2),
              block_net=row["Net_Cash_Amount"], shares_exec_id=sh, cpty=meta)

# =============================================================================
# E. 8 MULTI-LEG TRADES (16 internal -> 8 cpty combined-net rows)
# =============================================================================
for k in range(8):
    sh = (k % 2 == 0)
    if k < 4:   # IRS: pay fixed leg vs receive floating leg; cpty reports net accrual
        base = new_trade("Swap", side="PAY_FIXED"); base["_upf"] = 0.0
        N, r1 = base["Quantity_Notional"], base["Clean_Price_Rate"]
        r2 = round(r1 - rng.uniform(0.15, 0.60), 4); d = rng.choice([90, 91, 92, 181, 182])
        A1, A2 = round(N * r1 / 100 * d / 360, 2), round(N * r2 / 100 * d / 360, 2)
        legs = [("LEG_1_PAY", "PAY_FIXED", r1, 0.0, A1, A1), ("LEG_2_RECEIVE", "REC_FIXED", r2, 0.0, A2, A2)]
        net = round(A1 - A2, 2)
        row, meta = build_cpty(base, shared=sh, qty=N, price=r1, accrued=net, fees=0.0, net_override=net)
        kind = "IRS_FIXED_VS_FLOAT"
    else:       # FX: pay leg (buy base at F1) vs receive leg (sell base at F2); cpty reports net cash
        base = new_trade("Forward", side="BUY", qty=rng.randrange(1, 11) * 1_000_000)
        N, F1 = base["Quantity_Notional"], base["Clean_Price_Rate"]
        F2 = round(F1 * (1 - rng.uniform(0.0005, 0.004)), base["_sec"]["dec"])
        P1, P2 = round(N * F1, 2), round(N * F2, 2)
        legs = [("LEG_1_PAY", "BUY", F1, P1, 0.0, P1), ("LEG_2_RECEIVE", "SELL", F2, P2, 0.0, P2)]
        net = round(P1 - P2, 2)
        row, meta = build_cpty(base, shared=sh, qty=N, price=F1, accrued=0.0, fees=0.0, net_override=net)
        kind = "FX_PAY_VS_RECEIVE"
    plink = f"PLK-{rng.randint(100000, 999999)}"
    ids = []
    for leg, side, rate, prin, acc, lnet in legs:
        x = copy.deepcopy(base)
        x.update(Internal_Trade_ID=new_internal_id(), Leg_Structure=leg, Parent_Link_ID=plink, Side=side,
                 Clean_Price_Rate=rate, Principal_Gross=prin, Accrued_Interest=acc, Commission_Fees=0.0,
                 Net_Settlement_Amount=lnet)
        if kind == "IRS_FIXED_VS_FLOAT": x["Coupon_Rate"] = rate
        INTERNAL.append(x); ids.append(x["Internal_Trade_ID"])
    CPTY_ROWS.append(row)
    add_group("MULTI_LEG_NET", "TWO_TO_ONE_NET_MATCH", ids, [row["Cpty_Report_ID"]], leg_kind=kind,
              parent_link_id=plink, leg_net_amounts=[l[5] for l in legs], combined_net_cash=net,
              cpty_direction_basis="direction of LEG_1_PAY", shares_exec_id=meta["shares_exec_id"], cpty=meta)

# =============================================================================
# F. 5 CANCEL & CORRECT CHAINS (5 internal -> 15 cpty)
# =============================================================================
cc_plan = [("Bond","PRICE"),("Bond","QTY"),("Equity","PRICE"),("Equity","QTY"),("Forward","PRICE")]
for i, (asset, bust) in enumerate(cc_plan):
    t = new_trade(asset, bond_kind="CORP")
    cp = CPTY[t["Counterparty_Code"]]
    sh = (i % 2 == 0)
    ref = t["UTI_Exec_ID"] if sh else new_ext_ref(cp)
    if bust == "PRICE":
        bad = {"price": round(t["Clean_Price_Rate"] * (1 + rng.choice([-1, 1]) * rng.uniform(0.01, 0.03)), price_dec(t))}
    else:
        bad = {"qty": int(round(t["Quantity_Notional"] * 1.25 / 100) * 100)}
    r1, r2, r3 = new_report_id(cp), new_report_id(cp), new_report_id(cp)
    new_row, _ = build_cpty(t, action="NEW", report_id=r1, exec_ref=ref, **bad)
    canc_row, _ = build_cpty(t, action="CANC", cancels_prior=r1, report_id=r2, exec_ref=ref, **bad)
    corr_row, cm = build_cpty(t, action="CORR", cancels_prior=r2, report_id=r3, exec_ref=ref)
    INTERNAL.append(t); CPTY_ROWS.extend([new_row, canc_row, corr_row])
    add_group("CANCEL_CORRECT_CHAIN", "CHAIN_COLLAPSES_TO_EXACT_MATCH", [t["Internal_Trade_ID"]], [r1, r2, r3],
              asset_class=asset, bust_type=bust, chain={"NEW_busted": r1, "CANC_reverses": r2, "CORR_rebooked": r3},
              reference_links={"CANC.Cancels_Prior_Report_ID": r1, "CORR.Cancels_Prior_Report_ID": r2},
              surviving_cpty_report_id=r3, busted_values=bad, shares_exec_id=sh, cpty=cm)

# =============================================================================
# G. 85 INTERNAL ORPHANS
# =============================================================================
for asset in ["Bond"]*30 + ["Equity"]*28 + ["Forward"]*14 + ["Swap"]*13:
    t = new_trade(asset)
    if rng.random() < 0.3: t["UTI_Exec_ID"] = None
    INTERNAL.append(t)
    add_group("INTERNAL_ORPHAN", "UNMATCHED_INTERNAL", [t["Internal_Trade_ID"]], [], asset_class=asset,
              counterparty=t["Counterparty_Code"], reason=rng.choice(["UNCONFIRMED", "DK_BY_CPTY", "MISSING_FROM_STREET_FEED"]))

# =============================================================================
# H. 7 COUNTERPARTY ALLEGED TRADES
# =============================================================================
for asset in ["Equity"]*3 + ["Bond"]*2 + ["Forward"] + ["Swap"]:
    ghost = new_trade(asset)                                  # never added to Tab 1
    row, meta = build_cpty(ghost, shared=False)
    CPTY_ROWS.append(row)
    add_group("CPTY_ALLEGED", "UNMATCHED_COUNTERPARTY", [], [row["Cpty_Report_ID"]], asset_class=asset, cpty=meta)

# =============================================================================
# VERIFY + SHUFFLE
# =============================================================================
assert len(INTERNAL) == 300, len(INTERNAL)
assert len(CPTY_ROWS) == 200, len(CPTY_ROWS)
assert len(KNOWN) == 120
rng.shuffle(INTERNAL); rng.shuffle(CPTY_ROWS)
int_ids = [r["Internal_Trade_ID"] for r in INTERNAL]; cp_ids = [r["Cpty_Report_ID"] for r in CPTY_ROWS]
assert len(set(int_ids)) == 300 and len(set(cp_ids)) == 200
assert sorted(i for g in GROUPS for i in g["internal_trade_ids"]) == sorted(int_ids)
assert sorted(i for g in GROUPS for i in g["cpty_report_ids"]) == sorted(cp_ids)
sc = Counter(g["scenario"] for g in GROUPS)
assert sc == {"KNOWN_EXACT":120,"NEAR_MATCH_WITHIN_TOLERANCE":18,"TRUE_ECONOMIC_BREAK":20,"N_TO_1_ALLOCATION":12,
              "MULTI_LEG_NET":8,"CANCEL_CORRECT_CHAIN":5,"INTERNAL_ORPHAN":85,"CPTY_ALLEGED":7}, sc
assert sum(1 for k in KNOWN if k["kind"] == "UST32") == 12 and sum(1 for k in KNOWN if k["kind"] == "DIRTY") == 8
assert sum(1 for k in KNOWN if k["meta"]["direction_inverted"]) == 15
assert sum(1 for k in KNOWN if k["meta"]["shares_exec_id"]) == 60
assert len({k["meta"]["dir_style"] for k in KNOWN}) == 5

# =============================================================================
# WRITE EXCEL
# =============================================================================
INT_COLS = ["Internal_Trade_ID","UTI_Exec_ID","Parent_Link_ID","Leg_Structure","Portfolio_Book","Trade_Date","Settle_Date",
 "Asset_Class","Counterparty_Code","Counterparty_Legal_Name","Side","Ticker","ISIN","CUSIP","SEDOL","Instrument_Description",
 "Quantity_Notional","Clean_Price_Rate","Currency","Principal_Gross","Accrued_Interest","Commission_Fees",
 "Net_Settlement_Amount","Coupon_Rate","Maturity_Date"]
CP_COLS = ["Cpty_Report_ID","Shared_Market_Exec_Ref","Trans_Lifecycle_Action","Cancels_Prior_Report_ID",
 "Reporting_Counterparty_Name","Counterparty_LEI","Reporting_Perspective","Trade_Date","Value_Settle_Date","Product_Type",
 "Dir_Ind","Primary_Security_ID","Security_ID_Source","Security_Text","Reported_Qty_Notional","Exec_Price_Rate",
 "Price_Quote_Convention","Trade_Ccy","Reported_Accrued_Int","Net_Cash_Amount","Coupon","Maturity"]
INT_FMT = {"Trade_Date":"yyyy-mm-dd","Settle_Date":"yyyy-mm-dd","Maturity_Date":"yyyy-mm-dd","Quantity_Notional":"#,##0",
           "Clean_Price_Rate":"0.00####","Principal_Gross":"#,##0.00","Accrued_Interest":"#,##0.00",
           "Commission_Fees":"#,##0.00","Net_Settlement_Amount":"#,##0.00","Coupon_Rate":"0.000##"}
CP_FMT = {"Reported_Qty_Notional":"#,##0","Exec_Price_Rate":"0.00####","Reported_Accrued_Int":"#,##0.00",
          "Net_Cash_Amount":"#,##0.00","Coupon":"0.000##"}
HDR_FILL = PatternFill("solid", fgColor="1F3864")

def style_header(ws, ncols):
    for c in ws[1][:ncols]:
        c.font = Font(bold=True, color="FFFFFF"); c.fill = HDR_FILL
        c.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
def autosize(ws, maxw=42):
    for col in ws.columns:
        w = max((len(str(c.value)) if c.value is not None else 0) for c in col[:250])
        ws.column_dimensions[get_column_letter(col[0].column)].width = min(max(w + 2, 10), maxw)
def write_table(ws, cols, rows, fmts):
    ws.append(cols)
    for r in rows: ws.append([r.get(c) for c in cols])
    style_header(ws, len(cols)); ws.freeze_panes = "A2"; ws.auto_filter.ref = ws.dimensions
    for j, c in enumerate(cols, start=1):
        if c in fmts:
            for cell in ws.iter_rows(min_row=2, min_col=j, max_col=j):
                cell[0].number_format = fmts[c]
    autosize(ws)

wb = Workbook()
ws1 = wb.active; ws1.title = "Internal_Trades"; write_table(ws1, INT_COLS, INTERNAL, INT_FMT)
ws2 = wb.create_sheet("Counterparty_Trades"); write_table(ws2, CP_COLS, CPTY_ROWS, CP_FMT)

ws3 = wb.create_sheet("Known_Exact_Matches")
K_COLS = ["Match_Seq","Internal_Trade_ID","Cpty_Report_ID","Asset_Class","Internal_Counterparty_Code",
          "Shared_Exec_Ref_Present","Normalization_Tags"]
ws3.append(K_COLS)
known_sorted = KNOWN[:]; rng.shuffle(known_sorted)
for n, k in enumerate(known_sorted, 1):
    ws3.append([n, k["t"]["Internal_Trade_ID"], k["row"]["Cpty_Report_ID"], k["t"]["Asset_Class"],
                k["t"]["Counterparty_Code"], "Y" if k["meta"]["shares_exec_id"] else "N", "; ".join(k["tags"])])
style_header(ws3, len(K_COLS)); ws3.freeze_panes = "A2"
assert ws3.max_row == 121                                   # header + exactly 120 pairs
summary = [("SUMMARY TABLE", ""), ("Internal_Trades rows", len(INTERNAL)), ("Counterparty_Trades rows", len(CPTY_ROWS)),
           ("Known exact pairs (this tab)", len(KNOWN)), ("Unlabeled internal rows (blind)", len(INTERNAL) - len(KNOWN)),
           ("Unlabeled counterparty rows (blind)", len(CPTY_ROWS) - len(KNOWN)), ("", ""), ("Known pairs by asset class", "")]
for a, n in Counter(k["t"]["Asset_Class"] for k in KNOWN).items(): summary.append((f"  {a}", n))
summary += [("", ""), ("Known pairs by normalization challenge", ""),
            ("  UST 32nds pricing", sum(k["kind"] == "UST32" for k in KNOWN)),
            ("  Dirty-price bond quotes", sum(k["kind"] == "DIRTY" for k in KNOWN)),
            ("  Dealer bilateral inversion", sum(k["meta"]["direction_inverted"] for k in KNOWN)),
            ("  Shared exec ref present", sum(k["meta"]["shares_exec_id"] for k in KNOWN)),
            ("  Shared exec ref absent", sum(not k["meta"]["shares_exec_id"] for k in KNOWN)), ("", ""),
            ("Known pairs by direction/sign style", "")]
LBL = {"BSPR":"B/S/P/R unsigned qty","BLANK_SIGNED":"Blank Dir_Ind + signed qty","BOTH":"S/P code AND negative qty",
       "BOTSLD":"BOT/SLD/PAY/REC unsigned","LOWER":"lowercase buy/sell/pay/rec"}
for s, n in sorted(Counter(k["meta"]["dir_style"] for k in KNOWN).items()): summary.append((f"  {LBL[s]}", n))
for r, (a, b) in enumerate(summary, 1):
    ws3.cell(row=r, column=9, value=a); ws3.cell(row=r, column=10, value=b)
ws3.cell(row=1, column=9).font = Font(bold=True)
autosize(ws3, 60)
wb.save(XLSX_OUT)

# =============================================================================
# WRITE ORACLE JSON
# =============================================================================
oracle = {
 "metadata": {"dataset": "RecDiffy_Trade_Reconciliation_Dataset.xlsx", "seed": SEED,
              "row_counts": {"Internal_Trades": 300, "Counterparty_Trades": 200, "Known_Exact_Matches": 120},
              "scenario_counts": dict(sc),
              "suggested_tolerances": {"accrued_abs": 1.25, "fx_rate_abs": 0.0001, "fee_abs": 0.05,
                                       "note": "Net cash deltas for near-matches are derived; see per-group net_cash_delta"},
              "conventions": {
                "net_cash": "Absolute (unsigned) amount. BUY/PAY_FIXED: principal+accrued+fees; SELL/REC_FIXED: principal+accrued-fees.",
                "signed_qty": "Brokers with BLANK_SIGNED/BOTH styles send negative qty for SELL and PAY_FIXED.",
                "dir_codes": {"BSPR": "B/S (P/R for swaps), unsigned", "BLANK_SIGNED": "blank Dir_Ind, signed qty",
                              "BOTH": "B/S (P/R) AND signed qty", "BOTSLD": "BOT/SLD/PAY/REC",
                              "LOWER": "buy/sell/pay/rec"},
                "dealer_inversion": "DEALER_BILATERAL_VIEW rows report the opposite side of the internal trade.",
                "dirty_price": "Exec_Price_Rate = clean + accrued/notional*100 when Price_Quote_Convention=DIRTY_PCT_PAR.",
                "ust_32nds": "'98-24' = 98+24/32; trailing '+' adds 1/64.",
                "date_formats_by_counterparty": {c: v["datefmt"] for c, v in CPTY.items()},
                "multi_leg": "Cpty row = net of LEG_1_PAY and LEG_2_RECEIVE; direction mirrors LEG_1_PAY.",
                "cancel_correct": "CANC.Cancels_Prior -> NEW report id; CORR.Cancels_Prior -> CANC report id; CORR matches internal."},
              "counterparty_alias_map": {c: {"legal_name": v["legal"], "lei": v["lei"], "aliases": v["aliases"],
                                             "dir_style": v["style"], "security_id_preference": v["pref"]}
                                         for c, v in CPTY.items()}},
 "groups": GROUPS}
def _default(o):
    if isinstance(o, (dt.date, dt.datetime)): return o.isoformat()
    raise TypeError(type(o))
with open(JSON_OUT, "w", encoding="utf-8") as fh:
    json.dump(oracle, fh, indent=2, ensure_ascii=False, default=_default)

print(f"Wrote {XLSX_OUT}: Internal={len(INTERNAL)}  Counterparty={len(CPTY_ROWS)}  Known pairs={len(KNOWN)}")
print(f"Wrote {JSON_OUT}: {len(GROUPS)} groups  {dict(sc)}")
```

## Design decisions and assumptions

- **Counts:** the internal side is 120+18+20+36+16+5+85 = 300, and the counterparty side is 120+18+20+12+8+15+0+7 = 200. The script asserts both, plus oracle coverage: every row appears in exactly one oracle group.
- **Tab 3** holds exactly 120 data rows in A:G. The summary table sits in columns I:J, so row-counting tools still see 120 pairs. The summary reports only counts for the known pairs and the blind totals, and does not reveal the blind scenarios.
- **Net cash** is an unsigned amount in both tabs, so dealer inversion changes only `Dir_Ind` and the sign of the quantity.
- **Multi-leg and allocation rows:**
  - The multi-leg internal rows share a `Parent_Link_ID`.
  - Allocation rows deliberately have no parent or block ID, so the engine must discover the grouping.
  - For allocations and multi-leg trades, the exec-ID share rate is about half.
- **Cancel/correct chains:** `CANC` points at the `NEW` report ID, and `CORR` points at the `CANC` report ID. The oracle also records the surviving `CORR` ID.
- **Date formats:** counterparty dates are strings in per-broker formats (ISO, `MM/DD/YYYY`, `YYYYMMDD`, `DD-MON-YYYY`, `DD/MM/YYYY`). The formats are listed in the oracle metadata. If you want less noise, set every `datefmt` to `"%Y-%m-%d"`.
- **Identifiers:** CUSIP, ISIN, SEDOL and LEI values are synthetic but carry valid check digits. FX forwards and swaps have no ISIN, CUSIP or SEDOL, so brokers fall back to `TICKER`.
- **Settle-date breaks:** the cpty accrued interest is not re-accrued for the one-day shift, so the settle date is the only break in that scenario.

I haven't executed the script myself. If an assertion fires on your machine, paste the traceback and I'll fix it.