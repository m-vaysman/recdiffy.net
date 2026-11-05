I can’t attach binary `.xlsx` / `.json` files from here. Run the script below locally (`pip install openpyxl`, Python 3.9+). It writes both files and refuses to save unless the row counts and economic relationships check out.

```python
#!/usr/bin/env python3
"""
RecDiffy.NET institutional trade-reconciliation fixture generator.

Produces:
  RecDiffy_Trade_Reconciliation_Dataset.xlsx
  RecDiffy_Hidden_Test_Oracle.json

Usage:
  pip install openpyxl
  python generate_recdiffy_dataset.py [output_dir]

The workbook is the engine input. Tab 3 lists only the 120 exact pairs.
Blind ground truth is only in the JSON oracle.

Monetary fields in Excel are IEEE floats. Every value in this file round-trips
when rounded half-up to 2dp (money) or 8dp (rates). The JSON stores exact
decimal strings; prefer those for assertions.
"""

from __future__ import annotations

import json
import random
import sys
from copy import deepcopy
from datetime import date, timedelta
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path

try:
    from openpyxl import Workbook
    from openpyxl.styles import Alignment, Font, PatternFill
    from openpyxl.utils import get_column_letter
    from openpyxl.worksheet.datavalidation import DataValidation  # noqa: F401  (import guard)
except ImportError:
    sys.stderr.write("openpyxl is required: pip install openpyxl\n")
    raise

SEED = 20260315
OUT_XLSX = "RecDiffy_Trade_Reconciliation_Dataset.xlsx"
OUT_JSON = "RecDiffy_Hidden_Test_Oracle.json"

INTERNAL_COLS = [
    "Internal_Trade_ID", "UTI_Exec_ID", "Parent_Link_ID", "Leg_Structure",
    "Portfolio_Book", "Trade_Date", "Settle_Date", "Asset_Class",
    "Counterparty_Code", "Counterparty_Legal_Name", "Side", "Ticker", "ISIN",
    "CUSIP", "SEDOL", "Instrument_Description", "Quantity_Notional",
    "Clean_Price_Rate", "Currency", "Principal_Gross", "Accrued_Interest",
    "Commission_Fees", "Net_Settlement_Amount", "Coupon_Rate", "Maturity_Date",
]
CPTY_COLS = [
    "Cpty_Report_ID", "Shared_Market_Exec_Ref", "Trans_Lifecycle_Action",
    "Cancels_Prior_Report_ID", "Reporting_Counterparty_Name", "Counterparty_LEI",
    "Reporting_Perspective", "Trade_Date", "Value_Settle_Date", "Product_Type",
    "Dir_Ind", "Primary_Security_ID", "Security_ID_Source", "Security_Text",
    "Reported_Qty_Notional", "Exec_Price_Rate", "Price_Quote_Convention",
    "Trade_Ccy", "Reported_Accrued_Int", "Net_Cash_Amount", "Coupon", "Maturity",
]

BOOKS = [
    "MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP", "INDEX_ARB",
    "EM_MACRO", "RATES_RV", "CASH_EQ", "TREASURY_LIQ",
]
ALLOC_BOOKS = ["MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP"]

CPTY_MASTER = [
    ("GSCO", "Goldman Sachs & Co. LLC", "GSCO0000000001",
     ["GS_NY_PRIME", "Goldman Sachs International", "GS_EQD_NY", "GSCO_PB"]),
    ("JPM", "JPMorgan Chase Bank, N.A.", "JPM000000000001",
     ["JPMS_CLEAR", "J.P. Morgan Securities", "JPM_PRIME", "JPMC_NY_RATES"]),
    ("MSCO", "Morgan Stanley & Co. LLC", "MSCO0000000001",
     ["MS_PWM", "Morgan Stanley & Co", "MSCO_NY", "MS_PRIME_BRK"]),
    ("BAML", "BofA Securities, Inc.", "BAML0000000001",
     ["BOFA_MLPF&S", "Bank of America Merrill Lynch", "BAML_PRIME", "BOFA_SECURITIES"]),
    ("CITI", "Citigroup Global Markets Inc.", "CITI0000000001",
     ["CGMI_NY", "Citigroup Global Markets", "CITI_PRIME", "CGMI_RATES"]),
    ("BARC", "Barclays Capital Inc.", "BARC0000000001",
     ["BARCAP_LDN", "Barclays Capital", "BARC_PRIME", "BARCLAYS_BANK_PLC"]),
    ("UBS", "UBS Securities LLC", "UBS000000000001",
     ["UBS_AG_LDN", "UBS Securities", "UBS_PRIME", "UBS_AG_STAMFORD"]),
    ("CITADEL", "Citadel Securities LLC", "CTDL0000000001",
     ["CITADEL_SEC", "Citadel Securities", "CITADEL_NY", "CITADEL_CLEARING"]),
]


def D(x) -> Decimal:
    return Decimal(str(x))


def money(x) -> Decimal:
    return D(x).quantize(D("0.01"), rounding=ROUND_HALF_UP)


def q8(x) -> Decimal:
    return D(x).quantize(D("0.00000001"), rounding=ROUND_HALF_UP)


def dstr(x) -> str:
    if x is None:
        return None
    return format(D(x), "f")


def floats_recover(value: Decimal, places: int) -> bool:
    q = D(10) ** -places
    original = D(value).quantize(q)
    recovered = D(float(original)).quantize(q, rounding=ROUND_HALF_UP)
    return recovered == original


# ---------------------------------------------------------------------------
# Identifier check digits
# ---------------------------------------------------------------------------

def _luhn_check_digit(numeric: str) -> str:
    total = 0
    for i, ch in enumerate(reversed(numeric)):
        n = int(ch)
        if i % 2 == 0:
            n *= 2
            if n > 9:
                n -= 9
        total += n
    return str((10 - (total % 10)) % 10)


def _expand_alnum(text: str) -> str:
    out = []
    for ch in text:
        if ch.isdigit():
            out.append(ch)
        else:
            out.append(str(ord(ch.upper()) - 55))
    return "".join(out)


def cusip_check_digit(base8: str) -> str:
    if len(base8) != 8:
        raise ValueError(base8)
    total = 0
    for i, ch in enumerate(base8):
        v = int(ch) if ch.isdigit() else ord(ch.upper()) - 55
        if i % 2 == 1:
            v *= 2
        total += v // 10 + v % 10
    return str((10 - (total % 10)) % 10)


def make_cusip(base8: str) -> str:
    return base8 + cusip_check_digit(base8)


def isin_check_digit(body11: str) -> str:
    if len(body11) != 11:
        raise ValueError(body11)
    return _luhn_check_digit(_expand_alnum(body11))


def make_isin(country: str, nsin9: str) -> str:
    body = country + nsin9
    return body + isin_check_digit(body)


def sedol_check_digit(base6: str) -> str:
    weights = (1, 3, 1, 7, 3, 9)
    total = 0
    for ch, w in zip(base6, weights):
        v = int(ch) if ch.isdigit() else ord(ch.upper()) - 55
        total += v * w
    return str((10 - (total % 10)) % 10)


def make_sedol(base6: str) -> str:
    return base6 + sedol_check_digit(base6)


def make_lei(prefix18: str) -> str:
    if len(prefix18) != 18:
        raise ValueError(prefix18)
    rem = int(_expand_alnum(prefix18 + "00")) % 97
    lei = prefix18 + f"{98 - rem:02d}"
    if int(_expand_alnum(lei)) % 97 != 1:
        raise RuntimeError("LEI check digit failed")
    return lei


def _self_test_identifiers() -> None:
    if cusip_check_digit("03783310") != "0":
        raise RuntimeError("CUSIP algorithm mismatch")
    if isin_check_digit("US037833100") != "5":
        raise RuntimeError("ISIN algorithm mismatch")
    if sedol_check_digit("204625") != "1":
        raise RuntimeError("SEDOL algorithm mismatch")
    if make_isin("US", make_cusip("03783310")) != "US0378331005":
        raise RuntimeError("ISIN composition mismatch")


# ---------------------------------------------------------------------------
# Dates
# ---------------------------------------------------------------------------

def add_bdays(d: date, n: int) -> date:
    step = 1 if n >= 0 else -1
    left = abs(n)
    cur = d
    while left:
        cur += timedelta(days=step)
        if cur.weekday() < 5:
            left -= 1
    return cur


def roll_forward(d: date) -> date:
    while d.weekday() >= 5:
        d += timedelta(days=1)
    return d


def trade_dates() -> list[date]:
    days = []
    cur = date(2026, 3, 2)  # Monday
    while len(days) < 10:
        if cur.weekday() < 5:
            days.append(cur)
        cur += timedelta(days=1)
    return days


TRADE_DATES = trade_dates()


# ---------------------------------------------------------------------------
# Reference data
# ---------------------------------------------------------------------------

def build_securities() -> list[dict]:
    specs = [
        # UST bonds — fictional but check-digit-valid identifiers
        ("T4.125_05/15/31", "91282AA1", "B1C2D3", "US Treasury 4.125% 15-May-2031",
         "Bond", "USD", "0.04125", date(2031, 5, 15), True, "99.250"),
        ("T3.875_08/15/29", "91282AA2", "B1C2D4", "US Treasury 3.875% 15-Aug-2029",
         "Bond", "USD", "0.03875", date(2029, 8, 15), True, "98.750"),
        ("T4.500_02/15/36", "91282AB3", "B1C2D5", "US Treasury 4.500% 15-Feb-2036",
         "Bond", "USD", "0.04500", date(2036, 2, 15), True, "101.125"),
        ("T2.250_11/15/27", "91282AC4", "B1C2D6", "US Treasury 2.250% 15-Nov-2027",
         "Bond", "USD", "0.02250", date(2027, 11, 15), True, "97.500"),
        ("T4.000_05/31/30", "91282AD5", "B1C2D7", "US Treasury 4.000% 31-May-2030",
         "Bond", "USD", "0.04000", date(2030, 5, 31), True, "100.250"),
        ("T3.500_02/15/33", "91282AE6", "B1C2D8", "US Treasury 3.500% 15-Feb-2033",
         "Bond", "USD", "0.03500", date(2033, 2, 15), True, "96.875"),
        # Corporate bonds
        ("HLDR4.50_02/01/30", "99901AA1", "C4F5G6", "Heldera Industries 4.50% 01-Feb-2030",
         "Bond", "USD", "0.04500", date(2030, 2, 1), False, "101.250"),
        ("KORB3.25_06/15/28", "99901AB2", "C4F5G7", "Korben Capital 3.25% 15-Jun-2028",
         "Bond", "USD", "0.03250", date(2028, 6, 15), False, "98.500"),
        ("MEXT5.00_09/01/34", "99901AC3", "C4F5G8", "Mexton Energy 5.00% 01-Sep-2034",
         "Bond", "USD", "0.05000", date(2034, 9, 1), False, "103.125"),
        ("PFLD2.875_03/15/27", "99901AD4", "C4F5G9", "Pelford Foods 2.875% 15-Mar-2027",
         "Bond", "USD", "0.02875", date(2027, 3, 15), False, "97.875"),
        ("STHR4.25_12/01/32", "99901AE5", "C4F5H1", "Sothern Rail 4.25% 01-Dec-2032",
         "Bond", "USD", "0.04250", date(2032, 12, 1), False, "99.750"),
        ("DRAN6.125_07/15/35", "99901AF6", "C4F5H2", "Dranwell Health 6.125% 15-Jul-2035",
         "Bond", "USD", "0.06125", date(2035, 7, 15), False, "104.500"),
        # Equities (fictional issuers; valid check digits)
        ("VLNA", "11111AA1", "D6G7H8", "Velona Therapeutics Inc", "Equity", "USD", None, None, False, "48.25"),
        ("KORB", "11111AB2", "D6G7H9", "Korben Systems Inc", "Equity", "USD", None, None, False, "126.40"),
        ("MEXT", "11111AC3", "D6G7J1", "Mexton Energy Inc", "Equity", "USD", None, None, False, "87.15"),
        ("PFLD", "11111AD4", "D6G7J2", "Pelford Consumer Holdings", "Equity", "USD", None, None, False, "34.80"),
        ("STHR", "11111AE5", "D6G7J3", "Sothern Industrials PLC", "Equity", "USD", None, None, False, "62.55"),
        ("DRAN", "11111AF6", "D6G7J4", "Dranwell Health Inc", "Equity", "USD", None, None, False, "155.30"),
        ("HLCY", "11111AG7", "D6G7J5", "Halcyon Payments Inc", "Equity", "USD", None, None, False, "22.75"),
        ("QBNT", "11111AH8", "D6G7J6", "Quobent Software Inc", "Equity", "USD", None, None, False, "210.60"),
        # FX pairs — contract identifier, not a listed security
        ("EURUSD", "X00001AA1", "F8H9J1", "EUR/USD FX Forward", "Forward", "EUR", None, None, False, "1.08420"),
        ("GBPUSD", "X00001AB2", "F8H9J2", "GBP/USD FX Forward", "Forward", "GBP", None, None, False, "1.27150"),
        ("AUDUSD", "X00001AC3", "F8H9J3", "AUD/USD FX Forward", "Forward", "AUD", None, None, False, "0.64280"),
        ("USDCHF", "X00001AD4", "F8H9J4", "USD/CHF FX Forward", "Forward", "USD", None, None, False, "0.88140"),
        # Swaps
        ("IRSUSD2Y", "S00001AA1", "G9J1K2", "USD SOFR OIS 2Y", "Swap", "USD", "0.03450", date(2028, 3, 18), False, "0.03450"),
        ("IRSUSD5Y", "S00001AB2", "G9J1K3", "USD SOFR OIS 5Y", "Swap", "USD", "0.03625", date(2031, 3, 18), False, "0.03625"),
        ("IRSUSD10Y", "S00001AC3", "G9J1K4", "USD SOFR OIS 10Y", "Swap", "USD", "0.03900", date(2036, 3, 18), False, "0.03900"),
        ("IRSEUR5Y", "S00001AD4", "G9J1K5", "EUR EURIBOR IRS 5Y", "Swap", "EUR", "0.02450", date(2031, 3, 18), False, "0.02450"),
    ]
    out = []
    for ticker, cusip8, sedol6, desc, asset, ccy, coupon, mat, is_ust, ref_px in specs:
        cusip = make_cusip(cusip8)
        country = "US" if asset in ("Bond", "Equity") else "XS"
        nsin = cusip if country == "US" else cusip  # 9-char NSIN
        isin = make_isin(country, nsin)
        sedol = make_sedol(sedol6)
        out.append({
            "ticker": ticker,
            "isin": isin,
            "cusip": cusip,
            "sedol": sedol,
            "description": desc,
            "asset_class": asset,
            "currency": ccy,
            "coupon": None if coupon is None else D(coupon),
            "maturity": mat,
            "is_ust": is_ust,
            "ref_price": D(ref_px),
        })
    return out


def build_reference() -> dict:
    securities = build_securities()
    by_any = {}
    for sec in securities:
        for src, key in (("TICKER", "ticker"), ("ISIN", "isin"), ("CUSIP", "cusip"), ("SEDOL", "sedol")):
            token = sec[key]
            if token in by_any:
                raise RuntimeError(f"security id collision {token}")
            by_any[token] = sec
    codes = {}
    alias_to_code = {}
    lei_to_code = {}
    for code, legal, entity14, aliases in CPTY_MASTER:
        if len(entity14) != 14:
            raise RuntimeError(entity14)
        lei = make_lei("5493" + entity14)  # 4 + 14 = 18
        if any(a == code or a == legal for a in aliases):
            raise RuntimeError("alias collides with canonical name")
        codes[code] = {"code": code, "legal_name": legal, "lei": lei, "aliases": list(aliases)}
        lei_to_code[lei] = code
        for a in aliases:
            if a in alias_to_code:
                raise RuntimeError(f"duplicate alias {a}")
            alias_to_code[a] = code
    return {
        "securities": securities,
        "by_id": by_any,
        "codes": codes,
        "alias_to_code": alias_to_code,
        "lei_to_code": lei_to_code,
    }


# ---------------------------------------------------------------------------
# Economics
# ---------------------------------------------------------------------------

PRODUCT = {
    ("Bond", True): "UST_BOND",
    ("Bond", False): "CORP_BOND",
    ("Equity", False): "EQUITY",
    ("Forward", False): "FX_FORWARD",
    ("Swap", False): "IRS",
}
PRODUCT_ASSET = {
    "UST_BOND": "Bond", "CORP_BOND": "Bond", "EQUITY": "Equity",
    "FX_FORWARD": "Forward", "IRS": "Swap",
}
FLIP = {"BUY": "SELL", "SELL": "BUY", "PAY_FIXED": "REC_FIXED", "REC_FIXED": "PAY_FIXED"}


def product_type(sec: dict) -> str:
    return PRODUCT[(sec["asset_class"], bool(sec.get("is_ust")) if sec["asset_class"] == "Bond" else False)]


def principal_of(asset: str, qty: Decimal, price: Decimal) -> Decimal:
    if asset == "Bond":
        return money(qty * price / D(100))
    if asset == "Equity":
        return money(qty * price)
    if asset == "Forward":
        return money(qty * price)
    if asset == "Swap":
        return money(0)
    raise ValueError(asset)


def net_of(side: str, principal: Decimal, accrued: Decimal, commission: Decimal) -> Decimal:
    if side in ("BUY", "PAY_FIXED"):
        return money(-(principal + accrued + commission))
    if side in ("SELL", "REC_FIXED"):
        return money(principal + accrued - commission)
    raise ValueError(side)


def parse_32nds(text: str) -> Decimal:
    plus = text.endswith("+")
    core = text[:-1] if plus else text
    whole, frac = core.split("-")
    ticks = D(int(frac)) + (D("0.5") if plus else D(0))
    return D(int(whole)) + ticks / D(32)


def format_32nds(whole: int, ticks: int, plus: bool) -> tuple[str, Decimal]:
    text = f"{whole}-{ticks:02d}" + ("+" if plus else "")
    return text, parse_32nds(text)


def encode_direction(side: str, qty: Decimal, style: str) -> tuple[str | None, Decimal]:
    sellish = side in ("SELL", "PAY_FIXED")
    words_bs = {"BUY": "B", "SELL": "S", "PAY_FIXED": "P", "REC_FIXED": "R"}
    words_bot = {"BUY": "BOT", "SELL": "SLD", "PAY_FIXED": "PAY", "REC_FIXED": "REC"}
    words_low = {"BUY": "buy", "SELL": "sell", "PAY_FIXED": "pay", "REC_FIXED": "rec"}
    if style == "UNSIGNED_BS":
        return words_bs[side], qty
    if style == "BLANK_SIGNED":
        return None, (-qty if sellish else qty)
    if style == "DIR_AND_NEG":
        return words_bs[side], (-qty if sellish else qty)
    if style == "BOT_SLD":
        return words_bot[side], qty
    if style == "LOWERCASE":
        return words_low[side], qty
    raise ValueError(style)


def decode_direction(dir_ind, qty, asset: str) -> tuple[str, Decimal]:
    q = D(qty)
    d = (dir_ind or "").strip()
    du = d.upper()
    is_swap = asset == "Swap"
    if d == "":
        if q < 0:
            side = "PAY_FIXED" if is_swap else "SELL"
        else:
            side = "REC_FIXED" if is_swap else "BUY"
        return side, abs(q)
    if du in ("B", "BUY", "BOT"):
        return "BUY", abs(q)
    if du in ("S", "SELL", "SLD"):
        return "SELL", abs(q)
    if du in ("P", "PAY", "PAY_FIXED"):
        return "PAY_FIXED", abs(q)
    if du in ("R", "REC", "REC_FIXED", "RECEIVE"):
        return "REC_FIXED", abs(q)
    raise ValueError(f"bad Dir_Ind {dir_ind!r}")


def decode_price(raw, convention: str, accrued: Decimal, qty: Decimal) -> Decimal:
    if convention == "32NDS_UST":
        return parse_32nds(str(raw))
    if convention == "DIRTY_PRICE":
        return D(raw) - (D(accrued) / abs(D(qty)) * D(100))
    return D(raw)


def security_token(sec: dict, source: str) -> str:
    return {"TICKER": sec["ticker"], "ISIN": sec["isin"], "CUSIP": sec["cusip"], "SEDOL": sec["sedol"]}[source]


# ---------------------------------------------------------------------------
# Dataset
# ---------------------------------------------------------------------------

class IdFactory:
    def __init__(self, rng: random.Random):
        self.rng = rng
        self.used = set()

    def make(self, prefix: str) -> str:
        while True:
            token = f"{prefix}-{self.rng.randrange(10**8):08d}"
            if token not in self.used:
                self.used.add(token)
                return token

    def uti(self) -> str:
        while True:
            token = "UTI" + "".join(self.rng.choice("0123456789ABCDEF") for _ in range(28))
            if token not in self.used:
                self.used.add(token)
                return token


class Dataset:
    def __init__(self, rng: random.Random):
        self.rng = rng
        self.ids = IdFactory(rng)
        self.ref = build_reference()
        self.internal: list[dict] = []
        self.cpty: list[dict] = []
        self.exact = []
        self.near = []
        self.breaks = []
        self.allocs = []
        self.packages = []
        self.lifecycles = []
        self.orphans = []
        self.alleged = []
        self._n = 100

    def next_qty(self, asset: str) -> Decimal:
        self._n += 1
        n = self._n
        if asset == "Equity":
            return D(n * 10)
        if asset == "Bond":
            return D(n * 1000)
        if asset == "Forward":
            return D(n * 10000)
        if asset == "Swap":
            return D(n * 100000)
        raise ValueError(asset)

    def pick_sec(self, asset: str, i: int, ust=None) -> dict:
        pool = [s for s in self.ref["securities"] if s["asset_class"] == asset]
        if ust is True:
            pool = [s for s in pool if s.get("is_ust")]
        elif ust is False:
            pool = [s for s in pool if not s.get("is_ust")]
        if not pool:
            raise RuntimeError(asset)
        return pool[i % len(pool)]

    def alias(self, code: str) -> str:
        return self.rng.choice(self.ref["codes"][code]["aliases"])

    def make_internal(self, econ: dict, role: str, scenario_id: str) -> dict:
        sec = econ["security"]
        code = econ["cpty_code"]
        row = {
            "Internal_Trade_ID": self.ids.make("INT"),
            "UTI_Exec_ID": econ["uti"],
            "Parent_Link_ID": econ.get("parent_link_id"),
            "Leg_Structure": econ.get("leg_structure", "SINGLE"),
            "Portfolio_Book": econ["book"],
            "Trade_Date": econ["trade_date"],
            "Settle_Date": econ["settle_date"],
            "Asset_Class": econ["asset_class"],
            "Counterparty_Code": code,
            "Counterparty_Legal_Name": self.ref["codes"][code]["legal_name"],
            "Side": econ["side"],
            "Ticker": sec["ticker"],
            "ISIN": sec["isin"],
            "CUSIP": sec["cusip"],
            "SEDOL": sec["sedol"],
            "Instrument_Description": econ.get("description", sec["description"]),
            "Quantity_Notional": D(econ["qty"]),
            "Clean_Price_Rate": D(econ["clean_price"]),
            "Currency": econ["currency"],
            "Principal_Gross": money(econ["principal"]),
            "Accrued_Interest": money(econ["accrued"]),
            "Commission_Fees": money(econ["commission"]),
            "Net_Settlement_Amount": money(econ["net"]),
            "Coupon_Rate": econ["coupon"],
            "Maturity_Date": econ["maturity"],
        }
        if row["Quantity_Notional"] <= 0:
            raise RuntimeError("internal qty must be positive")
        row["_meta"] = {"role": role, "scenario_id": scenario_id, "econ": snapshot_econ(econ)}
        self.internal.append(row)
        return row

    def make_cpty(self, econ: dict, encoding: dict, role: str, scenario_id: str,
                  lifecycle: str = "NEW", cancels_id: str | None = None,
                  report_id: str | None = None) -> dict:
        sec = econ["security"]
        code = econ["cpty_code"]
        perspective = encoding["perspective"]
        side_for_words = econ["side"]
        reported_net = money(econ["net"])
        if perspective == "DEALER_BILATERAL_VIEW":
            side_for_words = FLIP[econ["side"]]
            reported_net = money(-econ["net"])
        dir_ind, reported_qty = encode_direction(side_for_words, D(econ["qty"]), encoding["direction_style"])
        raw_px, convention = encode_price(econ, encoding["price_convention"])
        if encoding["shared_exec"]:
            exec_ref = econ["uti"]
        else:
            exec_ref = self.ids.uti()
        alias = encoding["alias"]
        row = {
            "Cpty_Report_ID": report_id or self.ids.make("RPT"),
            "Shared_Market_Exec_Ref": exec_ref,
            "Trans_Lifecycle_Action": lifecycle,
            "Cancels_Prior_Report_ID": cancels_id,
            "Reporting_Counterparty_Name": alias,
            "Counterparty_LEI": self.ref["codes"][code]["lei"],
            "Reporting_Perspective": perspective,
            "Trade_Date": econ["trade_date"],
            "Value_Settle_Date": econ["settle_date"],
            "Product_Type": product_type(sec),
            "Dir_Ind": dir_ind,
            "Primary_Security_ID": security_token(sec, encoding["security_id_source"]),
            "Security_ID_Source": encoding["security_id_source"],
            "Security_Text": sec["description"],
            "Reported_Qty_Notional": reported_qty,
            "Exec_Price_Rate": raw_px,
            "Price_Quote_Convention": convention,
            "Trade_Ccy": econ["currency"],
            "Reported_Accrued_Int": money(econ["accrued"]),
            "Net_Cash_Amount": reported_net,
            "Coupon": econ["coupon"],
            "Maturity": econ["maturity"],
        }
        row["_meta"] = {
            "role": role,
            "scenario_id": scenario_id,
            "encoding": {
                "direction_style": encoding["direction_style"],
                "security_id_source": encoding["security_id_source"],
                "shared_exec": encoding["shared_exec"],
                "perspective": perspective,
                "price_convention": convention,
                "alias": alias,
                "canonical_code": code,
            },
        }
        self.cpty.append(row)
        return row


def snapshot_econ(econ: dict) -> dict:
    sec = econ["security"]
    return {
        "asset_class": econ["asset_class"],
        "counterparty_code": econ["cpty_code"],
        "side_client_view": econ["side"],
        "ticker": sec["ticker"],
        "isin": sec["isin"],
        "cusip": sec["cusip"],
        "sedol": sec["sedol"],
        "trade_date": econ["trade_date"].isoformat(),
        "settle_date": econ["settle_date"].isoformat(),
        "quantity": dstr(econ["qty"]),
        "clean_price": dstr(econ["clean_price"]),
        "currency": econ["currency"],
        "principal": dstr(money(econ["principal"])),
        "accrued": dstr(money(econ["accrued"])),
        "commission": dstr(money(econ["commission"])),
        "net_client_view": dstr(money(econ["net"])),
        "coupon": None if econ["coupon"] is None else dstr(econ["coupon"]),
        "maturity": None if econ["maturity"] is None else econ["maturity"].isoformat(),
        "book": econ["book"],
        "leg_structure": econ.get("leg_structure", "SINGLE"),
        "parent_link_id": econ.get("parent_link_id"),
    }


def encode_price(econ: dict, convention: str):
    clean = D(econ["clean_price"])
    if convention == "32NDS_UST":
        return econ["ust_text"], "32NDS_UST"
    if convention == "DIRTY_PRICE":
        dirty = clean + (D(econ["accrued"]) / D(econ["qty"]) * D(100))
        return dirty, "DIRTY_PRICE"
    return clean, convention


def base_econ(ds: Dataset, *, asset, sec, code, side, trade_date, qty, price, accrued,
              commission, book, shared_uti=None, leg="SINGLE", parent=None,
              description=None, net_override=None, principal_override=None,
              settle_date=None) -> dict:
    if asset in ("Bond", "Equity"):
        settle = settle_date or add_bdays(trade_date, 1)
        coupon = sec["coupon"]
        maturity = sec["maturity"]
        currency = sec["currency"]
    elif asset == "Forward":
        settle = settle_date or roll_forward(trade_date + timedelta(days=32))
        coupon = None
        maturity = None
        currency = sec["currency"]
    elif asset == "Swap":
        settle = settle_date or add_bdays(trade_date, 2)
        coupon = sec["coupon"]
        maturity = sec["maturity"]
        currency = sec["currency"]
    else:
        raise ValueError(asset)
    principal = money(principal_override) if principal_override is not None else principal_of(asset, D(qty), D(price))
    accrued_m = money(accrued)
    comm_m = money(commission)
    net = money(net_override) if net_override is not None else net_of(side, principal, accrued_m, comm_m)
    return {
        "asset_class": asset,
        "security": sec,
        "cpty_code": code,
        "side": side,
        "trade_date": trade_date,
        "settle_date": settle,
        "qty": D(qty),
        "clean_price": D(price),
        "accrued": accrued_m,
        "commission": comm_m,
        "currency": currency,
        "coupon": coupon,
        "maturity": maturity,
        "book": book,
        "leg_structure": leg,
        "parent_link_id": parent,
        "principal": principal,
        "net": net,
        "uti": shared_uti or ds.ids.uti(),
        "description": description,
        "ust_text": None,
    }


def encoding(ds: Dataset, code: str, *, style, source, shared, perspective, convention) -> dict:
    return {
        "direction_style": style,
        "security_id_source": source,
        "shared_exec": shared,
        "perspective": perspective,
        "price_convention": convention,
        "alias": ds.alias(code),
    }


def side_for(asset: str, i: int) -> str:
    if asset == "Swap":
        return "PAY_FIXED" if i % 2 == 0 else "REC_FIXED"
    return "BUY" if i % 2 == 0 else "SELL"


def default_accrued(asset: str, qty: Decimal, i: int) -> Decimal:
    if asset != "Bond":
        return D("0.00")
    # cent-exact and strictly positive
    return money(D(500) + D(i % 17) * D("25.50") + (qty / D(100000)) * D("1.25"))


def default_commission(asset: str, i: int) -> Decimal:
    if asset == "Equity":
        return money(D("8.00") + D(i % 5) * D("4.50"))
    if asset == "Bond" and i % 4 == 0:
        return D("150.00")
    return D("0.00")


# ---------------------------------------------------------------------------
# Scenario builders
# ---------------------------------------------------------------------------

UST_32NDS = [
    (98, 24, False), (99, 16, False), (100, 8, False), (97, 4, False),
    (101, 20, False), (98, 1, False), (99, 31, False), (102, 12, False),
    (98, 11, True), (97, 7, True), (100, 19, True), (99, 0, True),
]

DIRTY_CASES = [
    # qty, clean, accrued  -> dirty is an exact decimal
    ("1000000", "99.50", "2500.00"),
    ("2000000", "101.25", "5000.00"),
    ("500000", "98.125", "625.00"),
    ("5000000", "100.00", "10000.00"),
    ("250000", "102.375", "312.50"),
    ("3000000", "96.75", "7500.00"),
    ("1500000", "99.875", "1875.00"),
    ("1000000", "97.50", "1234.50"),
]


def build_exact(ds: Dataset) -> None:
    plan = []
    for i, spec in enumerate(UST_32NDS):
        plan.append({"asset": "Bond", "ust": True, "conv": "32NDS_UST", "invert": False, "px": spec, "i": i})
    for i, spec in enumerate(DIRTY_CASES):
        plan.append({"asset": "Bond", "ust": False, "conv": "DIRTY_PRICE", "invert": False, "px": spec, "i": i})
    for i in range(20):
        plan.append({"asset": "Bond", "ust": False, "conv": "PCT_PAR", "invert": False, "px": None, "i": i})
    for i in range(35):
        plan.append({"asset": "Equity", "ust": None, "conv": "DECIMAL", "invert": False, "px": None, "i": i})
    for i in range(8):
        plan.append({"asset": "Forward", "ust": None, "conv": "FX_RATE", "invert": True, "px": None, "i": i})
    for i in range(17):
        plan.append({"asset": "Forward", "ust": None, "conv": "FX_RATE", "invert": False, "px": None, "i": i})
    for i in range(7):
        plan.append({"asset": "Swap", "ust": None, "conv": "SWAP_RATE_DECIMAL", "invert": True, "px": None, "i": i})
    for i in range(13):
        plan.append({"asset": "Swap", "ust": None, "conv": "SWAP_RATE_DECIMAL", "invert": False, "px": None, "i": i})
    if len(plan) != 120:
        raise RuntimeError(len(plan))

    styles = (["UNSIGNED_BS"] * 25 + ["BLANK_SIGNED"] * 20 + ["DIR_AND_NEG"] * 20
              + ["BOT_SLD"] * 20 + ["LOWERCASE"] * 20)
    if len(styles) != 105:
        raise RuntimeError(len(styles))
    sources = ["ISIN"] * 30 + ["CUSIP"] * 30 + ["SEDOL"] * 30 + ["TICKER"] * 30
    ds.rng.shuffle(sources)
    shared_flags = [True] * 60 + [False] * 60
    ds.rng.shuffle(shared_flags)

    style_iter = iter(styles)
    for n, spec in enumerate(plan):
        asset = spec["asset"]
        sec = ds.pick_sec(asset, spec["i"] + n, ust=spec["ust"] if asset == "Bond" and spec["conv"] != "PCT_PAR" else (True if False else None))
        if spec["conv"] == "32NDS_UST":
            sec = ds.pick_sec("Bond", spec["i"], ust=True)
        elif spec["conv"] == "DIRTY_PRICE":
            sec = ds.pick_sec("Bond", spec["i"], ust=False)
        elif asset == "Bond":
            sec = ds.pick_sec("Bond", spec["i"], ust=False)
        code = list(ds.ref["codes"])[n % 8]
        side = side_for(asset, n)
        td = TRADE_DATES[n % len(TRADE_DATES)]
        qty = ds.next_qty(asset)
        accrued = default_accrued(asset, qty, n)
        commission = default_commission(asset, n)
        ust_text = None
        if spec["conv"] == "32NDS_UST":
            whole, ticks, plus = spec["px"]
            ust_text, price = format_32nds(whole, ticks, plus)
            accrued = default_accrued(asset, qty, n)
            commission = D("0.00")
        elif spec["conv"] == "DIRTY_PRICE":
            qty = D(spec["px"][0])
            price = D(spec["px"][1])
            accrued = D(spec["px"][2])
            commission = D("0.00")
        elif asset == "Equity":
            price = sec["ref_price"] + D("0.05") * D(n % 9)
        elif asset == "Forward":
            price = sec["ref_price"] + D("0.00010") * D(n % 7)
            accrued = D("0.00")
            commission = D("0.00")
        elif asset == "Swap":
            price = sec["ref_price"]
            accrued = D("0.00")
            commission = D("0.00")
        else:
            price = (sec["ref_price"] + D("0.125") * D(n % 5)).quantize(D("0.001"))
        book = BOOKS[n % len(BOOKS)]
        # value date variety for FX
        settle = None
        if asset == "Forward":
            settle = roll_forward(td + timedelta(days=21 + (n % 50)))
        econ = base_econ(
            ds, asset=asset, sec=sec, code=code, side=side, trade_date=td, qty=qty,
            price=price, accrued=accrued, commission=commission, book=book, settle_date=settle,
        )
        econ["ust_text"] = ust_text
        if spec["invert"]:
            style = "BOT_SLD"
            perspective = "DEALER_BILATERAL_VIEW"
        else:
            style = next(style_iter)
            perspective = "PB_CLIENT_VIEW"
        enc = encoding(
            ds, code, style=style, source=sources[n], shared=shared_flags[n],
            perspective=perspective, convention=spec["conv"],
        )
        irow = ds.make_internal(econ, "EXACT_MATCH", f"EXACT-{n+1:03d}")
        crow = ds.make_cpty(econ, enc, "EXACT_MATCH", f"EXACT-{n+1:03d}")
        challenges = ["NAMING_DRIFT", f"SEC_ID_{sources[n]}"]
        challenges.append("SHARED_EXEC_ID" if shared_flags[n] else "DIVERGENT_EXEC_ID")
        challenges.append({
            "UNSIGNED_BS": "DIR_UNSIGNED_BS",
            "BLANK_SIGNED": "DIR_BLANK_SIGNED_QTY",
            "DIR_AND_NEG": "DIR_AND_NEGATIVE_QTY",
            "BOT_SLD": "DIR_BOT_SLD",
            "LOWERCASE": "DIR_LOWERCASE",
        }[style] if not spec["invert"] else "DIR_DEALER_INVERTED")
        if spec["conv"] == "32NDS_UST":
            challenges.append("UST_32NDS")
        if spec["conv"] == "DIRTY_PRICE":
            challenges.append("DIRTY_PRICE")
        if spec["invert"]:
            challenges.append("DEALER_BILATERAL_INVERSION")
        ds.exact.append({
            "pair_number": n + 1,
            "scenario_id": f"EXACT-{n+1:03d}",
            "internal_trade_id": irow["Internal_Trade_ID"],
            "cpty_report_id": crow["Cpty_Report_ID"],
            "asset_class": asset,
            "counterparty_code": code,
            "shared_exec_ref": shared_flags[n],
            "challenges": challenges,
            "price_convention": spec["conv"],
            "direction_encoding": "DEALER_INVERT" if spec["invert"] else style,
            "security_id_source": sources[n],
            "reporting_perspective": perspective,
            "economic": irow["_meta"]["econ"],
            "street_alias": crow["Reporting_Counterparty_Name"],
            "lei": crow["Counterparty_LEI"],
            "dir_ind": crow["Dir_Ind"],
            "reported_qty": dstr(crow["Reported_Qty_Notional"]),
            "exec_price_raw": dstr(crow["Exec_Price_Rate"]) if not isinstance(crow["Exec_Price_Rate"], str) else crow["Exec_Price_Rate"],
        })


def build_near(ds: Dataset) -> None:
    specs = (
        [{"kind": "ACCRUED", "asset": "Bond", "delta": d} for d in
         ["0.40", "-0.55", "0.75", "-0.80", "1.00", "-1.10", "1.20", "-0.95"]]
        + [{"kind": "FX_RATE", "asset": "Forward", "delta": "0.0001" if i % 2 == 0 else "-0.0001"} for i in range(5)]
        + [{"kind": "FEE_PENNY", "asset": "Equity", "delta": "0.01"} for _ in range(5)]
    )
    if len(specs) != 18:
        raise RuntimeError(len(specs))
    styles = ["UNSIGNED_BS", "BLANK_SIGNED", "DIR_AND_NEG", "BOT_SLD", "LOWERCASE"]
    sources = ["ISIN", "CUSIP", "SEDOL", "TICKER"]
    for i, spec in enumerate(specs):
        asset = spec["asset"]
        sec = ds.pick_sec(asset, i, ust=False if asset == "Bond" else None)
        code = list(ds.ref["codes"])[(i + 3) % 8]
        side = side_for(asset, i)
        td = TRADE_DATES[(i + 2) % len(TRADE_DATES)]
        qty = ds.next_qty(asset)
        price = sec["ref_price"]
        accrued = D("1500.00") if asset == "Bond" else D("0.00")
        commission = D("15.00") if asset == "Equity" else D("0.00")
        settle = roll_forward(td + timedelta(days=40 + i)) if asset == "Forward" else None
        uti = ds.ids.uti()
        econ = base_econ(
            ds, asset=asset, sec=sec, code=code, side=side, trade_date=td, qty=qty,
            price=price, accrued=accrued, commission=commission, book=BOOKS[i % len(BOOKS)],
            shared_uti=uti, settle_date=settle,
        )
        enc = encoding(
            ds, code, style=styles[i % 5], source=sources[i % 4], shared=True,
            perspective="PB_CLIENT_VIEW",
            convention={"Bond": "PCT_PAR", "Equity": "DECIMAL", "Forward": "FX_RATE"}[asset],
        )
        irow = ds.make_internal(econ, "NEAR_MATCH", f"NEAR-{i+1:02d}")
        crow = ds.make_cpty(econ, enc, "NEAR_MATCH", f"NEAR-{i+1:02d}")
        delta = D(spec["delta"])
        if spec["kind"] == "ACCRUED":
            new_accrued = money(econ["accrued"] + delta)
            new_net = net_of(side, econ["principal"], new_accrued, econ["commission"])
            crow["Reported_Accrued_Int"] = new_accrued
            crow["Net_Cash_Amount"] = new_net
            detail = {
                "field": "accrued_interest",
                "internal": dstr(econ["accrued"]),
                "counterparty": dstr(new_accrued),
                "delta_cpty_minus_internal": dstr(delta),
                "net_delta_cpty_minus_internal": dstr(new_net - econ["net"]),
            }
        elif spec["kind"] == "FX_RATE":
            new_px = q8(econ["clean_price"] + delta)
            new_principal = principal_of(asset, econ["qty"], new_px)
            new_net = net_of(side, new_principal, econ["accrued"], econ["commission"])
            crow["Exec_Price_Rate"] = new_px
            crow["Net_Cash_Amount"] = new_net
            detail = {
                "field": "fx_rate",
                "internal": dstr(econ["clean_price"]),
                "counterparty": dstr(new_px),
                "delta_cpty_minus_internal": dstr(delta),
                "principal_internal": dstr(econ["principal"]),
                "principal_counterparty": dstr(new_principal),
                "net_delta_cpty_minus_internal": dstr(new_net - econ["net"]),
            }
        else:
            new_net = money(econ["net"] - D("0.01"))
            crow["Net_Cash_Amount"] = new_net
            detail = {
                "field": "commission_fee_implied",
                "internal_commission": dstr(econ["commission"]),
                "counterparty_implied_commission": dstr(econ["commission"] + D("0.01")),
                "delta_cpty_minus_internal": "0.01",
                "net_internal": dstr(econ["net"]),
                "net_counterparty": dstr(new_net),
                "net_delta_cpty_minus_internal": dstr(new_net - econ["net"]),
                "note": "Counterparty file has no fee column. A 0.01 higher fee is implied by Net_Cash_Amount. For both BUY and SELL, a higher fee moves client-view net by -0.01.",
            }
        ds.near.append({
            "scenario_id": f"NEAR-{i+1:02d}",
            "kind": spec["kind"],
            "internal_trade_id": irow["Internal_Trade_ID"],
            "cpty_report_id": crow["Cpty_Report_ID"],
            "shared_exec_ref": True,
            "uti": uti,
            "within_tolerance": True,
            "detail": detail,
            "encoding": crow["_meta"]["encoding"],
            "economic_internal": irow["_meta"]["econ"],
        })


def build_breaks(ds: Dataset) -> None:
    specs = (
        [{"kind": "PRICE", "asset": a} for a in ["Bond", "Bond", "Equity", "Equity", "Forward", "Swap"]]
        + [{"kind": "PARTIAL_80", "asset": a} for a in ["Equity", "Equity", "Bond", "Forward", "Swap"]]
        + [{"kind": "SETTLE_LAG", "asset": a} for a in ["Equity", "Equity", "Bond", "Bond", "Forward"]]
        + [{"kind": "COMMISSION_450", "asset": a} for a in ["Equity", "Equity", "Bond", "Swap"]]
    )
    if len(specs) != 20:
        raise RuntimeError(len(specs))
    styles = ["UNSIGNED_BS", "BLANK_SIGNED", "BOT_SLD", "LOWERCASE", "DIR_AND_NEG"]
    sources = ["CUSIP", "ISIN", "TICKER", "SEDOL"]
    bumps = {"Bond": D("0.50"), "Equity": D("1.25"), "Forward": D("0.00500"), "Swap": D("0.00250")}
    for i, spec in enumerate(specs):
        asset = spec["asset"]
        sec = ds.pick_sec(asset, i + 1, ust=True if asset == "Bond" and i % 2 == 0 else (False if asset == "Bond" else None))
        code = list(ds.ref["codes"])[(i + 1) % 8]
        side = side_for(asset, i + 1)
        td = TRADE_DATES[(i + 4) % len(TRADE_DATES)]
        qty = ds.next_qty(asset)
        price = sec["ref_price"]
        accrued = D("2200.00") if asset == "Bond" else D("0.00")
        commission = D("25.00") if asset in ("Equity", "Bond") else D("0.00")
        settle = roll_forward(td + timedelta(days=28 + i)) if asset == "Forward" else None
        uti = ds.ids.uti()
        econ = base_econ(
            ds, asset=asset, sec=sec, code=code, side=side, trade_date=td, qty=qty,
            price=price, accrued=accrued, commission=commission, book=BOOKS[(i + 2) % len(BOOKS)],
            shared_uti=uti, settle_date=settle,
        )
        conv = {"Bond": "PCT_PAR", "Equity": "DECIMAL", "Forward": "FX_RATE", "Swap": "SWAP_RATE_DECIMAL"}[asset]
        enc = encoding(
            ds, code, style=styles[i % 5], source=sources[i % 4], shared=True,
            perspective="PB_CLIENT_VIEW", convention=conv,
        )
        irow = ds.make_internal(econ, "FIELD_BREAK", f"BREAK-{i+1:02d}")
        crow = ds.make_cpty(econ, enc, "FIELD_BREAK", f"BREAK-{i+1:02d}")
        kind = spec["kind"]
        if kind == "PRICE":
            bump = bumps[asset]
            new_px = q8(econ["clean_price"] + bump)
            new_principal = principal_of(asset, econ["qty"], new_px)
            new_net = net_of(side, new_principal, econ["accrued"], econ["commission"])
            crow["Exec_Price_Rate"] = new_px
            crow["Net_Cash_Amount"] = new_net
            detail = {
                "internal_price": dstr(econ["clean_price"]),
                "counterparty_price": dstr(new_px),
                "price_delta": dstr(bump),
                "internal_net": dstr(econ["net"]),
                "counterparty_net": dstr(new_net),
            }
        elif kind == "PARTIAL_80":
            new_qty = money(econ["qty"] * D("0.8"))
            if new_qty != econ["qty"] * D("0.8"):
                # money() is 2dp; quantities are integral. Keep exact integral qty.
                new_qty = econ["qty"] * D("0.8")
            sign = D(-1) if crow["Reported_Qty_Notional"] < 0 else D(1)
            new_accrued = money(econ["accrued"] * D("0.8"))
            new_comm = money(econ["commission"] * D("0.8"))
            new_principal = principal_of(asset, new_qty, econ["clean_price"])
            new_net = net_of(side, new_principal, new_accrued, new_comm)
            crow["Reported_Qty_Notional"] = sign * new_qty
            crow["Reported_Accrued_Int"] = new_accrued
            crow["Net_Cash_Amount"] = new_net
            detail = {
                "internal_qty": dstr(econ["qty"]),
                "counterparty_abs_qty": dstr(new_qty),
                "fill_ratio": "0.80",
                "internal_net": dstr(econ["net"]),
                "counterparty_net": dstr(new_net),
                "note": "Size-dependent accrued and fees are scaled by 0.80 as well. Price, dates, side and instrument agree. Pairing key is the shared execution id.",
            }
        elif kind == "SETTLE_LAG":
            lagged = add_bdays(econ["settle_date"], 1)
            crow["Value_Settle_Date"] = lagged
            detail = {
                "internal_settle_date": econ["settle_date"].isoformat(),
                "counterparty_value_date": lagged.isoformat(),
                "lag_business_days_cpty_minus_internal": 1,
                "note": "T+1 internal vs T+2 street (one extra business day). All other economics agree. Pairing key is the shared execution id.",
            }
        else:
            new_net = money(econ["net"] - D("450.00"))
            crow["Net_Cash_Amount"] = new_net
            detail = {
                "internal_commission": dstr(econ["commission"]),
                "counterparty_implied_commission": dstr(econ["commission"] + D("450.00")),
                "commission_delta": "450.00",
                "internal_net": dstr(econ["net"]),
                "counterparty_net": dstr(new_net),
                "net_delta_cpty_minus_internal": "-450.00",
                "note": "No fee column on the street file. Implied fee is 450.00 higher, so client-view net is 450.00 worse.",
            }
        ds.breaks.append({
            "scenario_id": f"BREAK-{i+1:02d}",
            "kind": kind,
            "internal_trade_id": irow["Internal_Trade_ID"],
            "cpty_report_id": crow["Cpty_Report_ID"],
            "shared_exec_ref": True,
            "uti": uti,
            "detail": detail,
            "encoding": crow["_meta"]["encoding"],
            "economic_internal": irow["_meta"]["econ"],
        })


def build_allocs(ds: Dataset) -> None:
    # Distinct prices so a block cannot accidentally equal a single-ticket trade.
    templates = [
        ("Bond", False, "103.125", "PCT_PAR", "0"),
        ("Bond", True, "99.875", "PCT_PAR", "1"),
        ("Bond", False, "100.500", "PCT_PAR", "2"),
        ("Bond", False, "97.250", "PCT_PAR", "3"),
        ("Equity", None, "77.75", "DECIMAL", "0"),
        ("Equity", None, "41.20", "DECIMAL", "1"),
        ("Equity", None, "63.30", "DECIMAL", "2"),
        ("Equity", None, "18.40", "DECIMAL", "3"),
        ("Forward", None, "1.11110", "FX_RATE", "0"),
        ("Forward", None, "1.22220", "FX_RATE", "1"),
        ("Swap", None, None, "SWAP_RATE_DECIMAL", "0"),
        ("Swap", None, None, "SWAP_RATE_DECIMAL", "1"),
    ]
    if len(templates) != 12:
        raise RuntimeError(len(templates))
    styles = ["UNSIGNED_BS", "BLANK_SIGNED", "BOT_SLD", "LOWERCASE", "DIR_AND_NEG", "UNSIGNED_BS"]
    sources = ["ISIN", "CUSIP", "SEDOL", "TICKER"]
    for g, (asset, ust, px, conv, sec_i) in enumerate(templates):
        sec = ds.pick_sec(asset, int(sec_i), ust=ust if asset == "Bond" else None)
        code = list(ds.ref["codes"])[g % 8]
        side = "PAY_FIXED" if asset == "Swap" and g % 2 == 0 else (
            "REC_FIXED" if asset == "Swap" else ("BUY" if g % 2 == 0 else "SELL")
        )
        td = TRADE_DATES[g % len(TRADE_DATES)]
        if asset == "Equity":
            qtys = [D(4000 + g * 100), D(2500 + g * 50), D(1500 + g * 25)]
        elif asset == "Bond":
            qtys = [D(1_000_000 + g * 10000), D(600_000 + g * 5000), D(400_000 + g * 5000)]
        elif asset == "Forward":
            qtys = [D(2_000_000 + g * 10000), D(1_500_000), D(500_000 + g * 10000)]
        else:
            qtys = [D(10_000_000), D(6_000_000 + g * 100000), D(4_000_000)]
        price = sec["ref_price"] if asset == "Swap" else D(px)
        settle = roll_forward(td + timedelta(days=45)) if asset == "Forward" else None
        components = []
        sum_qty = D(0)
        sum_prin = D(0)
        sum_acc = D(0)
        sum_comm = D(0)
        sum_net = D(0)
        for leg_i, (book, qty) in enumerate(zip(ALLOC_BOOKS, qtys)):
            if asset == "Bond":
                accrued = money(qty / D(1000) * D("1.25"))
                commission = D("0.00")
            elif asset == "Equity":
                accrued = D("0.00")
                commission = money(qty * D("0.005"))
            else:
                accrued = D("0.00")
                commission = D("0.00")
            econ = base_econ(
                ds, asset=asset, sec=sec, code=code, side=side, trade_date=td, qty=qty,
                price=price, accrued=accrued, commission=commission, book=book, settle_date=settle,
            )
            # Force principal from the same formula and confirm cent-exact additivity later.
            irow = ds.make_internal(econ, "ALLOCATION_COMPONENT", f"ALLOC-{g+1:02d}")
            components.append(irow)
            sum_qty += econ["qty"]
            sum_prin += econ["principal"]
            sum_acc += econ["accrued"]
            sum_comm += econ["commission"]
            sum_net += econ["net"]
        block_principal_formula = principal_of(asset, sum_qty, price)
        if money(sum_prin) != block_principal_formula:
            raise RuntimeError(f"allocation principal drift group {g+1}: {sum_prin} vs {block_principal_formula}")
        block_net = net_of(side, money(sum_prin), money(sum_acc), money(sum_comm))
        if money(sum_net) != block_net:
            raise RuntimeError(f"allocation net drift group {g+1}")
        block_econ = base_econ(
            ds, asset=asset, sec=sec, code=code, side=side, trade_date=td, qty=sum_qty,
            price=price, accrued=money(sum_acc), commission=money(sum_comm), book="BLOCK",
            settle_date=components[0]["Settle_Date"],
            principal_override=money(sum_prin), net_override=money(sum_net),
        )
        enc = encoding(
            ds, code, style=styles[g % len(styles)], source=sources[g % 4], shared=False,
            perspective="PB_CLIENT_VIEW", convention=conv,
        )
        crow = ds.make_cpty(block_econ, enc, "ALLOCATION_BLOCK", f"ALLOC-{g+1:02d}")
        ds.allocs.append({
            "scenario_id": f"ALLOC-{g+1:02d}",
            "internal_trade_ids": [r["Internal_Trade_ID"] for r in components],
            "books": list(ALLOC_BOOKS),
            "component_quantities": [dstr(q) for q in qtys],
            "cpty_report_id": crow["Cpty_Report_ID"],
            "block_quantity": dstr(sum_qty),
            "block_principal": dstr(money(sum_prin)),
            "block_accrued": dstr(money(sum_acc)),
            "block_commission": dstr(money(sum_comm)),
            "block_net_client_view": dstr(money(sum_net)),
            "shared_exec_ref": False,
            "encoding": crow["_meta"]["encoding"],
            "match_rule": (
                "Three internal allocations, one street block. After direction normalization, "
                "abs(street qty) equals the sum of internal Quantity_Notional. Sum of Principal_Gross, "
                "Accrued_Interest, Commission_Fees and client-view Net_Settlement_Amount equal the block. "
                "Do not use execution ids; they intentionally differ. Parent_Link_ID is blank."
            ),
        })


def build_packages(ds: Dataset) -> None:
    # 4 FX + 4 swap packages. Offsetting leg NPVs sum to the street net of 0.
    for i in range(8):
        asset = "Forward" if i < 4 else "Swap"
        sec = ds.pick_sec(asset, i)
        code = list(ds.ref["codes"])[(i + 5) % 8]
        td = TRADE_DATES[(i + 1) % len(TRADE_DATES)]
        parent = ds.ids.make("PL")
        npv = D("2500.00")
        if asset == "Forward":
            base_qty = D(1_000_000 + (i + 1) * 250_000)
            rate = sec["ref_price"]
            settle = roll_forward(td + timedelta(days=60 + i * 3))
            quote_qty = money(base_qty * rate)  # may be x.xx exact for these rates * 250k
            # Keep quote qty exact: if money() changed it, use the money value and accept rate * base may round.
            quote_qty = money(base_qty * rate)
            pay_econ = base_econ(
                ds, asset=asset, sec=sec, code=code, side="SELL", trade_date=td,
                qty=quote_qty, price=rate, accrued=0, commission=0, book="EM_MACRO",
                settle_date=settle, leg="LEG_1_PAY", parent=parent,
                description=sec["description"] + " PAY QUOTE",
                net_override=-npv, principal_override=quote_qty,
            )
            pay_econ["currency"] = "USD"
            rec_econ = base_econ(
                ds, asset=asset, sec=sec, code=code, side="BUY", trade_date=td,
                qty=base_qty, price=rate, accrued=0, commission=0, book="EM_MACRO",
                settle_date=settle, leg="LEG_2_RECEIVE", parent=parent,
                description=sec["description"] + " REC BASE",
                net_override=npv, principal_override=base_qty,
            )
            package_side = "BUY"
            package_qty = base_qty
            package_ccy = sec["currency"]
            rate_field = rate
        else:
            notional = D(5_000_000 * (i - 2))
            rate = sec["ref_price"]
            settle = add_bdays(td, 2)
            pay_econ = base_econ(
                ds, asset=asset, sec=sec, code=code, side="PAY_FIXED", trade_date=td,
                qty=notional, price=rate, accrued=0, commission=0, book="MACRO_RATES",
                settle_date=settle, leg="LEG_1_PAY", parent=parent,
                description=sec["description"] + " PAY FIXED",
                net_override=-npv, principal_override=0,
            )
            rec_econ = base_econ(
                ds, asset=asset, sec=sec, code=code, side="REC_FIXED", trade_date=td,
                qty=notional, price=D("0"), accrued=0, commission=0, book="MACRO_RATES",
                settle_date=settle, leg="LEG_2_RECEIVE", parent=parent,
                description=sec["description"] + " REC FLOAT",
                net_override=npv, principal_override=0,
            )
            rec_econ["coupon"] = D("0")
            package_side = "PAY_FIXED"
            package_qty = notional
            package_ccy = sec["currency"]
            rate_field = rate
        pay_row = ds.make_internal(pay_econ, "MULTI_LEG_COMPONENT", f"PKG-{i+1:02d}")
        rec_row = ds.make_internal(rec_econ, "MULTI_LEG_COMPONENT", f"PKG-{i+1:02d}")
        shared = i % 2 == 0
        pkg_uti = parent if shared else ds.ids.uti()
        pkg_econ = base_econ(
            ds, asset=asset, sec=sec, code=code, side=package_side, trade_date=td,
            qty=package_qty, price=rate_field, accrued=0, commission=0, book="BLOCK",
            settle_date=pay_row["Settle_Date"], shared_uti=pkg_uti,
            net_override=0, principal_override=0 if asset == "Swap" else principal_of(asset, package_qty, rate_field),
        )
        pkg_econ["currency"] = package_ccy
        enc = encoding(
            ds, code,
            style=["UNSIGNED_BS", "BOT_SLD", "LOWERCASE", "BLANK_SIGNED"][i % 4],
            source=["ISIN", "TICKER", "CUSIP", "SEDOL"][i % 4],
            shared=False,  # street ref is pkg_uti, which is NOT the leg UTI
            perspective="PB_CLIENT_VIEW",
            convention="FX_RATE" if asset == "Forward" else "SWAP_RATE_DECIMAL",
        )
        # Force the street exec ref to the package id (parent link) for even groups,
        # and to an unrelated ref for odd groups. make_cpty with shared_exec False
        # would mint a random ref; overwrite after.
        crow = ds.make_cpty(pkg_econ, enc, "MULTI_LEG_COMBINED", f"PKG-{i+1:02d}")
        crow["Shared_Market_Exec_Ref"] = pkg_uti
        crow["Net_Cash_Amount"] = D("0.00")
        ds.packages.append({
            "scenario_id": f"PKG-{i+1:02d}",
            "asset_class": asset,
            "parent_link_id": parent,
            "internal_trade_ids": [pay_row["Internal_Trade_ID"], rec_row["Internal_Trade_ID"]],
            "leg_structures": ["LEG_1_PAY", "LEG_2_RECEIVE"],
            "cpty_report_id": crow["Cpty_Report_ID"],
            "street_exec_ref_equals_parent_link": shared,
            "street_exec_ref": pkg_uti,
            "package_notional": dstr(package_qty),
            "package_rate": dstr(rate_field),
            "package_side_client_view": package_side,
            "leg_nets": [dstr(pay_row["Net_Settlement_Amount"]), dstr(rec_row["Net_Settlement_Amount"])],
            "combined_net": "0.00",
            "match_rule": (
                "Collapse the two internal legs that share Parent_Link_ID. Do not sum notionals "
                "(FX quote-leg qty is notional*rate and must not be added to the base leg; swap legs "
                "have equal notional). Package notional = base-leg qty for FX, either leg notional for swaps. "
                "Package rate = pay-leg Clean_Price_Rate (FX forward rate or swap fixed rate). "
                "Package side = BUY base for FX, PAY_FIXED for swaps. Combined net cash = sum of leg nets = 0. "
                "Neither leg alone matches the street row, because leg nets are +2500/-2500 rather than 0, "
                "and the FX pay leg is in USD with the quote amount."
            ),
        })


def build_lifecycles(ds: Dataset) -> None:
    assets = ["Equity", "Equity", "Bond", "Forward", "Swap"]
    bumps = {"Equity": D("1.50"), "Bond": D("0.75"), "Forward": D("0.00750"), "Swap": D("0.00300")}
    for i, asset in enumerate(assets):
        sec = ds.pick_sec(asset, i + 2, ust=False if asset == "Bond" else None)
        code = list(ds.ref["codes"])[(i + 6) % 8]
        side = side_for(asset, i)
        td = TRADE_DATES[(i + 6) % len(TRADE_DATES)]
        qty = ds.next_qty(asset)
        price = sec["ref_price"]
        accrued = D("800.00") if asset == "Bond" else D("0.00")
        commission = D("10.00") if asset == "Equity" else D("0.00")
        settle = roll_forward(td + timedelta(days=36)) if asset == "Forward" else None
        uti = ds.ids.uti()
        econ = base_econ(
            ds, asset=asset, sec=sec, code=code, side=side, trade_date=td, qty=qty,
            price=price, accrued=accrued, commission=commission, book=BOOKS[i % len(BOOKS)],
            shared_uti=uti, settle_date=settle,
        )
        conv = {"Bond": "PCT_PAR", "Equity": "DECIMAL", "Forward": "FX_RATE", "Swap": "SWAP_RATE_DECIMAL"}[asset]
        enc = encoding(
            ds, code, style=["BOT_SLD", "UNSIGNED_BS", "LOWERCASE", "BLANK_SIGNED", "DIR_AND_NEG"][i],
            source=["TICKER", "ISIN", "CUSIP", "SEDOL", "ISIN"][i],
            shared=True, perspective="PB_CLIENT_VIEW", convention=conv,
        )
        irow = ds.make_internal(econ, "LIFECYCLE_INTERNAL", f"CXL-{i+1:02d}")
        # Busted NEW
        bust_px = q8(price + bumps[asset])
        bust_principal = principal_of(asset, qty, bust_px)
        bust_net = net_of(side, bust_principal, money(accrued), money(commission))
        bust_econ = deepcopy(econ)
        bust_econ["clean_price"] = bust_px
        bust_econ["principal"] = bust_principal
        bust_econ["net"] = bust_net
        bust_econ["uti"] = ds.ids.uti()  # busted booking has its own exec ref
        bust_enc = deepcopy(enc)
        bust_enc["shared_exec"] = False
        new_row = ds.make_cpty(bust_econ, bust_enc, "LIFECYCLE_NEW", f"CXL-{i+1:02d}", lifecycle="NEW")
        # Reversing CANC: negate qty, accrued and cash. Same price, same exec ref, explicit link.
        canc = deepcopy(new_row)
        canc["Cpty_Report_ID"] = ds.ids.make("RPT")
        canc["Trans_Lifecycle_Action"] = "CANC"
        canc["Cancels_Prior_Report_ID"] = new_row["Cpty_Report_ID"]
        canc["Reported_Qty_Notional"] = -D(new_row["Reported_Qty_Notional"])
        canc["Reported_Accrued_Int"] = money(-D(new_row["Reported_Accrued_Int"]))
        canc["Net_Cash_Amount"] = money(-D(new_row["Net_Cash_Amount"]))
        canc["_meta"] = deepcopy(new_row["_meta"])
        canc["_meta"]["role"] = "LIFECYCLE_CANC"
        ds.cpty.append(canc)
        # CORR matches internal and shares the internal UTI
        corr = ds.make_cpty(econ, enc, "LIFECYCLE_CORR", f"CXL-{i+1:02d}", lifecycle="CORR")
        ds.lifecycles.append({
            "scenario_id": f"CXL-{i+1:02d}",
            "internal_trade_id": irow["Internal_Trade_ID"],
            "new_report_id": new_row["Cpty_Report_ID"],
            "canc_report_id": canc["Cpty_Report_ID"],
            "corr_report_id": corr["Cpty_Report_ID"],
            "surviving_report_id": corr["Cpty_Report_ID"],
            "shared_exec_ref_on_corr": True,
            "uti": uti,
            "busted_price": dstr(bust_px),
            "correct_price": dstr(price),
            "collapse_rule": (
                "CANC.Cancels_Prior_Report_ID points at the busted NEW row. CANC negates that row's "
                "quantity, accrued and net cash (NEW+CANC sum to zero) but the required collapse is "
                "link-based: drop both the CANC row and the referenced NEW row. The CORR row is the "
                "sole surviving street record and matches the internal trade after ordinary normalization. "
                "Do not leave the busted NEW in the economic book."
            ),
        })


def build_orphans(ds: Dataset) -> None:
    assets = ["Bond"] * 25 + ["Equity"] * 25 + ["Forward"] * 20 + ["Swap"] * 15
    if len(assets) != 85:
        raise RuntimeError(len(assets))
    for i, asset in enumerate(assets):
        sec = ds.pick_sec(asset, i, ust=False if asset == "Bond" and i % 3 else (True if asset == "Bond" else None))
        code = list(ds.ref["codes"])[i % 8]
        side = side_for(asset, i)
        td = TRADE_DATES[i % len(TRADE_DATES)]
        qty = ds.next_qty(asset)
        price = sec["ref_price"] + D("0.01") * D(i % 11)
        accrued = default_accrued(asset, qty, i)
        commission = default_commission(asset, i)
        settle = roll_forward(td + timedelta(days=20 + (i % 30))) if asset == "Forward" else None
        econ = base_econ(
            ds, asset=asset, sec=sec, code=code, side=side, trade_date=td, qty=qty,
            price=price, accrued=accrued, commission=commission, book=BOOKS[i % len(BOOKS)],
            settle_date=settle,
        )
        row = ds.make_internal(econ, "INTERNAL_ORPHAN", f"ORPH-{i+1:03d}")
        ds.orphans.append({
            "scenario_id": f"ORPH-{i+1:03d}",
            "internal_trade_id": row["Internal_Trade_ID"],
            "reason": "Unconfirmed / DK. No street row shares this UTI or the full economic key.",
            "economic": row["_meta"]["econ"],
        })


def build_alleged(ds: Dataset) -> None:
    assets = ["Bond", "Bond", "Equity", "Equity", "Forward", "Forward", "Swap"]
    styles = ["UNSIGNED_BS", "BLANK_SIGNED", "BOT_SLD", "LOWERCASE", "DIR_AND_NEG", "UNSIGNED_BS", "BOT_SLD"]
    for i, asset in enumerate(assets):
        sec = ds.pick_sec(asset, i + 3, ust=False if asset == "Bond" else None)
        code = list(ds.ref["codes"])[(i + 4) % 8]
        side = side_for(asset, i + 2)
        td = TRADE_DATES[(i + 5) % len(TRADE_DATES)]
        qty = ds.next_qty(asset)
        price = sec["ref_price"]
        accrued = D("640.00") if asset == "Bond" else D("0.00")
        commission = D("12.00") if asset == "Equity" else D("0.00")
        settle = roll_forward(td + timedelta(days=55)) if asset == "Forward" else None
        econ = base_econ(
            ds, asset=asset, sec=sec, code=code, side=side, trade_date=td, qty=qty,
            price=price, accrued=accrued, commission=commission, book="ALLEGED",
            settle_date=settle,
        )
        enc = encoding(
            ds, code, style=styles[i], source=["ISIN", "CUSIP", "SEDOL", "TICKER", "ISIN", "CUSIP", "TICKER"][i],
            shared=False, perspective="PB_CLIENT_VIEW",
            convention={"Bond": "PCT_PAR", "Equity": "DECIMAL", "Forward": "FX_RATE", "Swap": "SWAP_RATE_DECIMAL"}[asset],
        )
        crow = ds.make_cpty(econ, enc, "COUNTERPARTY_ALLEGED", f"ALG-{i+1:02d}")
        ds.alleged.append({
            "scenario_id": f"ALG-{i+1:02d}",
            "cpty_report_id": crow["Cpty_Report_ID"],
            "reason": "Street alleged trade. No internal row shares this execution ref or the full client-view economic key.",
            "encoding": crow["_meta"]["encoding"],
            "economic_client_view": snapshot_econ(econ),
        })


# ---------------------------------------------------------------------------
# Normalization used by the self-check
# ---------------------------------------------------------------------------

def normalize_cpty(row: dict, ref: dict) -> dict:
    if row["Trans_Lifecycle_Action"] == "CANC":
        raise RuntimeError("CANC rows are not economic records")
    asset = PRODUCT_ASSET[row["Product_Type"]]
    code = ref["alias_to_code"][row["Reporting_Counterparty_Name"]]
    if ref["lei_to_code"][row["Counterparty_LEI"]] != code:
        raise RuntimeError("LEI / alias mismatch")
    sec = ref["by_id"][str(row["Primary_Security_ID"])]
    side, qty = decode_direction(row["Dir_Ind"], row["Reported_Qty_Notional"], asset)
    accrued = money(row["Reported_Accrued_Int"] or 0)
    price = decode_price(row["Exec_Price_Rate"], row["Price_Quote_Convention"], accrued, qty)
    net = money(row["Net_Cash_Amount"])
    if row["Reporting_Perspective"] == "DEALER_BILATERAL_VIEW":
        side = FLIP[side]
        net = money(-net)
    return {
        "code": code,
        "isin": sec["isin"],
        "asset": asset,
        "side": side,
        "qty": D(qty),
        "price": q8(price),
        "accrued": accrued,
        "net": net,
        "trade_date": row["Trade_Date"],
        "settle_date": row["Value_Settle_Date"],
        "currency": row["Trade_Ccy"],
        "coupon": None if row["Coupon"] is None else D(row["Coupon"]),
        "maturity": row["Maturity"],
    }


def normalize_internal(row: dict) -> dict:
    return {
        "code": row["Counterparty_Code"],
        "isin": row["ISIN"],
        "asset": row["Asset_Class"],
        "side": row["Side"],
        "qty": D(row["Quantity_Notional"]),
        "price": q8(row["Clean_Price_Rate"]),
        "accrued": money(row["Accrued_Interest"]),
        "net": money(row["Net_Settlement_Amount"]),
        "trade_date": row["Trade_Date"],
        "settle_date": row["Settle_Date"],
        "currency": row["Currency"],
        "coupon": None if row["Coupon_Rate"] is None else D(row["Coupon_Rate"]),
        "maturity": row["Maturity_Date"],
    }


def key_of(n: dict) -> tuple:
    return (
        n["code"], n["isin"], n["asset"], n["side"], dstr(n["qty"]), dstr(n["price"]),
        n["trade_date"].isoformat(), n["settle_date"].isoformat(), n["currency"],
        dstr(n["accrued"]), dstr(n["net"]),
        None if n["coupon"] is None else dstr(n["coupon"]),
        None if n["maturity"] is None else n["maturity"].isoformat(),
    )


def verify(ds: Dataset) -> None:
    if len(ds.internal) != 300:
        raise RuntimeError(f"internal {len(ds.internal)}")
    if len(ds.cpty) != 200:
        raise RuntimeError(f"cpty {len(ds.cpty)}")
    if len(ds.exact) != 120:
        raise RuntimeError("exact oracle")
    roles_i = {}
    for row in ds.internal:
        roles_i.setdefault(row["_meta"]["role"], 0)
        roles_i[row["_meta"]["role"]] += 1
        if D(row["Quantity_Notional"]) <= 0:
            raise RuntimeError("negative internal qty")
    roles_c = {}
    for row in ds.cpty:
        roles_c.setdefault(row["_meta"]["role"], 0)
        roles_c[row["_meta"]["role"]] += 1
    expect_i = {
        "EXACT_MATCH": 120, "NEAR_MATCH": 18, "FIELD_BREAK": 20,
        "ALLOCATION_COMPONENT": 36, "MULTI_LEG_COMPONENT": 16,
        "LIFECYCLE_INTERNAL": 5, "INTERNAL_ORPHAN": 85,
    }
    expect_c = {
        "EXACT_MATCH": 120, "NEAR_MATCH": 18, "FIELD_BREAK": 20,
        "ALLOCATION_BLOCK": 12, "MULTI_LEG_COMBINED": 8,
        "LIFECYCLE_NEW": 5, "LIFECYCLE_CANC": 5, "LIFECYCLE_CORR": 5,
        "COUNTERPARTY_ALLEGED": 7,
    }
    if roles_i != expect_i:
        raise RuntimeError(f"internal roles {roles_i}")
    if roles_c != expect_c:
        raise RuntimeError(f"cpty roles {roles_c}")

    by_i = {r["Internal_Trade_ID"]: r for r in ds.internal}
    by_c = {r["Cpty_Report_ID"]: r for r in ds.cpty}
    if len(by_i) != 300 or len(by_c) != 200:
        raise RuntimeError("duplicate ids")

    # Exact pairs round-trip through the normalizer.
    n_32 = n_dirty = n_inv = n_shared = 0
    src_counts = {}
    for pair in ds.exact:
        left = normalize_internal(by_i[pair["internal_trade_id"]])
        right = normalize_cpty(by_c[pair["cpty_report_id"]], ds.ref)
        if key_of(left) != key_of(right):
            raise RuntimeError(f"exact pair failed normalization {pair['scenario_id']}: {key_of(left)} vs {key_of(right)}")
        if pair["shared_exec_ref"]:
            n_shared += 1
            if by_i[pair["internal_trade_id"]]["UTI_Exec_ID"] != by_c[pair["cpty_report_id"]]["Shared_Market_Exec_Ref"]:
                raise RuntimeError("shared UTI mismatch")
        else:
            if by_i[pair["internal_trade_id"]]["UTI_Exec_ID"] == by_c[pair["cpty_report_id"]]["Shared_Market_Exec_Ref"]:
                raise RuntimeError("divergent UTI unexpectedly equal")
        tags = set(pair["challenges"])
        n_32 += "UST_32NDS" in tags
        n_dirty += "DIRTY_PRICE" in tags
        n_inv += "DEALER_BILATERAL_INVERSION" in tags
        src_counts[pair["security_id_source"]] = src_counts.get(pair["security_id_source"], 0) + 1
        if "NAMING_DRIFT" not in tags:
            raise RuntimeError("naming drift missing")
        if by_c[pair["cpty_report_id"]]["Reporting_Counterparty_Name"] in (
            pair["counterparty_code"],
            ds.ref["codes"][pair["counterparty_code"]]["legal_name"],
        ):
            raise RuntimeError("street name is not drifted")
    if n_shared != 60 or n_32 != 12 or n_dirty != 8 or n_inv != 15:
        raise RuntimeError(f"challenge counts shared={n_shared} 32nds={n_32} dirty={n_dirty} inv={n_inv}")
    if src_counts != {"ISIN": 30, "CUSIP": 30, "SEDOL": 30, "TICKER": 30}:
        raise RuntimeError(src_counts)

    # Near matches: same UTI, differ only as tagged, and the normalizer does NOT see an exact key.
    for item in ds.near:
        irow = by_i[item["internal_trade_id"]]
        crow = by_c[item["cpty_report_id"]]
        if irow["UTI_Exec_ID"] != crow["Shared_Market_Exec_Ref"]:
            raise RuntimeError("near UTI")
        left = normalize_internal(irow)
        right = normalize_cpty(crow, ds.ref)
        if key_of(left) == key_of(right):
            raise RuntimeError("near match is actually exact")
        if item["kind"] == "ACCRUED":
            if abs(right["accrued"] - left["accrued"]) < D("0.40") or abs(right["accrued"] - left["accrued"]) > D("1.20"):
                raise RuntimeError("accrued delta out of range")
            if left["price"] != right["price"] or left["qty"] != right["qty"] or left["settle_date"] != right["settle_date"]:
                raise RuntimeError("accrued near disturbed other fields")
        elif item["kind"] == "FX_RATE":
            if abs(right["price"] - left["price"]) != D("0.0001"):
                raise RuntimeError("fx delta")
        elif item["kind"] == "FEE_PENNY":
            if left["price"] != right["price"] or left["accrued"] != right["accrued"]:
                raise RuntimeError("fee near disturbed price/accrued")
            if money(left["net"] - right["net"]) != D("0.01"):
                raise RuntimeError("fee penny net delta")

    for item in ds.breaks:
        irow = by_i[item["internal_trade_id"]]
        crow = by_c[item["cpty_report_id"]]
        if irow["UTI_Exec_ID"] != crow["Shared_Market_Exec_Ref"]:
            raise RuntimeError("break UTI")
        left = normalize_internal(irow)
        right = normalize_cpty(crow, ds.ref)
        if key_of(left) == key_of(right):
            raise RuntimeError("break is actually exact")
        if item["kind"] == "SETTLE_LAG":
            if right["settle_date"] != add_bdays(left["settle_date"], 1):
                raise RuntimeError("settle lag")
            if left["price"] != right["price"] or left["qty"] != right["qty"] or left["net"] != right["net"]:
                raise RuntimeError("settle lag disturbed economics")
        if item["kind"] == "COMMISSION_450":
            if money(left["net"] - right["net"]) != D("450.00"):
                raise RuntimeError("commission break net")
        if item["kind"] == "PARTIAL_80":
            if right["qty"] != left["qty"] * D("0.8"):
                raise RuntimeError("partial qty")
        if item["kind"] == "PRICE":
            if right["price"] == left["price"]:
                raise RuntimeError("price break missing")

    for grp in ds.allocs:
        rows = [by_i[i] for i in grp["internal_trade_ids"]]
        crow = by_c[grp["cpty_report_id"]]
        norm = normalize_cpty(crow, ds.ref)
        if norm["qty"] != sum(D(r["Quantity_Notional"]) for r in rows):
            raise RuntimeError("alloc qty")
        if norm["net"] != money(sum(D(r["Net_Settlement_Amount"]) for r in rows)):
            raise RuntimeError("alloc net")
        if norm["accrued"] != money(sum(D(r["Accrued_Interest"]) for r in rows)):
            raise RuntimeError("alloc accrued")
        if len({r["ISIN"] for r in rows}) != 1 or len({r["Side"] for r in rows}) != 1:
            raise RuntimeError("alloc identity")
        if [r["Portfolio_Book"] for r in rows] != ALLOC_BOOKS:
            raise RuntimeError("alloc books")
        if any(r["Parent_Link_ID"] for r in rows):
            raise RuntimeError("alloc should not carry parent link")

    for pkg in ds.packages:
        legs = [by_i[i] for i in pkg["internal_trade_ids"]]
        crow = by_c[pkg["cpty_report_id"]]
        if legs[0]["Parent_Link_ID"] != legs[1]["Parent_Link_ID"]:
            raise RuntimeError("parent link")
        if money(legs[0]["Net_Settlement_Amount"] + legs[1]["Net_Settlement_Amount"]) != D("0.00"):
            raise RuntimeError("leg nets")
        if money(crow["Net_Cash_Amount"]) != D("0.00"):
            raise RuntimeError("package net")
        # Neither leg is an exact client-view match of the combined row.
        cnorm = normalize_cpty(crow, ds.ref)
        for leg in legs:
            if key_of(normalize_internal(leg)) == key_of(cnorm):
                raise RuntimeError("leg individually matches package row")

    for chain in ds.lifecycles:
        new = by_c[chain["new_report_id"]]
        canc = by_c[chain["canc_report_id"]]
        corr = by_c[chain["corr_report_id"]]
        internal = by_i[chain["internal_trade_id"]]
        if canc["Cancels_Prior_Report_ID"] != new["Cpty_Report_ID"]:
            raise RuntimeError("canc link")
        if money(new["Reported_Qty_Notional"] + canc["Reported_Qty_Notional"]) != 0:
            raise RuntimeError("canc qty")
        if money(new["Net_Cash_Amount"] + canc["Net_Cash_Amount"]) != 0:
            raise RuntimeError("canc net")
        if key_of(normalize_internal(internal)) != key_of(normalize_cpty(corr, ds.ref)):
            raise RuntimeError("CORR does not match internal")
        if normalize_cpty(new, ds.ref)["price"] == normalize_internal(internal)["price"]:
            raise RuntimeError("NEW was not busted")

    # No unlabeled exact economic collision between a final street row and an internal row,
    # except the intended exact pairs (and CORR, which is an exact match by design).
    intended = {(p["internal_trade_id"], p["cpty_report_id"]) for p in ds.exact}
    intended |= {(c["internal_trade_id"], c["corr_report_id"]) for c in ds.lifecycles}
    final_cpty = [r for r in ds.cpty if r["_meta"]["role"] not in ("LIFECYCLE_CANC", "LIFECYCLE_NEW")]
    int_keys = {}
    for row in ds.internal:
        k = key_of(normalize_internal(row))
        int_keys.setdefault(k, []).append(row["Internal_Trade_ID"])
    for ids in int_keys.values():
        if len(ids) > 1:
            raise RuntimeError(f"ambiguous internal economic key {ids}")
    for row in final_cpty:
        k = key_of(normalize_cpty(row, ds.ref))
        owners = int_keys.get(k, [])
        if row["_meta"]["role"] in ("EXACT_MATCH", "LIFECYCLE_CORR"):
            if len(owners) != 1:
                raise RuntimeError(f"exact/CORR missing internal {row['Cpty_Report_ID']}")
            if (owners[0], row["Cpty_Report_ID"]) not in intended:
                raise RuntimeError("unexpected exact economic pair")
        else:
            if owners:
                raise RuntimeError(
                    f"unintended exact economic match {owners} <-> {row['Cpty_Report_ID']} role {row['_meta']['role']}"
                )

    # Orphan UTIs and alleged exec refs are disjoint from the other side.
    utis = {r["UTI_Exec_ID"] for r in ds.internal}
    execs = {r["Shared_Market_Exec_Ref"] for r in ds.cpty}
    for item in ds.orphans:
        uti = by_i[item["internal_trade_id"]]["UTI_Exec_ID"]
        if uti in execs:
            raise RuntimeError("orphan UTI leaked onto a street row")
    for item in ds.alleged:
        ref = by_c[item["cpty_report_id"]]["Shared_Market_Exec_Ref"]
        if ref in utis:
            raise RuntimeError("alleged exec ref leaked onto an internal row")

    # Direction codec round-trip for every style and side.
    for style in ("UNSIGNED_BS", "BLANK_SIGNED", "DIR_AND_NEG", "BOT_SLD", "LOWERCASE"):
        for asset, sides in (
            ("Equity", ("BUY", "SELL")),
            ("Swap", ("PAY_FIXED", "REC_FIXED")),
        ):
            for side in sides:
                word, qty = encode_direction(side, D("1200"), style)
                back_side, back_qty = decode_direction(word, qty, asset)
                if back_side != side or back_qty != D("1200"):
                    raise RuntimeError(f"direction round-trip {style} {side}")


# ---------------------------------------------------------------------------
# Writers
# ---------------------------------------------------------------------------

def json_ready(value):
    if isinstance(value, Decimal):
        return format(value, "f")
    if isinstance(value, date):
        return value.isoformat()
    if isinstance(value, dict):
        return {k: json_ready(v) for k, v in value.items() if not str(k).startswith("_")}
    if isinstance(value, list):
        return [json_ready(v) for v in value]
    return value


def dump_row(row: dict, excel_row: int) -> dict:
    out = {k: json_ready(row[k]) for k in row if not k.startswith("_")}
    out["excel_row"] = excel_row
    out["role"] = row["_meta"]["role"]
    out["scenario_id"] = row["_meta"]["scenario_id"]
    return out


def write_sheet(ws, columns, rows, header_color: str) -> None:
    header_fill = PatternFill("solid", fgColor=header_color)
    header_font = Font(bold=True, color="FFFFFF", name="Calibri", size=10)
    for col, name in enumerate(columns, start=1):
        cell = ws.cell(1, col, name)
        cell.fill = header_fill
        cell.font = header_font
        cell.alignment = Alignment(wrap_text=True, vertical="center")
    money_cols = {
        "Principal_Gross", "Accrued_Interest", "Commission_Fees", "Net_Settlement_Amount",
        "Reported_Accrued_Int", "Net_Cash_Amount",
    }
    qty_cols = {"Quantity_Notional", "Reported_Qty_Notional"}
    rate_cols = {"Clean_Price_Rate", "Exec_Price_Rate", "Coupon_Rate", "Coupon"}
    date_cols = {"Trade_Date", "Settle_Date", "Value_Settle_Date", "Maturity_Date", "Maturity"}
    for r_i, row in enumerate(rows, start=2):
        for c_i, name in enumerate(columns, start=1):
            value = row.get(name)
            cell = ws.cell(r_i, c_i)
            if value is None or value == "":
                cell.value = None
                continue
            if name in date_cols and isinstance(value, date):
                cell.value = value
                cell.number_format = "YYYY-MM-DD"
            elif name in qty_cols:
                q = D(value)
                cell.value = int(q) if q == q.to_integral_value() else float(q)
                cell.number_format = "#,##0.00"
            elif name in money_cols:
                if not floats_recover(D(value), 2):
                    raise RuntimeError(f"money float round-trip failed {name}={value}")
                cell.value = float(D(value))
                cell.number_format = "#,##0.00"
            elif name in rate_cols:
                if isinstance(value, str):
                    cell.value = value
                    cell.number_format = "@"
                else:
                    if not floats_recover(D(value), 8):
                        raise RuntimeError(f"rate float round-trip failed {name}={value}")
                    cell.value = float(D(value))
                    cell.number_format = "0.00000000"
            else:
                cell.value = value
    ws.auto_filter.ref = f"A1:{get_column_letter(len(columns))}{len(rows) + 1}"
    ws.freeze_panes = "A2"
    ws.auto_filter.ref = f"A1:{get_column_letter(len(columns))}{max(len(rows) + 1, 2)}"
    ws.row_dimensions[1].height = 32
    widths = {
        "Instrument_Description": 42, "Security_Text": 42, "Counterparty_Legal_Name": 34,
        "Reporting_Counterparty_Name": 34, "UTI_Exec_ID": 34, "Shared_Market_Exec_Ref": 34,
        "Counterparty_LEI": 24, "Primary_Security_ID": 22,
    }
    for c_i, name in enumerate(columns, start=1):
        ws.column_dimensions[get_column_letter(c_i)].width = widths.get(name, 18)
    ws.page_setup.orientation = "landscape"
    ws.page_setup.fitToPage = True
    ws.page_setup.fitToWidth = 1
    ws.page_setup.fitToHeight = 0
    ws.sheet_view.showGridLines = False
    ws.oddHeader.left.text = ws.title
    ws.auto_filter.ref = f"A1:{get_column_letter(len(columns))}{len(rows) + 1}"


def write_known_sheet(ws, exact_rows: list[dict]) -> None:
    columns = [
        "Pair_Number", "Internal_Trade_ID", "Cpty_Report_ID", "Asset_Class",
        "Counterparty_Code", "Shared_Exec_Ref", "Normalization_Challenges",
        "Price_Quote_Convention", "Direction_Encoding", "Security_ID_Source",
        "Reporting_Perspective",
    ]
    header_fill = PatternFill("solid", fgColor="1E8449")
    header_font = Font(bold=True, color="FFFFFF", name="Calibri", size=10)
    for col, name in enumerate(columns, start=1):
        cell = ws.cell(1, col, name)
        cell.fill = header_fill
        cell.font = header_font
        cell.alignment = Alignment(wrap_text=True, vertical="center")
    for i, pair in enumerate(exact_rows, start=2):
        values = [
            pair["pair_number"], pair["internal_trade_id"], pair["cpty_report_id"],
            pair["asset_class"], pair["counterparty_code"],
            "YES" if pair["shared_exec_ref"] else "NO",
            ";".join(pair["challenges"]), pair["price_convention"],
            pair["direction_encoding"], pair["security_id_source"],
            pair["reporting_perspective"],
        ]
        for c, v in enumerate(values, start=1):
            ws.cell(i, c, v)
    ws.auto_filter.ref = "A1:K121"
    ws.freeze_panes = "A2"
    ws.row_dimensions[1].height = 32
    for col in range(1, 12):
        ws.column_dimensions[get_column_letter(col)].width = 28 if col == 7 else 22
    ws.column_dimensions["A"].width = 14

    # Summary sits to the right so the pair table is exactly 120 data rows.
    summary_fill = PatternFill("solid", fgColor="1F4E79")
    label_font = Font(bold=True, color="FFFFFF", name="Calibri", size=10)
    ws["M1"] = "Summary_Metric"
    ws["N1"] = "Value"
    ws["M1"].fill = summary_fill
    ws["N1"].fill = summary_fill
    ws["M1"].font = label_font
    ws["N1"].font = label_font
    metrics = [
        ("Pair table rows (this sheet, column B)", "=COUNTA(B2:B121)", 120),
        ("Internal_Trades data rows", "=COUNTA(Internal_Trades!A:A)-1", 300),
        ("Counterparty_Trades data rows", "=COUNTA(Counterparty_Trades!A:A)-1", 200),
        ("Known exact 1:1 pairs listed here", "=COUNTA(B2:B121)", 120),
        ("Internal rows deliberately unlabeled", "=COUNTA(Internal_Trades!A:A)-1-COUNTA(B2:B121)", 180),
        ("Counterparty rows deliberately unlabeled", "=COUNTA(Counterparty_Trades!A:A)-1-COUNTA(B2:B121)", 80),
        ("Shared execution id among the 120", '=COUNTIF(F2:F121,"YES")', 60),
        ("Divergent execution id among the 120", '=COUNTIF(F2:F121,"NO")', 60),
        ("UST 32nds price convention", '=COUNTIF(G2:G121,"*UST_32NDS*")', 12),
        ("Dirty price bond quotes", '=COUNTIF(G2:G121,"*DIRTY_PRICE*")', 8),
        ("Dealer bilateral inversion", '=COUNTIF(G2:G121,"*DEALER_BILATERAL_INVERSION*")', 15),
        ("Security id source ISIN", '=COUNTIF(J2:J121,"ISIN")', 30),
        ("Security id source CUSIP", '=COUNTIF(J2:J121,"CUSIP")', 30),
        ("Security id source SEDOL", '=COUNTIF(J2:J121,"SEDOL")', 30),
        ("Security id source TICKER", '=COUNTIF(J2:J121,"TICKER")', 30),
        ("Asset class Bond", '=COUNTIF(D2:D121,"Bond")', 40),
        ("Asset class Equity", '=COUNTIF(D2:D121,"Equity")', 35),
        ("Asset class Forward", '=COUNTIF(D2:D121,"Forward")', 25),
        ("Asset class Swap", '=COUNTIF(D2:D121,"Swap")', 20),
    ]
    for i, (label, formula, expected) in enumerate(metrics, start=2):
        ws.cell(i, 13, label)
        ws.cell(i, 14, formula)
        ws.cell(i, 15, expected)
    ws["M22"] = "Expected"
    ws["M22"].font = Font(bold=True, name="Calibri", size=10)
    ws["O1"] = "Expected"
    ws["O1"].fill = summary_fill
    ws["O1"].font = label_font
    ws["M24"] = (
        "This sheet lists ONLY the 120 predetermined 1:1 pairs that are economically identical "
        "after normalization. The other 180 internal rows and 80 counterparty rows are unlabeled "
        "on purpose. Breaks, tolerance pairs, allocations, multi-leg packages, cancel/correct chains, "
        "orphans and alleged trades are NOT identified here. Score those from RecDiffy_Hidden_Test_Oracle.json."
    )
    ws["M24"].alignment = Alignment(wrap_text=True, vertical="top")
    ws.merge_cells("M24:O28")
    ws.column_dimensions["M"].width = 52
    ws.column_dimensions["N"].width = 18
    ws.column_dimensions["O"].width = 14
    ws.row_dimensions[24].height = 30
    ws.sheet_view.showGridLines = False


def build_oracle(ds: Dataset) -> dict:
    return {
        "dataset_name": "RecDiffy_Trade_Reconciliation_Dataset",
        "spec_version": "1.0",
        "seed": SEED,
        "row_counts": {
            "internal_trades": 300,
            "counterparty_trades": 200,
            "known_exact_matches_listed_in_excel": 120,
            "unlabeled_internal": 180,
            "unlabeled_counterparty": 80,
        },
        "files": {"workbook": OUT_XLSX, "oracle": OUT_JSON},
        "excel_layout": {
            "tabs": ["Internal_Trades", "Counterparty_Trades", "Known_Exact_Matches"],
            "known_pair_table": "Known_Exact_Matches!A1:K121",
            "summary_table": "Known_Exact_Matches!M1:O28",
            "note": "Do not treat rows absent from Known_Exact_Matches as unmatched. They are the blind set.",
        },
        "monetary_convention": {
            "client_view_net": "BUY and PAY_FIXED: net = -(principal + accrued + commission). SELL and REC_FIXED: net = +(principal + accrued - commission).",
            "principal": {
                "Bond": "qty * clean_price / 100",
                "Equity": "qty * price",
                "Forward": "qty * forward_rate (quote-currency amount)",
                "Swap": "0 at inception",
            },
            "quantity_internal": "Always unsigned and positive.",
            "accrued": "Unsigned on economic rows. CANC rows negate accrued so NEW+CANC sums to zero.",
            "excel_numbers": "Money compares at 2 decimal places, half-up. Rates compare at 8 decimal places, half-up. JSON decimal strings are exact.",
            "dealer_cash": "DEALER_BILATERAL_VIEW stores the dealer's cash sign, which is the opposite of client-view net. Multiply by -1 when converting to client view.",
        },
        "suggested_tolerances": {
            "accrued_interest_abs": "1.50",
            "fx_rate_abs": "0.00015",
            "fee_or_net_penny_abs": "0.02",
            "note": "Near-match accrued deltas are between 0.40 and 1.20. Near-match FX deltas are exactly 0.0001. Near-match fee deltas are 0.01. Field breaks are far outside these bands (price bumps of 0.50 / 1.25 / 0.005 / 0.0025, 80% quantity, 1 business-day settle lag, or 450.00 commission).",
        },
        "normalization_spec": {
            "counterparty": "Map Reporting_Counterparty_Name through reference_data.alias_to_code. Cross-check Counterparty_LEI. Compare to Internal Counterparty_Code. Street names are never the internal code or the internal legal name on exact pairs.",
            "security": "Map Primary_Security_ID using Security_ID_Source. Ticker, ISIN, CUSIP and SEDOL on the internal row identify the same instrument. Security_Text is display only.",
            "direction": "Trim Dir_Ind. B/BUY/BOT = BUY, S/SELL/SLD = SELL, P/PAY/PAY_FIXED = PAY_FIXED, R/REC/REC_FIXED/RECEIVE = REC_FIXED. Lowercase buy/sell/pay/rec are valid. If Dir_Ind is blank, a negative quantity is SELL (Bond/Equity/Forward) or PAY_FIXED (Swap); a positive quantity is BUY or REC_FIXED. If Dir_Ind is present and quantity is also negative, the word wins and the quantity is absolute — do not double-invert.",
            "perspective": "If Reporting_Perspective is DEALER_BILATERAL_VIEW, invert the decoded side (BUY<->SELL, PAY_FIXED<->REC_FIXED) and multiply Net_Cash_Amount by -1. These 15 rows use positive quantity and SLD/BOT/REC/PAY from the dealer side.",
            "price_32nds": "W-TT or W-TT+ means W + (TT + 0.5 if plus else TT) / 32. Examples: 98-24 = 98.75; 98-11+ = 98.359375. Internal Clean_Price_Rate is the decimal.",
            "price_dirty": "Clean = Exec_Price_Rate - Reported_Accrued_Int / abs(qty) * 100. Net cash was computed from the clean price, so a naive dirty*qty/100 principal will not penny-match.",
            "execution_ids": "UTI_Exec_ID equals Shared_Market_Exec_Ref on 60 of the 120 exact pairs only. Inequality is not itself a break.",
            "lifecycle": "Drop the CANC row and the NEW row it names in Cancels_Prior_Report_ID. Keep CORR. NEW is a busted price; CORR matches the internal trade.",
            "allocations": "Sum three internal rows to one street block on absolute quantity, principal, accrued, commission and client-view net. Execution ids differ on purpose.",
            "multi_leg": "Group internal rows by Parent_Link_ID. Do not sum notionals. Package net = sum of leg nets = 0. See each package match_rule.",
        },
        "reference_data": {
            "alias_to_code": ds.ref["alias_to_code"],
            "lei_to_code": ds.ref["lei_to_code"],
            "counterparties": {
                code: {"legal_name": meta["legal_name"], "lei": meta["lei"], "aliases": meta["aliases"]}
                for code, meta in ds.ref["codes"].items()
            },
            "securities": [
                {
                    "ticker": s["ticker"], "isin": s["isin"], "cusip": s["cusip"], "sedol": s["sedol"],
                    "description": s["description"], "asset_class": s["asset_class"],
                    "currency": s["currency"], "coupon": None if s["coupon"] is None else dstr(s["coupon"]),
                    "maturity": None if s["maturity"] is None else s["maturity"].isoformat(),
                    "is_ust": s["is_ust"],
                }
                for s in ds.ref["securities"]
            ],
        },
        "known_exact_matches": ds.exact,
        "blind_scenarios": {
            "within_tolerance_near_matches": ds.near,
            "true_economic_field_breaks": ds.breaks,
            "n_to_1_allocation_splits": ds.allocs,
            "multi_leg_swap_fx_representations": ds.packages,
            "cancel_correct_lifecycle_chains": ds.lifecycles,
            "unmatched_internal_orphans": ds.orphans,
            "unmatched_counterparty_alleged_trades": ds.alleged,
        },
    }


def main() -> None:
    _self_test_identifiers()
    out_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(".")
    out_dir.mkdir(parents=True, exist_ok=True)
    rng = random.Random(SEED)
    ds = Dataset(rng)
    build_exact(ds)
    build_near(ds)
    build_breaks(ds)
    build_allocs(ds)
    build_packages(ds)
    build_lifecycles(ds)
    build_orphans(ds)
    build_alleged(ds)
    verify(ds)

    rng.shuffle(ds.internal)
    rng.shuffle(ds.cpty)
    # Pair table stays in pair_number order so Pair_Number is 1..120 contiguous.
    ds.exact.sort(key=lambda p: p["pair_number"])

    wb = Workbook()
    ws_i = wb.active
    ws_i.title = "Internal_Trades"
    write_sheet(ws_i, INTERNAL_COLS, ds.internal, "1F4E79")
    ws_i.sheet_properties.tabColor = "1F4E79"

    ws_c = wb.create_sheet("Counterparty_Trades")
    write_sheet(ws_c, CPTY_COLS, ds.cpty, "6B3E26")
    ws_c.sheet_properties.tabColor = "6B3E26"

    ws_k = wb.create_sheet("Known_Exact_Matches")
    write_known_sheet(ws_k, ds.exact)
    ws_k.sheet_properties.tabColor = "1E8449"

    # Row-count guard immediately before save. Summary formulas are not data rows.
    if ws_i.max_row != 301 or ws_c.max_row != 201:
        raise RuntimeError(f"unexpected sheet dimensions {ws_i.max_row} {ws_c.max_row}")
    if ws_k["B121"].value is None or ws_k["B122"].value is not None:
        raise RuntimeError("known pair table is not exactly 120 rows")

    int_excel = {row["Internal_Trade_ID"]: i for i, row in enumerate(ds.internal, start=2)}
    cpty_excel = {row["Cpty_Report_ID"]: i for i, row in enumerate(ds.cpty, start=2)}

    oracle = build_oracle(ds)
    oracle["internal_rows"] = [dump_row(r, int_excel[r["Internal_Trade_ID"]]) for r in ds.internal]
    oracle["counterparty_rows"] = [dump_row(r, cpty_excel[r["Cpty_Report_ID"]]) for r in ds.cpty]
    oracle["internal_rows"].sort(key=lambda r: r["excel_row"])
    oracle["counterparty_rows"].sort(key=lambda r: r["excel_row"])

    def stamp(records, id_field, lookup, out_field):
        for rec in records:
            if id_field in rec:
                rec[out_field] = lookup[rec[id_field]]

    stamp(oracle["known_exact_matches"], "internal_trade_id", int_excel, "internal_excel_row")
    stamp(oracle["known_exact_matches"], "cpty_report_id", cpty_excel, "cpty_excel_row")
    for bucket, i_field, c_field in (
        ("within_tolerance_near_matches", "internal_trade_id", "cpty_report_id"),
        ("true_economic_field_breaks", "internal_trade_id", "cpty_report_id"),
    ):
        for rec in oracle["blind_scenarios"][bucket]:
            rec["internal_excel_row"] = int_excel[rec[i_field]]
            rec["cpty_excel_row"] = cpty_excel[rec[c_field]]
    for rec in oracle["blind_scenarios"]["n_to_1_allocation_splits"]:
        rec["internal_excel_rows"] = [int_excel[i] for i in rec["internal_trade_ids"]]
        rec["cpty_excel_row"] = cpty_excel[rec["cpty_report_id"]]
    for rec in oracle["blind_scenarios"]["multi_leg_swap_fx_representations"]:
        rec["internal_excel_rows"] = [int_excel[i] for i in rec["internal_trade_ids"]]
        rec["cpty_excel_row"] = cpty_excel[rec["cpty_report_id"]]
    for rec in oracle["blind_scenarios"]["cancel_correct_lifecycle_chains"]:
        rec["internal_excel_row"] = int_excel[rec["internal_trade_id"]]
        rec["new_excel_row"] = cpty_excel[rec["new_report_id"]]
        rec["canc_excel_row"] = cpty_excel[rec["canc_report_id"]]
        rec["corr_excel_row"] = cpty_excel[rec["corr_report_id"]]
    for rec in oracle["blind_scenarios"]["unmatched_internal_orphans"]:
        rec["internal_excel_row"] = int_excel[rec["internal_trade_id"]]
    for rec in oracle["blind_scenarios"]["unmatched_counterparty_alleged_trades"]:
        rec["cpty_excel_row"] = cpty_excel[rec["cpty_report_id"]]

    oracle = json_ready(oracle)

    xlsx_path = out_dir / OUT_XLSX
    json_path = out_dir / OUT_JSON
    wb.properties.title = "RecDiffy Trade Reconciliation Dataset"
    wb.properties.creator = "RecDiffy.NET fixture generator"
    wb.properties.subject = "Synthetic OMS vs street-feed reconciliation fixture. Blind key is the companion JSON."
    wb.save(xlsx_path)
    json_path.write_text(json.dumps(oracle, indent=2), encoding="utf-8")
    print(f"Wrote {xlsx_path}")
    print(f"Wrote {json_path}")
    print("Verified 300 internal / 200 counterparty / 120 known exact pairs.")
    print("Blind set: 18 near, 20 breaks, 12 allocation blocks (36 legs), 8 packages (16 legs),")
    print("5 cancel/correct chains (15 street rows), 85 orphans, 7 alleged.")


if __name__ == "__main__":
    main()
```

What the fixture contains, and how to score it:

- **Tab 1** is 300 shuffled internal rows. **Tab 2** is 200 shuffled street rows. **Tab 3** lists only the 120 exact pairs, plus a summary that counts those pairs and the normalization mix. It does not name breaks, splits, orphans, or alleged trades.
- The JSON is the answer key: role on every row, Excel row numbers, alias/LEI/security maps, and the match rule for each blind group.
- Street rows differ from the internal book on name, direction encoding, which security id is sent, and (for half of the exact pairs) execution id. Twelve exact pairs use Treasury 32nds (`98-24` = 98.75, `98-11+` = 98.359375). Eight report dirty price. Fifteen OTC pairs are dealer-perspective and must be flipped.
- Client-view net is `-(principal + accrued + commission)` for buy/pay-fixed and `+(principal + accrued - commission)` for sell/receive-fixed. Dealer-view cash is the opposite sign.
- Cancel/correct is link-based: drop the `CANC` and the `NEW` it names; `CORR` is the survivor. Allocation blocks match the **sum** of three books. Swap/FX packages match on `Parent_Link_ID`; do not sum notionals.