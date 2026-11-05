#!/usr/bin/env python3
"""Generate the RecDiffy.NET trade reconciliation test fixtures.

Produces an Excel workbook (three tabs) and a companion ground-truth oracle (JSON).
All data is synthetic, and the same seed always produces the same rows.

Usage: python generate_dataset.py --xlsx <path> --json <path>
"""
from __future__ import annotations

import argparse
import hashlib
import json
import random
import re
import shutil
import zipfile
from dataclasses import dataclass
from datetime import date, timedelta
from decimal import ROUND_HALF_UP, Decimal as D
from pathlib import Path
from typing import Optional

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.utils import get_column_letter

SEED = 20261104
rng = random.Random(SEED)

TAB1 = ["Internal_Trade_ID", "UTI_Exec_ID", "Parent_Link_ID", "Leg_Structure", "Portfolio_Book", "Trade_Date",
        "Settle_Date", "Asset_Class", "Counterparty_Code", "Counterparty_Legal_Name", "Side", "Ticker", "ISIN",
        "CUSIP", "SEDOL", "Instrument_Description", "Quantity_Notional", "Clean_Price_Rate", "Currency",
        "Principal_Gross", "Accrued_Interest", "Commission_Fees", "Net_Settlement_Amount", "Coupon_Rate",
        "Maturity_Date"]
TAB2 = ["Cpty_Report_ID", "Shared_Market_Exec_Ref", "Trans_Lifecycle_Action", "Cancels_Prior_Report_ID",
        "Reporting_Counterparty_Name", "Counterparty_LEI", "Reporting_Perspective", "Trade_Date",
        "Value_Settle_Date", "Product_Type", "Dir_Ind", "Primary_Security_ID", "Security_ID_Source", "Security_Text",
        "Reported_Qty_Notional", "Exec_Price_Rate", "Price_Quote_Convention", "Trade_Ccy", "Reported_Accrued_Int",
        "Net_Cash_Amount", "Coupon", "Maturity"]
TAB3 = ["Pair_No", "Internal_Trade_ID", "Cpty_Report_ID", "Asset_Class", "Match_Basis", "Normalization_Tests"]

TRADE_DATES = [date(2026, 10, 27), date(2026, 10, 28), date(2026, 10, 29), date(2026, 10, 30),
               date(2026, 11, 2), date(2026, 11, 3)]
HOLIDAYS = {date(2026, 11, 26), date(2026, 12, 25), date(2027, 1, 1), date(2027, 1, 18), date(2027, 2, 15),
            date(2027, 3, 26), date(2027, 5, 31), date(2027, 7, 5), date(2027, 9, 6), date(2027, 11, 25)}

ASSET_CLASS = {"UST": "Bond", "CORP": "Bond", "EQUITY": "Equity", "FX_FWD": "Forward", "FX_SWAP": "Forward",
               "IRS": "Swap", "FIXED_FIXED": "Swap"}
BONDS = ("UST", "CORP")
CODES_ALL = ["GSCO", "JPM", "MSCO", "BAML", "CITI", "BARC", "UBS", "CITADEL"]
CODES_NO_CITADEL = CODES_ALL[:-1]
ELIGIBLE = {"EQUITY": CODES_ALL, "UST": CODES_ALL, "CORP": CODES_NO_CITADEL, "FX_FWD": CODES_NO_CITADEL,
            "IRS": CODES_NO_CITADEL, "FX_SWAP": CODES_NO_CITADEL, "FIXED_FIXED": CODES_NO_CITADEL}
SIDES = {"EQUITY": ["BUY", "SELL"], "UST": ["BUY", "SELL"], "CORP": ["BUY", "SELL"], "FX_FWD": ["BUY", "SELL"],
         "IRS": ["PAY_FIXED", "REC_FIXED"]}
FLIP = {"BUY": "SELL", "SELL": "BUY", "PAY_FIXED": "REC_FIXED", "REC_FIXED": "PAY_FIXED"}
LEGAL_NAME = {"GSCO": "Goldman Sachs & Co. LLC", "JPM": "J.P. Morgan Securities LLC",
              "MSCO": "Morgan Stanley & Co. LLC", "BAML": "BofA Securities, Inc.",
              "CITI": "Citigroup Global Markets Inc.", "BARC": "Barclays Capital Inc.", "UBS": "UBS Securities LLC",
              "CITADEL": "Citadel Securities LLC"}
ALLOCATION_BOOKS = ["MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP"]
# The allocation books also appear on every other kind of row, so a book name never gives a scenario away.
BOOKS = ALLOCATION_BOOKS + ["GLOBAL_MACRO", "EVENT_DRIVEN", "FX_ALPHA", "RATES_RV", "EQ_LONG_SHORT"]
PRODUCT = {"UST": ["GOVT_BOND", "Govt Bond", "FIXED_INCOME"], "CORP": ["CORP_BOND", "Corp Bond", "FIXED_INCOME"],
           "EQUITY": ["EQUITY_CASH", "Cash Equity", "EQ"], "FX_FWD": ["FX_FWD", "FX Forward", "FXFWD"],
           "IRS": ["IRS", "Interest Rate Swap", "SWAP"], "FX_SWAP": ["FX_SWAP", "FX Swap", "FXSWAP"],
           "FIXED_FIXED": ["IRS_PACKAGE", "Fixed-Fixed Swap Package", "SWAP_PKG"]}
PLAIN_QUOTE = {"UST": "DECIMAL_CLEAN_PCT", "CORP": "DECIMAL_CLEAN_PCT", "EQUITY": "PER_SHARE", "FX_FWD": "FX_OUTRIGHT",
               "IRS": "FIXED_RATE_PCT"}
LOT = {"EQUITY": D(100), "UST": D(25000), "CORP": D(25000), "FX_FWD": D(100000), "IRS": D(1000000)}


# ------------------------------------------------------------------ numbers and dates
def cents(x: D) -> D:
    return x.quantize(D("0.01"), rounding=ROUND_HALF_UP)


def places(x: D, dp: int) -> D:
    return x.quantize(D(1).scaleb(-dp), rounding=ROUND_HALF_UP)


def add_bdays(d: date, n: int) -> date:
    while n > 0:
        d += timedelta(days=1)
        if d.weekday() < 5 and d not in HOLIDAYS:
            n -= 1
    return d


def roll(d: date) -> date:
    while d.weekday() >= 5 or d in HOLIDAYS:
        d += timedelta(days=1)
    return d


def add_months(d: date, months: int) -> date:
    m = d.month - 1 + months
    y, m = d.year + m // 12, m % 12 + 1
    last = (date(y + m // 12, m % 12 + 1, 1) - timedelta(days=1)).day
    return roll(date(y, m, min(d.day, last)))


# ------------------------------------------------------------------ identifier check digits
def cusip_check(base8: str) -> str:
    total = 0
    for i, c in enumerate(base8):
        v = int(c, 36)
        v = v * 2 if i % 2 else v
        total += v // 10 + v % 10
    return str((10 - total % 10) % 10)


def isin_check(base11: str) -> str:
    digits = "".join(str(int(c, 36)) for c in base11)
    total = 0
    for i, ch in enumerate(reversed(digits)):
        v = int(ch) * 2 if i % 2 == 0 else int(ch)
        total += v // 10 + v % 10
    return str((10 - total % 10) % 10)


def sedol_check(base6: str) -> str:
    total = sum(int(c, 36) * w for c, w in zip(base6, (1, 3, 1, 7, 3, 9)))
    return str((10 - total % 10) % 10)


def lei_check(base18: str) -> str:
    return f"{98 - int(''.join(str(int(c, 36)) for c in base18 + '00')) % 97:02d}"


def digest(text: str, alphabet: str, n: int) -> str:
    h = int(hashlib.sha256(f"{SEED}:{text}".encode()).hexdigest(), 16)
    out = ""
    for _ in range(n):
        h, r = divmod(h, len(alphabet))
        out += alphabet[r]
    return out


ALNUM = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
SEDOL_ALPHABET = "0123456789BCDFGHJKLMNPQRSTVWXYZ"


# ------------------------------------------------------------------ reference data
@dataclass(frozen=True)
class Alias:
    name: str
    code: str
    entity: str
    style: str
    id_pref: str
    view: str
    dialect: int

    @property
    def lei(self) -> str:
        base = "5493" + "00" + digest(self.entity, ALNUM, 12)
        return base + lei_check(base)


ALIASES = [
    Alias("GS_NY_PRIME", "GSCO", "Goldman Sachs & Co. LLC", "CODE_UNSIGNED", "ISIN", "PB", 0),
    Alias("GS_PB_CLEARING", "GSCO", "Goldman Sachs & Co. LLC", "CODE_UNSIGNED", "CUSIP", "PB", 1),
    Alias("Goldman Sachs International", "GSCO", "Goldman Sachs International", "WORD_UNSIGNED", "ISIN", "DEALER", 1),
    Alias("JPMS_CLEAR", "JPM", "J.P. Morgan Securities LLC", "BLANK_SIGNED", "CUSIP", "PB", 0),
    Alias("JPM_PRIME_NY", "JPM", "J.P. Morgan Securities LLC", "BLANK_SIGNED", "ISIN", "PB", 2),
    Alias("JPMCB_OTC", "JPM", "JPMorgan Chase Bank, N.A.", "BLANK_SIGNED", "ISIN", "DEALER", 0),
    Alias("MS_PRIME_NY", "MSCO", "Morgan Stanley & Co. LLC", "CODE_SIGNED", "SEDOL", "PB", 1),
    Alias("Morgan Stanley & Co LLC", "MSCO", "Morgan Stanley & Co. LLC", "CODE_SIGNED", "ISIN", "PB", 0),
    Alias("MSIP_LDN", "MSCO", "Morgan Stanley & Co. International plc", "WORD_UNSIGNED", "TICKER", "DEALER", 2),
    Alias("BOFA_MLPF&S", "BAML", "BofA Securities, Inc.", "WORD_UNSIGNED", "CUSIP", "PB", 0),
    Alias("MERRILL_PRIME", "BAML", "BofA Securities, Inc.", "WORD_UNSIGNED", "TICKER", "PB", 1),
    Alias("BANA_OTC_NY", "BAML", "Bank of America, N.A.", "BLANK_SIGNED", "ISIN", "DEALER", 0),
    Alias("CGMI_NY", "CITI", "Citigroup Global Markets Inc.", "LOWER_UNSIGNED", "TICKER", "PB", 1),
    Alias("Citigroup Global Markets", "CITI", "Citigroup Global Markets Inc.", "LOWER_UNSIGNED", "ISIN", "PB", 0),
    Alias("CITIBANK_NA_OTC", "CITI", "Citibank, N.A.", "WORD_UNSIGNED", "ISIN", "DEALER", 2),
    Alias("BARCAP_LDN", "BARC", "Barclays Capital Securities Ltd", "CODE_UNSIGNED", "SEDOL", "PB", 2),
    Alias("Barclays Capital Inc", "BARC", "Barclays Capital Inc.", "CODE_UNSIGNED", "CUSIP", "PB", 0),
    Alias("BBPLC_OTC", "BARC", "Barclays Bank PLC", "WORD_UNSIGNED", "ISIN", "DEALER", 1),
    Alias("UBS_SEC_LLC", "UBS", "UBS Securities LLC", "BLANK_SIGNED", "ISIN", "PB", 0),
    Alias("UBS_PB_STAMFORD", "UBS", "UBS Securities LLC", "CODE_SIGNED", "SEDOL", "PB", 2),
    Alias("UBS_AG_LDN", "UBS", "UBS AG London Branch", "BLANK_SIGNED", "TICKER", "DEALER", 2),
    Alias("CITADEL_SEC", "CITADEL", "Citadel Securities LLC", "LOWER_UNSIGNED", "TICKER", "PB", 1),
    Alias("Citadel Securities LLC", "CITADEL", "Citadel Securities LLC", "CODE_SIGNED", "ISIN", "PB", 0),
]


def alias_for(code: str, view: str) -> Alias:
    return rng.choice([a for a in ALIASES if a.code == code and a.view == view])


@dataclass(frozen=True)
class Instrument:
    kind: str
    ticker: str
    isin: str
    cusip: Optional[str]
    sedol: Optional[str]
    description: str
    texts: tuple
    currency: str
    coupon: Optional[D]
    maturity: Optional[date]
    ref_price: D


# ticker, name, ISIN, CUSIP, SEDOL, reference price. Meant to match real listed securities, but not
# checked against a reference data source; only their check digits are asserted below.
EQUITY_ROWS = [
    ("AAPL", "Apple Inc.", "US0378331005", "037833100", "2046251", "268.00"),
    ("MSFT", "Microsoft Corp.", "US5949181045", "594918104", "2588173", "520.00"),
    ("AMZN", "Amazon.com Inc.", "US0231351067", "023135106", "2000019", "225.00"),
    ("NVDA", "NVIDIA Corp.", "US67066G1040", "67066G104", "2379504", "190.00"),
    ("GOOGL", "Alphabet Inc. Class A", "US02079K3059", "02079K305", "BYVY8G0", "280.00"),
    ("JPM", "JPMorgan Chase & Co.", "US46625H1005", "46625H100", "2190385", "300.00"),
    ("XOM", "Exxon Mobil Corp.", "US30231G1022", "30231G102", "2326618", "115.00"),
    ("JNJ", "Johnson & Johnson", "US4781601046", "478160104", "2475833", "185.00"),
    ("V", "Visa Inc. Class A", "US92826C8394", "92826C839", "B2PZN04", "340.00"),
    ("PG", "Procter & Gamble Co.", "US7427181091", "742718109", "2704407", "150.00"),
    ("KO", "Coca-Cola Co.", "US1912161007", "191216100", "2206657", "70.00"),
    ("PFE", "Pfizer Inc.", "US7170811035", "717081103", "2684703", "25.00"),
    ("BAC", "Bank of America Corp.", "US0605051046", "060505104", "2295677", "50.00"),
    ("WMT", "Walmart Inc.", "US9311421039", "931142103", "2936921", "100.00"),
    ("HD", "Home Depot Inc.", "US4370761029", "437076102", "2434209", "390.00"),
]
# Traded only on alleged street rows, so these identifiers appear nowhere in the internal book.
ALLEGED_ONLY_EQUITY_ROWS = [
    ("DIS", "Walt Disney Co.", "US2546871060", "254687106", "2270726", "110.00"),
    ("NFLX", "Netflix Inc.", "US64110L1061", "64110L106", "2857817", "1100.00"),
]
UST_ROWS = [("91282C", "3.875", date(2027, 11, 15)), ("91282C", "4.000", date(2028, 2, 15)),
            ("91282C", "4.125", date(2029, 8, 15)), ("91282C", "3.625", date(2030, 5, 15)),
            ("91282C", "4.000", date(2031, 2, 15)), ("91282C", "4.125", date(2032, 11, 15)),
            ("91282C", "4.500", date(2033, 11, 15)), ("91282C", "4.375", date(2034, 5, 15)),
            ("91282C", "4.250", date(2035, 5, 15)), ("912810", "4.625", date(2040, 2, 15)),
            ("912810", "4.750", date(2053, 11, 15))]
CORP_ROWS = [("AAPL", "APPLE INC", "037833", "4.500", date(2036, 5, 15)),
             ("MSFT", "MICROSOFT CORP", "594918", "3.500", date(2035, 2, 15)),
             ("JPM", "JPMORGAN CHASE & CO", "46625H", "5.350", date(2034, 6, 1)),
             ("GS", "GOLDMAN SACHS GROUP INC", "38141G", "5.150", date(2032, 5, 22)),
             ("BAC", "BANK OF AMERICA CORP", "06051G", "4.950", date(2033, 7, 22)),
             ("VZ", "VERIZON COMMUNICATIONS", "92343V", "4.780", date(2035, 2, 15)),
             ("T", "AT&T INC", "00206R", "5.400", date(2034, 2, 15)),
             ("XOM", "EXXON MOBIL CORP", "30231G", "4.227", date(2040, 3, 19)),
             ("WMT", "WALMART INC", "931142", "4.100", date(2033, 4, 15)),
             ("JNJ", "JOHNSON & JOHNSON", "478160", "4.900", date(2031, 6, 1)),
             ("CMCSA", "COMCAST CORP", "20030N", "4.650", date(2033, 2, 15)),
             ("ABBV", "ABBVIE INC", "00287Y", "5.050", date(2034, 3, 15))]
FX_PAIRS = [("EUR", "USD", "1.1650", 4), ("GBP", "USD", "1.3120", 4), ("AUD", "USD", "0.6540", 4),
            ("USD", "CAD", "1.3950", 4), ("USD", "CHF", "0.8010", 4), ("EUR", "GBP", "0.8880", 4),
            ("NZD", "USD", "0.5720", 4), ("USD", "JPY", "153.40", 2)]
SWAP_CURVE = [("USD", "SOFR", 2, "3.60"), ("USD", "SOFR", 5, "3.70"), ("USD", "SOFR", 7, "3.80"),
              ("USD", "SOFR", 10, "3.95"), ("USD", "SOFR", 30, "4.05"), ("EUR", "ESTR", 5, "2.45"),
              ("EUR", "ESTR", 10, "2.75"), ("GBP", "SONIA", 5, "3.85"), ("GBP", "SONIA", 10, "4.10")]


def equity_instrument(row) -> Instrument:
    ticker, name, isin, cusip, sedol, price = row
    assert isin[-1] == isin_check(isin[:-1]), f"bad ISIN {isin}"
    assert cusip[-1] == cusip_check(cusip[:-1]), f"bad CUSIP {cusip}"
    assert sedol[-1] == sedol_check(sedol[:-1]), f"bad SEDOL {sedol}"
    assert isin[2:11] == cusip, f"ISIN and CUSIP disagree for {ticker}"
    return Instrument("EQUITY", ticker, isin, cusip, sedol, f"{name} Common Stock",
                      (f"{name.upper().rstrip('.')} COM", f"{ticker} US Equity", name),
                      "USD", None, None, D(price))


EQUITIES = [equity_instrument(r) for r in EQUITY_ROWS]
ALLEGED_EQUITIES = [equity_instrument(r) for r in ALLEGED_ONLY_EQUITY_ROWS]


def bond_ids(seed_text: str, cusip_prefix: str) -> tuple:
    cusip8 = cusip_prefix + digest(seed_text, ALNUM, 8 - len(cusip_prefix))
    cusip = cusip8 + cusip_check(cusip8)
    isin = "US" + cusip + isin_check("US" + cusip)
    sedol6 = "B" + digest(seed_text + "sedol", SEDOL_ALPHABET, 5)
    return isin, cusip, sedol6 + sedol_check(sedol6)


def short_coupon(c: D) -> str:
    return format(c.normalize(), "f")


def ust_instrument(row) -> Instrument:
    prefix, coupon, maturity = row
    c = D(coupon)
    ticker = f"T {short_coupon(c)} {maturity:%m/%d/%y}"
    isin, cusip, sedol = bond_ids(ticker, prefix)
    return Instrument("UST", ticker, isin, cusip, sedol, f"US Treasury Note {coupon}% due {maturity:%Y-%m-%d}",
                      (f"US TREASURY N/B {short_coupon(c)} {maturity:%m/%d/%y}", ticker,
                       f"UST {coupon}% {maturity:%b %Y}"), "USD", c, maturity, D(100))


def corp_instrument(row) -> Instrument:
    tick, name, prefix, coupon, maturity = row
    c = D(coupon)
    ticker = f"{tick} {short_coupon(c)} {maturity:%m/%d/%y}"
    isin, cusip, sedol = bond_ids(ticker, prefix)
    return Instrument("CORP", ticker, isin, cusip, sedol,
                      f"{name.title()} {coupon}% Senior Notes due {maturity:%Y-%m-%d}",
                      (f"{name} {short_coupon(c)} {maturity:%m/%d/%y}", ticker, f"{name.title()} {coupon}% {maturity:%Y}"),
                      "USD", c, maturity, D(98))


USTS = [ust_instrument(r) for r in UST_ROWS]
CORPS = [corp_instrument(r) for r in CORP_ROWS]
_otc_cache: dict = {}


def otc_isin(seed_text: str) -> str:
    base = "EZ" + digest(seed_text, ALNUM, 9)
    return base + isin_check(base)


def fx_instrument(pair, value: date) -> Instrument:
    base, quote, ref, _ = pair
    key = ("FX", base, quote, value)
    if key not in _otc_cache:
        ticker = f"{base}{quote} FWD {value:%d%b%y}".upper()
        _otc_cache[key] = Instrument(
            "FX_FWD", ticker, otc_isin(ticker), None, None, f"{base}/{quote} FX Forward value {value:%Y-%m-%d}",
            (f"{base}/{quote} OUTRIGHT VAL {value:%Y-%m-%d}", f"FWD {base}{quote} {value:%m/%d/%y}", ticker),
            quote, None, value, D(ref))
    return _otc_cache[key]


def irs_instrument(curve, maturity: date) -> Instrument:
    ccy, index, tenor, ref = curve
    key = ("IRS", ccy, index, maturity)
    if key not in _otc_cache:
        ticker = f"{ccy} {index} {tenor}Y {maturity:%d%b%y}".upper()
        _otc_cache[key] = Instrument(
            "IRS", ticker, otc_isin(ticker), None, None, f"{ccy} {index} OIS {tenor}Y Fixed vs Float",
            (f"{ccy} {index} {tenor}Y IRS MAT {maturity:%Y-%m-%d}", f"IRS {ccy} {tenor}Y {maturity:%m/%d/%y}", ticker),
            ccy, None, maturity, D(ref))
    return _otc_cache[key]


def fx_swap_instrument(pair, near: date, far: date) -> Instrument:
    base, quote, ref, _ = pair
    key = ("FXS", base, quote, near, far)
    if key not in _otc_cache:
        ticker = f"{base}{quote} SWAP {near:%d%b%y}/{far:%d%b%y}".upper()
        _otc_cache[key] = Instrument(
            "FX_SWAP", ticker, otc_isin(ticker), None, None, f"{base}/{quote} FX Swap {near:%Y-%m-%d} / {far:%Y-%m-%d}",
            (f"{base}/{quote} FX SWAP {near:%Y-%m-%d} {far:%Y-%m-%d}", f"FXSWAP {base}{quote} {far:%m/%d/%y}", ticker),
            quote, None, far, D(ref))
    return _otc_cache[key]


def fixed_fixed_instrument(curve, maturity: date) -> Instrument:
    ccy, index, tenor, ref = curve
    key = ("FF", ccy, tenor, maturity)
    if key not in _otc_cache:
        ticker = f"{ccy} FIX/FIX {tenor}Y {maturity:%d%b%y}".upper()
        _otc_cache[key] = Instrument(
            "FIXED_FIXED", ticker, otc_isin(ticker), None, None, f"{ccy} Fixed/Fixed Swap Package {tenor}Y",
            (f"{ccy} FIXED/FIXED {tenor}Y MAT {maturity:%Y-%m-%d}", f"PKG {ccy} {tenor}Y {maturity:%m/%d/%y}", ticker),
            ccy, None, maturity, D(ref))
    return _otc_cache[key]


# ------------------------------------------------------------------ economics
def coupon_period(ins: Instrument, settle: date) -> tuple:
    next_c = ins.maturity
    prev_c = add_raw_months(next_c, -6)
    while not prev_c <= settle < next_c:
        next_c, prev_c = prev_c, add_raw_months(prev_c, -6)
    return prev_c, next_c


def add_raw_months(d: date, months: int) -> date:
    m = d.month - 1 + months
    return date(d.year + m // 12, m % 12 + 1, d.day)


def accrued_of(ins: Instrument, qty: D, settle: date) -> D:
    prev_c, next_c = coupon_period(ins, settle)
    if ins.kind == "UST":  # Actual/Actual
        return cents(qty * ins.coupon / 100 / 2 * (settle - prev_c).days / (next_c - prev_c).days)
    days = (settle.year - prev_c.year) * 360 + (settle.month - prev_c.month) * 30 + min(settle.day, 30) - min(prev_c.day, 30)
    return cents(qty * ins.coupon / 100 * days / 360)  # 30/360


def principal_of(kind: str, qty: D, price: D) -> D:
    if kind in BONDS:
        return cents(qty * price / 100)
    if kind in ("EQUITY", "FX_FWD", "FX_SWAP"):
        return cents(qty * price)
    return cents(qty * price / 100 / 2)  # swaps: first semi-annual fixed amount


def net_of(side: str, principal: D, accrued: D, fee: D) -> D:
    return principal + accrued + fee if side in ("BUY", "PAY_FIXED") else principal + accrued - fee


def fee_of(kind: str, qty: D) -> D:
    if kind in BONDS:
        return D(rng.choice(["0.00", "12.50", "25.00", "37.50", "50.00", "75.00", "100.00", "150.00"]))
    if kind == "EQUITY":
        return cents(qty * D(rng.choice(["0.005", "0.0075", "0.01", "0.015", "0.02", "0.03"])))
    if kind in ("IRS", "FIXED_FIXED"):
        return D(rng.choice(["25.00", "50.00", "75.00", "125.00", "175.00", "250.00", "350.00"]))
    return D("0.00")


def default_price(kind: str, ins: Instrument) -> D:
    if kind == "UST":
        return D(rng.randint(94, 103)) + D(rng.randint(0, 63)) / 64
    if kind == "CORP":
        return D(rng.randrange(88000, 108000, 5)) / 1000
    if kind == "EQUITY":
        return places(ins.ref_price * (1 + D(rng.randint(-250, 250)) / 10000), rng.choice([2, 2, 4]))
    if kind == "FX_FWD":
        dp = 2 if ins.currency == "JPY" else 4
        return places(ins.ref_price * (1 + D(rng.randint(-120, 120)) / 10000), dp)
    return places(ins.ref_price + D(rng.randrange(-80, 81)) * D("0.0025"), 4)  # IRS fixed rate


def default_qty(kind: str, lot_multiple: int = 1) -> D:
    span = {"EQUITY": (5, 2000), "UST": (4, 400), "CORP": (4, 400), "FX_FWD": (5, 250), "IRS": (5, 150)}[kind]
    low, high = span
    return LOT[kind] * lot_multiple * rng.randint(max(1, low // lot_multiple), max(2, high // lot_multiple))


def encode_direction(side: str, style: str, flip: bool) -> tuple:
    effective = FLIP[side] if flip else side
    negative = effective in ("SELL", "PAY_FIXED")
    code = {"BUY": "B", "SELL": "S", "PAY_FIXED": "P", "REC_FIXED": "R"}[effective]
    word = {"BUY": "BOT", "SELL": "SLD", "PAY_FIXED": "PAY", "REC_FIXED": "REC"}[effective]
    lower = {"BUY": "buy", "SELL": "sell", "PAY_FIXED": "pay", "REC_FIXED": "rec"}[effective]
    return {"CODE_UNSIGNED": (code, 1), "BLANK_SIGNED": (None, -1 if negative else 1),
            "CODE_SIGNED": (code, -1 if negative else 1), "WORD_UNSIGNED": (word, 1),
            "LOWER_UNSIGNED": (lower, 1)}[style]


def to_32nds(price: D) -> str:
    whole = int(price)
    thirty_seconds = (price - whole) * 32
    ticks = int(thirty_seconds)
    half = thirty_seconds - ticks
    assert half in (D(0), D("0.5")), f"{price} is not a 64th"
    return f"{whole}-{ticks:02d}" + ("+" if half else "")


# ------------------------------------------------------------------ rows
@dataclass
class Internal:
    uti: str
    kind: str
    code: str
    side: str
    ins: Instrument
    trade_date: date
    settle: date
    qty: D
    price: D
    principal: D
    accrued: D
    fee: D
    net: D
    book: str
    coupon: Optional[D] = None
    parent: Optional[str] = None
    leg: str = "SINGLE"
    id: Optional[str] = None

    def row(self) -> list:
        i = self.ins
        return [self.id, self.uti, self.parent, self.leg, self.book, self.trade_date, self.settle,
                ASSET_CLASS[self.kind], self.code, LEGAL_NAME[self.code], self.side, i.ticker, i.isin, i.cusip, i.sedol,
                i.description, self.qty, self.price, i.currency, self.principal, self.accrued, self.fee, self.net,
                self.coupon, i.maturity]


@dataclass
class Street:
    ref: str
    action: str
    alias: Alias
    view: str
    trade_date: date
    value_date: date
    product: str
    dir_ind: Optional[str]
    sec_id: str
    sec_src: str
    sec_text: str
    qty_signed: D
    exec_price: object
    quote: str
    ccy: str
    accrued: D
    net: D
    coupon: Optional[D]
    maturity: Optional[date]
    canon_qty: D
    canon_price: D
    cancels: Optional["Street"] = None
    id: Optional[str] = None

    def row(self) -> list:
        return [self.id, self.ref, self.action, self.cancels.id if self.cancels else None, self.alias.name,
                self.alias.lei, "DEALER_BILATERAL_VIEW" if self.view == "DEALER" else "PB_CLIENT_VIEW", self.trade_date,
                self.value_date, self.product, self.dir_ind, self.sec_id, self.sec_src, self.sec_text, self.qty_signed,
                self.exec_price, self.quote, self.ccy, self.accrued, self.net, self.coupon, self.maturity]


used_keys: set = set()
used_refs: set = set()


def new_ref() -> str:
    while True:
        ref = "UTI" + f"{rng.getrandbits(80):020X}"
        if ref not in used_refs:
            used_refs.add(ref)
            return ref


def instrument_for(kind: str, td: date, fx_filter=None):
    """Return the instrument and settlement date a trade of this kind would have on this trade date."""
    if kind == "EQUITY":
        return rng.choice(EQUITIES), add_bdays(td, 1)
    if kind == "UST":
        return rng.choice(USTS), add_bdays(td, 1)
    if kind == "CORP":
        return rng.choice(CORPS), add_bdays(td, 1)
    if kind == "FX_FWD":
        pairs = [p for p in FX_PAIRS if fx_filter is None or fx_filter(p)]
        value = add_months(add_bdays(td, 2), rng.choice([1, 2, 3, 6, 9, 12]))
        return fx_instrument(rng.choice(pairs), value), value
    if kind == "IRS":
        curve = rng.choice(SWAP_CURVE)
        effective = add_bdays(td, 2)
        return irs_instrument(curve, add_months(effective, curve[2] * 12)), effective
    raise ValueError(kind)


def make_trade(kind, code, side, ins, td, settle, qty, price, book=None, uti=None, fee=None) -> Internal:
    principal = principal_of(kind, qty, price)
    accrued = accrued_of(ins, qty, settle) if kind in BONDS else D("0.00")
    fee = fee_of(kind, qty) if fee is None else fee
    coupon = ins.coupon if kind in BONDS else (price if kind in ("IRS", "FIXED_FIXED") else None)
    return Internal(uti or new_ref(), kind, code, side, ins, td, settle, qty, price, principal, accrued, fee,
                    net_of(side, principal, accrued, fee), book or rng.choice(BOOKS), coupon)


def new_trade(kind, side=None, qty=None, price=None, code=None, ins=None, fx_filter=None, lot_multiple=1) -> Internal:
    for _ in range(20000):
        td = rng.choice(TRADE_DATES)
        c = code or rng.choice(ELIGIBLE[kind])
        i, settle = instrument_for(kind, td, fx_filter)
        if ins is not None:
            i = ins
        s = side or rng.choice(SIDES[kind])
        key = (c, td, i.isin, s)
        if key not in used_keys:
            used_keys.add(key)
            q = qty if qty is not None else default_qty(kind, lot_multiple)
            p = price if price is not None else default_price(kind, i)
            return make_trade(kind, c, s, i, td, settle, q, p)
    raise RuntimeError("could not find a free trade key")


def pick_security_id(ins: Instrument, alias: Alias) -> tuple:
    value = {"ISIN": ins.isin, "CUSIP": ins.cusip, "SEDOL": ins.sedol, "TICKER": ins.ticker}[alias.id_pref]
    return (value, alias.id_pref) if value else (ins.isin, "ISIN")


def build_street(t: Internal, *, ref, view="PB", quote_mode="PLAIN", qty=None, price=None, value_date=None, accrued=None,
                 net=None, action="NEW", alias=None, side=None, ins=None, kind=None, coupon="same", exec_override=None,
                 quote_override=None) -> Street:
    alias = alias or alias_for(t.code, view)
    ins = ins or t.ins
    kind = kind or t.kind
    dir_ind, sign = encode_direction(side or t.side, alias.style, view == "DEALER")
    q = t.qty if qty is None else qty
    p = t.price if price is None else price
    a = t.accrued if accrued is None else accrued
    if exec_override is not None:
        exec_price, quote = exec_override, quote_override
    elif quote_mode == "32NDS":
        exec_price, quote = to_32nds(p), "32NDS_UST"
    elif quote_mode == "DIRTY":
        exec_price, quote = places(p + a / q * 100, 8), "DIRTY_PCT"
    else:
        exec_price, quote = p, PLAIN_QUOTE[kind]
    sec_id, sec_src = pick_security_id(ins, alias)
    return Street(ref, action, alias, view, t.trade_date, value_date or t.settle, PRODUCT[kind][alias.dialect], dir_ind,
                  sec_id, sec_src, ins.texts[alias.dialect], q * sign, exec_price, quote, ins.currency, a,
                  t.net if net is None else net, t.coupon if coupon == "same" else coupon, ins.maturity, q, p)


def differences(t: Internal, s: Street) -> list:
    out = []
    for internal_field, cpty_field, left, right in (
            ("Settle_Date", "Value_Settle_Date", t.settle, s.value_date),
            ("Quantity_Notional", "Reported_Qty_Notional", t.qty, s.canon_qty),
            ("Clean_Price_Rate", "Exec_Price_Rate", t.price, s.canon_price),
            ("Accrued_Interest", "Reported_Accrued_Int", t.accrued, s.accrued),
            ("Net_Settlement_Amount", "Net_Cash_Amount", t.net, s.net)):
        if left != right:
            if isinstance(left, date):
                out.append({"internal_field": internal_field, "counterparty_field": cpty_field,
                            "internal_value": left.isoformat(), "counterparty_value": right.isoformat(),
                            "abs_diff_days": abs((left - right).days)})
            else:
                out.append({"internal_field": internal_field, "counterparty_field": cpty_field,
                            "internal_value": float(left), "counterparty_value": float(right),
                            "abs_diff": float(abs(left - right))})
    return out


def shuffled_flags(total: int, true_count: int) -> list:
    flags = [True] * true_count + [False] * (total - true_count)
    rng.shuffle(flags)
    return flags


def non_jpy(pair) -> bool:
    return "JPY" not in pair[:2]


# ------------------------------------------------------------------ scenarios
def build_dataset():
    internals: list = []
    streets: list = []

    # ---- 120 known exact pairs
    known = []
    specs = [("UST", "32NDS", 12, "PB", 6), ("CORP", "DIRTY", 8, "PB", 4), ("CORP", "PLAIN", 14, "PB", 7),
             ("UST", "PLAIN", 10, "PB", 5), ("EQUITY", "PLAIN", 40, "PB", 20), ("FX_FWD", "PLAIN", 8, "DEALER", 4),
             ("FX_FWD", "PLAIN", 10, "PB", 5), ("IRS", "PLAIN", 7, "DEALER", 4), ("IRS", "PLAIN", 11, "PB", 5)]
    # (whole, 64ths). The first two reproduce the spec's examples: 98-24 and 98-11+.
    ticks_32nds = [(98, 48), (98, 23), (99, 14), (101, 7), (100, 0), (97, 31), (102, 40), (96, 17), (103, 2), (95, 59),
                   (99, 33), (100, 26)]
    for kind, mode, n, view, share_count in specs:
        for i, shared in enumerate(shuffled_flags(n, share_count)):
            side = None
            if view == "DEALER":  # mostly an internal BUY or PAY_FIXED, as in the spec's description
                side = rng.choices(SIDES[kind], [7, 3])[0]
            price = None
            if mode == "32NDS":
                whole, k = ticks_32nds[i]
                price = D(whole) + D(k) / 64
            t = new_trade(kind, side=side, price=price)
            s = build_street(t, ref=t.uti if shared else new_ref(), view=view, quote_mode=mode)
            assert not differences(t, s), "known exact pair must have no differences"
            tests = ["CPTY_ALIAS", f"DIR_{s.alias.style}", f"ID_{s.sec_src}"]
            if mode == "32NDS":
                tests.append("PRICE_32NDS")
            if mode == "DIRTY":
                tests.append("PRICE_DIRTY")
            if view == "DEALER":
                tests.append("DEALER_INVERSION")
            internals.append(t)
            streets.append(s)
            known.append({"t": t, "s": s, "basis": "SHARED_EXEC_REF" if shared else "NATURAL_KEY", "tests": tests})

    def street_view(kind):
        return "DEALER" if kind in ("FX_FWD", "IRS") and rng.random() < 0.35 else "PB"

    # ---- 18 within-tolerance near matches: 6 accrued, 6 FX rate, 6 fee rounding
    near = []
    for tolerance_type, kinds in (("ACCRUED_INTEREST", ["UST", "CORP", "UST", "CORP", "CORP", "UST"]),
                                  ("FX_RATE_4TH_DECIMAL", ["FX_FWD"] * 6),
                                  ("FEE_ROUNDING", ["EQUITY"] * 6)):
        for kind, shared in zip(kinds, shuffled_flags(6, 3)):
            t = new_trade(kind, fx_filter=non_jpy if kind == "FX_FWD" else None)
            ref = t.uti if shared else new_ref()
            view = street_view(kind)
            if tolerance_type == "ACCRUED_INTEREST":
                delta = D(rng.randint(40, 120)) / 100 * rng.choice([1, -1])
                s = build_street(t, ref=ref, view=view, accrued=t.accrued + delta, net=t.net + delta)
            elif tolerance_type == "FX_RATE_4TH_DECIMAL":
                delta = D(rng.choice([1, 2, 3])) * D("0.0001") * rng.choice([1, -1])
                new_price = t.price + delta
                s = build_street(t, ref=ref, view=view, price=new_price, net=principal_of(kind, t.qty, new_price))
            else:
                delta = D(rng.randint(1, 5)) / 100 * rng.choice([1, -1])
                s = build_street(t, ref=ref, view=view, net=t.net + delta)
            internals.append(t)
            streets.append(s)
            near.append({"t": t, "s": s, "type": tolerance_type})

    # ---- 20 true economic breaks: 5 of each type
    breaks = []
    for break_type, kinds in (("PRICE_BUST", ["EQUITY", "CORP", "UST", "FX_FWD", "EQUITY"]),
                              ("PARTIAL_FILL_80_PCT", ["EQUITY", "CORP", "EQUITY", "UST", "FX_FWD"]),
                              ("SETTLE_DATE_LAG_T1_T2", ["EQUITY", "EQUITY", "EQUITY", "IRS", "IRS"]),
                              ("COMMISSION_450", ["EQUITY", "CORP", "EQUITY", "UST", "IRS"])):
        for kind, shared in zip(kinds, shuffled_flags(5, 2 if break_type in ("PRICE_BUST", "COMMISSION_450") else 3)):
            t = new_trade(kind, lot_multiple=5 if break_type == "PARTIAL_FILL_80_PCT" else 1)
            ref = t.uti if shared else new_ref()
            view = street_view(kind)
            if break_type == "PRICE_BUST":
                move = D(rng.randint(75, 250)) / 10000 * rng.choice([1, -1])
                dp = 2 if kind == "EQUITY" else (4 if kind == "FX_FWD" and t.ins.currency != "JPY" else 3)
                new_price = places(t.price * (1 + move), dp)
                s = build_street(t, ref=ref, view=view, price=new_price,
                                 net=net_of(t.side, principal_of(kind, t.qty, new_price), t.accrued, t.fee))
            elif break_type == "PARTIAL_FILL_80_PCT":
                new_qty = t.qty * 4 / 5
                assert new_qty % LOT[kind] == 0, "80% fill must stay on a lot boundary"
                new_accrued = accrued_of(t.ins, new_qty, t.settle) if kind in BONDS else D("0.00")
                new_fee = cents(t.fee * 4 / 5)
                s = build_street(t, ref=ref, view=view, qty=new_qty, accrued=new_accrued,
                                 net=net_of(t.side, principal_of(kind, new_qty, t.price), new_accrued, new_fee))
            elif break_type == "SETTLE_DATE_LAG_T1_T2":
                other = add_bdays(t.trade_date, 2) if kind == "EQUITY" else add_bdays(t.trade_date, 1)
                s = build_street(t, ref=ref, view=view, value_date=other)
            else:
                delta = D(450) if t.side in ("BUY", "PAY_FIXED") else D(-450)
                s = build_street(t, ref=ref, view=view, net=t.net + delta)
            internals.append(t)
            streets.append(s)
            breaks.append({"t": t, "s": s, "type": break_type})

    # ---- 12 N-to-1 allocation groups: 3 sub-fund rows to 1 prime broker block
    allocations = []
    alloc_kinds = ["EQUITY"] * 6 + ["UST"] * 3 + ["CORP"] * 3
    rng.shuffle(alloc_kinds)
    for kind, shared in zip(alloc_kinds, shuffled_flags(12, 6)):
        lots = rng.randint(50, 2000) if kind == "EQUITY" else rng.randint(20, 400)
        total_qty = LOT[kind] * lots
        if kind == "EQUITY":
            ins_probe = rng.choice(EQUITIES)
            price = places(ins_probe.ref_price * (1 + D(rng.randint(-250, 250)) / 10000), 2)
        elif kind == "UST":
            ins_probe = rng.choice(USTS)
            price = D(rng.randint(94, 103)) + D(rng.randrange(0, 8)) / 8
        else:
            ins_probe = rng.choice(CORPS)
            price = D(rng.randrange(88000, 108000, 5)) / 1000
        block = new_trade(kind, qty=total_qty, price=price, ins=ins_probe)
        weights = rng.choice([(50, 30, 20), (40, 35, 25), (60, 25, 15), (45, 30, 25)])
        lot_splits = [lots * weights[0] // 100, lots * weights[1] // 100]
        lot_splits.append(lots - sum(lot_splits))
        books = ALLOCATION_BOOKS[:]
        rng.shuffle(books)
        block_ref = new_ref()
        rows = []
        accrued_left, fee_left = block.accrued, block.fee
        for j, (split, book) in enumerate(zip(lot_splits, books)):
            q = LOT[kind] * split
            principal = principal_of(kind, q, price)
            assert principal == (q * price / 100 if kind in BONDS else q * price), "allocation principal must be exact"
            if j < 2:
                a, f = cents(block.accrued * split / lots), cents(block.fee * split / lots)
            else:  # the last allocation carries the rounding plug, as an OMS would
                a, f = accrued_left, fee_left
            accrued_left -= a
            fee_left -= f
            coupon = block.ins.coupon if kind in BONDS else None
            rows.append(Internal(block_ref if shared else new_ref(), kind, block.code, block.side, block.ins,
                                 block.trade_date, block.settle, q, price, principal, a, f,
                                 net_of(block.side, principal, a, f), book, coupon))
        assert sum(r.qty for r in rows) == block.qty and sum(r.net for r in rows) == block.net
        s = build_street(block, ref=block_ref, view="PB")
        internals.extend(rows)
        streets.append(s)
        allocations.append({"rows": rows, "s": s, "shared": shared})

    # ---- 8 multi-leg trades: 2 linked internal rows to 1 combined net street row
    multileg = []
    used_packages = set()

    def free_package_key(code, td, isin):
        key = (code, td, isin)
        if key in used_packages:
            return False
        used_packages.add(key)
        return True

    leg_plan = [("FX_SWAP", s) for s in shuffled_flags(4, 2)] + [("FIXED_FIXED", s) for s in shuffled_flags(4, 2)]
    for n, (kind, shared) in enumerate(leg_plan):
        parent = f"PKG-{rng.randint(100000, 999999)}"
        ref_single = new_ref()
        view = "DEALER" if n % 4 == 3 else "PB"
        while True:
            td, code = rng.choice(TRADE_DATES), rng.choice(ELIGIBLE[kind])
            if kind == "FX_SWAP":
                pair = rng.choice(FX_PAIRS)
                near_date = add_bdays(td, 2)
                far_date = add_months(near_date, rng.choice([1, 3, 6]))
                ins = fx_swap_instrument(pair, near_date, far_date)
            else:
                curve = rng.choice([c for c in SWAP_CURVE if c[2] >= 5])
                effective = add_bdays(td, 2)
                ins = fixed_fixed_instrument(curve, add_months(effective, curve[2] * 12))
            if free_package_key(code, td, ins.isin):
                break
        leg1_uti = ref_single if shared else new_ref()
        leg2_uti = ref_single if shared else new_ref()
        street_ref = ref_single if shared else new_ref()
        if kind == "FX_SWAP":
            dp = 2 if pair[1] == "JPY" else 4
            near_rate = places(D(pair[2]) * (1 + D(rng.randint(-100, 100)) / 10000), dp)
            points = (D(rng.randint(10, 150)) / 100 if dp == 2 else D(rng.randint(10, 100)) / 10000) * rng.choice([1, -1])
            far_rate = near_rate + points
            qty = LOT["FX_FWD"] * rng.randint(10, 150)
            leg1 = make_trade("FX_SWAP", code, "BUY", ins, td, near_date, qty, near_rate, uti=leg1_uti, fee=D("0.00"))
            leg2 = make_trade("FX_SWAP", code, "SELL", ins, td, far_date, qty, far_rate, uti=leg2_uti, fee=D("0.00"))
            exec_price, quote = far_rate - near_rate, "FX_SWAP_POINTS"
            value_date, coupon, rule = far_date, None, "FX_SWAP_NET"
        else:
            rate1 = places(ins.ref_price + D(rng.randrange(-40, 41)) * D("0.0025"), 4)
            spread = D(rng.randrange(4, 40)) * D("0.0125") * rng.choice([1, -1])
            rate2 = rate1 + spread
            qty = LOT["IRS"] * rng.randint(10, 120)
            leg1 = make_trade("FIXED_FIXED", code, "PAY_FIXED", ins, td, effective, qty, rate1, uti=leg1_uti)
            leg2 = make_trade("FIXED_FIXED", code, "REC_FIXED", ins, td, effective, qty, rate2, uti=leg2_uti)
            exec_price, quote = abs(rate1 - rate2), "NET_FIXED_SPREAD_PCT"
            value_date, coupon, rule = effective, abs(rate1 - rate2), "FIXED_FIXED_NET"
        leg1.parent = leg2.parent = parent
        leg1.leg, leg2.leg = "LEG_1_PAY", "LEG_2_RECEIVE"
        net_cash = abs(leg1.net - leg2.net)
        net_side = "PAY_FIXED" if leg1.net > leg2.net else "REC_FIXED"
        s = build_street(leg1, ref=street_ref, view=view, side=net_side, net=net_cash, accrued=D("0.00"),
                         value_date=value_date, coupon=coupon, exec_override=exec_price, quote_override=quote)
        internals.extend([leg1, leg2])
        streets.append(s)
        multileg.append({"legs": [leg1, leg2], "s": s, "rule": rule, "net_side": net_side, "shared": shared})

    # ---- 5 cancel and correct chains: NEW (busted), CANC, CORR
    chains = []
    for kind, bust, shared in (("EQUITY", "PRICE", True), ("CORP", "QUANTITY", False), ("FX_FWD", "PRICE", True),
                               ("IRS", "QUANTITY", False), ("UST", "PRICE", True)):
        t = new_trade(kind, fx_filter=non_jpy if kind == "FX_FWD" else None)
        alias = alias_for(t.code, "PB")
        ref = t.uti if shared else new_ref()
        if bust == "PRICE":
            dp = {"EQUITY": 2, "UST": 3, "FX_FWD": 4}[kind]
            bad_price = places(t.price * (1 + D(rng.choice([150, -150, 225, -225])) / 10000), dp)
            bad = dict(price=bad_price, net=net_of(t.side, principal_of(kind, t.qty, bad_price), t.accrued, t.fee))
        else:
            bad_qty = t.qty + LOT[kind] * rng.randint(2, 4)
            bad_accrued = accrued_of(t.ins, bad_qty, t.settle) if kind in BONDS else D("0.00")
            bad = dict(qty=bad_qty, accrued=bad_accrued,
                       net=net_of(t.side, principal_of(kind, bad_qty, t.price), bad_accrued, t.fee))
        new_row = build_street(t, ref=ref, action="NEW", alias=alias, **bad)
        canc_row = build_street(t, ref=ref, action="CANC", alias=alias, **bad)
        canc_row.cancels = new_row
        corr_row = build_street(t, ref=ref, action="CORR", alias=alias)
        internals.append(t)
        streets.extend([new_row, canc_row, corr_row])
        chains.append({"t": t, "new": new_row, "canc": canc_row, "corr": corr_row, "bust": bust})

    # ---- 85 unmatched internal orphans, 15 of them deliberately resembling a known exact trade
    orphans = []
    exact_trades = [k["t"] for k in known]
    cash_trades = [t for t in exact_trades if t.kind in ("EQUITY", "UST", "CORP")]

    def clone(t, code=None, td=None, qty=None, price=None):
        settle = t.settle if td is None else add_bdays(td, 1)
        return make_trade(t.kind, code or t.code, t.side, t.ins, td or t.trade_date, settle, qty or t.qty, price or t.price)

    for t in rng.sample(exact_trades, 5):  # same counterparty, date, security and side; different size and price
        moved = places(t.price * D("1.005"), 4)
        orphans.append({"t": clone(t, qty=t.qty * 2, price=moved), "trap": "SAME_KEY_DIFFERENT_QTY_AND_PRICE", "of": t})
    picked = 0
    for t in rng.sample(exact_trades, len(exact_trades)):  # same economics, different counterparty
        if picked == 5:
            break
        others = [c for c in ELIGIBLE[t.kind] if c != t.code and (c, t.trade_date, t.ins.isin, t.side) not in used_keys]
        if others:
            code = rng.choice(others)
            used_keys.add((code, t.trade_date, t.ins.isin, t.side))
            orphans.append({"t": clone(t, code=code), "trap": "SAME_ECONOMICS_DIFFERENT_COUNTERPARTY", "of": t})
            picked += 1
    picked = 0
    for t in rng.sample(cash_trades, len(cash_trades)):  # same economics, different trade date
        if picked == 5:
            break
        dates = [d for d in TRADE_DATES if d != t.trade_date and (t.code, d, t.ins.isin, t.side) not in used_keys]
        if dates:
            td = rng.choice(dates)
            used_keys.add((t.code, td, t.ins.isin, t.side))
            orphans.append({"t": clone(t, td=td), "trap": "SAME_ECONOMICS_DIFFERENT_TRADE_DATE", "of": t})
            picked += 1
    for kind, n in (("EQUITY", 24), ("UST", 12), ("CORP", 14), ("FX_FWD", 10), ("IRS", 10)):
        orphans.extend({"t": new_trade(kind), "trap": None, "of": None} for _ in range(n))
    internals.extend(o["t"] for o in orphans)
    for o in orphans:
        o["reason"] = rng.choices(["UNCONFIRMED", "DK"], [7, 3])[0]

    # ---- 7 unmatched counterparty alleged trades
    alleged = []
    for kind, ins, note in (("EQUITY", ALLEGED_EQUITIES[0], "SECURITY_NOT_IN_INTERNAL_BOOK"),
                            ("EQUITY", ALLEGED_EQUITIES[1], "SECURITY_NOT_IN_INTERNAL_BOOK"),
                            ("EQUITY", None, "NO_INTERNAL_TRADE"), ("UST", None, "NO_INTERNAL_TRADE"),
                            ("CORP", None, "NO_INTERNAL_TRADE"), ("FX_FWD", None, "NO_INTERNAL_TRADE"),
                            ("IRS", None, "NO_INTERNAL_TRADE")):
        phantom = new_trade(kind, ins=ins)  # never added to the internal book
        s = build_street(phantom, ref=new_ref(), view=street_view(kind))
        streets.append(s)
        alleged.append({"s": s, "note": note})

    # ---- row counts, then shuffle and assign IDs so that IDs carry no scenario information
    assert len(internals) == 300, f"Internal rows: {len(internals)}"
    assert len(streets) == 200, f"Counterparty rows: {len(streets)}"
    assert len(known) == 120, f"Known exact pairs: {len(known)}"
    rng.shuffle(internals)
    rng.shuffle(streets)
    rng.shuffle(known)
    for row, n in zip(internals, rng.sample(range(100000, 1000000), 300)):
        row.id = f"IT-{n}"
    for row, n in zip(streets, rng.sample(range(10000000, 100000000), 200)):
        row.id = f"CR-{n}"
    for c in chains:  # within a chain, IDs rise in lifecycle order, as a real feed's would
        ids = sorted(x.id for x in (c["new"], c["canc"], c["corr"]))
        c["new"].id, c["canc"].id, c["corr"].id = ids

    return dict(internals=internals, streets=streets, known=known, near=near, breaks=breaks, allocations=allocations,
                multileg=multileg, chains=chains, orphans=orphans, alleged=alleged)


# ------------------------------------------------------------------ oracle
def build_oracle(ds: dict) -> dict:
    known = [{"pair_no": i, "internal_trade_id": k["t"].id, "cpty_report_id": k["s"].id,
              "asset_class": ASSET_CLASS[k["t"].kind], "match_basis": k["basis"], "normalization_tests": k["tests"],
              "expected_status": "MATCHED_EXACT"} for i, k in enumerate(ds["known"], 1)]
    near = [{"internal_trade_id": x["t"].id, "cpty_report_id": x["s"].id, "asset_class": ASSET_CLASS[x["t"].kind],
             "tolerance_type": x["type"], "shares_exec_ref": x["t"].uti == x["s"].ref,
             "differences": differences(x["t"], x["s"]), "expected_status": "MATCHED_WITHIN_TOLERANCE"}
            for x in ds["near"]]
    breaks = [{"internal_trade_id": x["t"].id, "cpty_report_id": x["s"].id, "asset_class": ASSET_CLASS[x["t"].kind],
               "break_type": x["type"], "shares_exec_ref": x["t"].uti == x["s"].ref,
               "differences": differences(x["t"], x["s"]), "expected_status": "BREAK"} for x in ds["breaks"]]
    allocations = [{"group_no": i, "internal_trade_ids": [r.id for r in x["rows"]], "cpty_report_id": x["s"].id,
                    "asset_class": ASSET_CLASS[x["rows"][0].kind], "books": [r.book for r in x["rows"]],
                    "allocated_quantities": [float(r.qty) for r in x["rows"]], "block_quantity": float(x["s"].canon_qty),
                    "block_net_cash": float(x["s"].net), "shares_exec_ref": x["shared"],
                    "expected_status": "MATCHED_N_TO_1"} for i, x in enumerate(ds["allocations"], 1)]
    multileg = [{"group_no": i, "parent_link_id": x["legs"][0].parent,
                 "internal_trade_ids": [r.id for r in x["legs"]], "cpty_report_id": x["s"].id,
                 "asset_class": ASSET_CLASS[x["legs"][0].kind], "net_rule": x["rule"],
                 "net_direction": "PAY" if x["net_side"] == "PAY_FIXED" else "REC",
                 "net_cash": float(x["s"].net), "notional": float(x["s"].canon_qty), "shares_exec_ref": x["shared"],
                 "expected_status": "MATCHED_MULTI_LEG"} for i, x in enumerate(ds["multileg"], 1)]
    chains = [{"internal_trade_id": x["t"].id, "new_report_id": x["new"].id, "canc_report_id": x["canc"].id,
               "corr_report_id": x["corr"].id, "asset_class": ASSET_CLASS[x["t"].kind], "busted_field": x["bust"],
               "matching_report_id": x["corr"].id, "shares_exec_ref": x["t"].uti == x["corr"].ref,
               "expected_status": "MATCHED_AFTER_LIFECYCLE_COLLAPSE"} for x in ds["chains"]]
    orphans = [{"internal_trade_id": x["t"].id, "asset_class": ASSET_CLASS[x["t"].kind], "reason": x["reason"],
                "decoy_type": x["trap"], "resembles_internal_trade_id": x["of"].id if x["of"] else None,
                "expected_status": "UNMATCHED_INTERNAL"} for x in ds["orphans"]]
    alleged = [{"cpty_report_id": x["s"].id, "product_type": x["s"].product, "note": x["note"],
                "expected_status": "UNMATCHED_COUNTERPARTY"} for x in ds["alleged"]]

    leis = {}
    for a in ALIASES:
        leis[a.lei] = {"counterparty_code": a.code, "legal_entity": a.entity}
    products = {}
    for kind, names in PRODUCT.items():
        for name in names:
            products[name] = ASSET_CLASS[kind]

    return {
        "meta": {
            "dataset": "RecDiffy.NET trade reconciliation test dataset",
            "generator_seed": SEED,
            "generated_for_as_of_date": "2026-11-04",
            "trade_dates": [d.isoformat() for d in TRADE_DATES],
            "synthetic_data_note": ("All trades are synthetic and prices are illustrative. Equity tickers and identifiers "
                                    "aim to match real listed securities but are not reference data. LEIs and bond, FX "
                                    "and swap identifiers are made up. All identifiers pass their check-digit rules."),
            "row_counts": {"Internal_Trades": 300, "Counterparty_Trades": 200, "Known_Exact_Matches": 120,
                           "blind_internal": 180, "blind_counterparty": 80},
        },
        "economic_conventions": {
            "principal": {"Bond": "quantity * clean price / 100", "Equity": "quantity * price",
                          "Forward": "base notional * rate, in the quote currency",
                          "Swap": "notional * fixed rate / 100 / 2 (first semi-annual fixed amount)"},
            "net_settlement": ("principal + accrued + fees when we pay (BUY, PAY_FIXED); principal + accrued - fees when "
                               "we receive (SELL, REC_FIXED). Cash amounts are unsigned on both tabs."),
            "accrued_interest": "Bonds only. US Treasuries Actual/Actual, corporates 30/360, to the settlement date.",
            "commission": "Tab 2 has no commission column, so fees show only in Net_Cash_Amount.",
            "settlement": "Equities and bonds T+1. FX forwards on the value date. Swaps effective T+2.",
            "allocations": ("Allocated principal is exact. Accrued and fees are pro rata, with the last allocation "
                            "carrying any rounding, so quantity and net cash sum exactly to the block."),
            "multi_leg_rules": {
                "FX_SWAP_NET": ("LEG_1_PAY buys base on the near date; LEG_2_RECEIVE sells it on the far date. Street "
                                "row: notional = leg notional, Exec_Price_Rate = far rate - near rate, Net_Cash_Amount "
                                "= |far amount - near amount|, Value_Settle_Date = far date, direction = PAY if the near "
                                "amount is larger, otherwise REC."),
                "FIXED_FIXED_NET": ("LEG_1_PAY pays one fixed rate; LEG_2_RECEIVE receives another. Street row: notional "
                                    "= leg notional, Exec_Price_Rate and Coupon = |rate difference|, Net_Cash_Amount = "
                                    "|leg 1 net - leg 2 net|, direction = PAY if leg 1 net is larger, otherwise REC. "
                                    "Fixed/fixed is used because the Side column has no floating-rate value."),
            },
        },
        "normalization_reference": {
            "counterparty_aliases": {a.name: a.code for a in ALIASES},
            "counterparty_lei": leis,
            "direction_codes": {"B": "BUY", "BOT": "BUY", "buy": "BUY", "S": "SELL", "SLD": "SELL", "sell": "SELL",
                                "P": "PAY_FIXED", "PAY": "PAY_FIXED", "pay": "PAY_FIXED", "R": "REC_FIXED",
                                "REC": "REC_FIXED", "rec": "REC_FIXED"},
            "blank_direction_rule": ("When Dir_Ind is blank, a negative quantity means SELL or PAY and a positive "
                                     "quantity means BUY or REC. Swap products and FX_SWAP net rows use PAY and REC."),
            "dealer_view_rule": ("DEALER_BILATERAL_VIEW rows state the dealer's side, so invert direction to get our "
                                 "side. PB_CLIENT_VIEW rows already state our side."),
            "product_types": products,
            "price_quote_conventions": {
                "DECIMAL_CLEAN_PCT": "Decimal clean price, percent of par.",
                "32NDS_UST": "WHOLE-TT or WHOLE-TT+ where TT is 32nds and + adds 1/64. 98-24 = 98.75; 98-11+ = 98.359375.",
                "DIRTY_PCT": "Dirty price. Clean = dirty - Reported_Accrued_Int / abs(quantity) * 100; round to 6 decimals.",
                "PER_SHARE": "Price per share.",
                "FX_OUTRIGHT": "Outright forward rate.",
                "FIXED_RATE_PCT": "Swap fixed rate, percent.",
                "FX_SWAP_POINTS": "Far rate minus near rate.",
                "NET_FIXED_SPREAD_PCT": "Absolute difference between the two fixed rates, percent.",
            },
            "security_lookup": ("Primary_Security_ID resolves against the Tab 1 column named by Security_ID_Source. FX "
                                "and swap rows have no CUSIP or SEDOL, so those brokers fall back to ISIN."),
            "field_mapping": {"Trade_Date": "Trade_Date", "Settle_Date": "Value_Settle_Date",
                              "Quantity_Notional": "Reported_Qty_Notional (absolute value)",
                              "Clean_Price_Rate": "Exec_Price_Rate (after quote convention)", "Currency": "Trade_Ccy",
                              "Accrued_Interest": "Reported_Accrued_Int", "Net_Settlement_Amount": "Net_Cash_Amount",
                              "Coupon_Rate": "Coupon", "Maturity_Date": "Maturity"},
        },
        "tolerances": {
            "accrued_interest_abs": 1.25,
            "accrued_interest_note": "Net_Cash_Amount moves by the same amount as accrued.",
            "fx_rate_abs": 0.0005,
            "fx_rate_note": "Net_Cash_Amount moves by notional * rate difference and should not break on its own.",
            "fee_rounding_net_cash_abs": 0.05,
        },
        "known_exact_matches": known,
        "blind_scenarios": {
            "within_tolerance_near_matches": near,
            "true_economic_breaks": breaks,
            "n_to_1_allocations": allocations,
            "multi_leg_representations": multileg,
            "cancel_correct_chains": chains,
            "unmatched_internal_orphans": orphans,
            "unmatched_counterparty_alleged": alleged,
        },
        "expected_counts": {
            "within_tolerance_near_matches": {"internal": len(near), "counterparty": len(near)},
            "true_economic_breaks": {"internal": len(breaks), "counterparty": len(breaks)},
            "n_to_1_allocations": {"internal": 3 * len(allocations), "counterparty": len(allocations)},
            "multi_leg_representations": {"internal": 2 * len(multileg), "counterparty": len(multileg)},
            "cancel_correct_chains": {"internal": len(chains), "counterparty": 3 * len(chains)},
            "unmatched_internal_orphans": {"internal": len(orphans), "counterparty": 0},
            "unmatched_counterparty_alleged": {"internal": 0, "counterparty": len(alleged)},
        },
    }


# ------------------------------------------------------------------ workbook
ARIAL = Font(name="Arial", size=10)
ARIAL_BOLD = Font(name="Arial", size=10, bold=True)
HEADER_FONT = Font(name="Arial", size=10, bold=True, color="FFFFFF")
HEADER_FILL = PatternFill("solid", fgColor="1F3864")
SECTION_FILL = PatternFill("solid", fgColor="D9E1F2")
DATE_FMT = "yyyy-mm-dd"
MONEY_FMT = "#,##0.00"
PRICE_FMT = "0.00######"

TAB1_FORMATS = {"Trade_Date": DATE_FMT, "Settle_Date": DATE_FMT, "Maturity_Date": DATE_FMT, "Quantity_Notional": "#,##0",
                "Clean_Price_Rate": PRICE_FMT, "Principal_Gross": MONEY_FMT, "Accrued_Interest": MONEY_FMT,
                "Commission_Fees": MONEY_FMT, "Net_Settlement_Amount": MONEY_FMT, "Coupon_Rate": "0.000#"}
TAB2_FORMATS = {"Trade_Date": DATE_FMT, "Value_Settle_Date": DATE_FMT, "Maturity": DATE_FMT,
                "Reported_Qty_Notional": "#,##0;-#,##0", "Reported_Accrued_Int": MONEY_FMT, "Net_Cash_Amount": MONEY_FMT,
                "Coupon": "0.000#"}


def write_table(ws, headers: list, rows: list, formats: dict) -> None:
    ws.append(headers)
    for row in rows:
        ws.append(row)
    for col, name in enumerate(headers, 1):
        cell = ws.cell(row=1, column=col)
        cell.font, cell.fill = HEADER_FONT, HEADER_FILL
        cell.alignment = Alignment(horizontal="center", vertical="center")
        fmt = formats.get(name)
        width = len(name)
        for r in range(2, ws.max_row + 1):
            c = ws.cell(row=r, column=col)
            c.font = ARIAL
            if isinstance(c.value, str):
                c.number_format = "@"  # keeps IDs and 32nds strings as text
            elif fmt:
                c.number_format = fmt
            elif name == "Exec_Price_Rate":
                c.number_format = PRICE_FMT
            if c.value is not None:
                width = max(width, min(len(str(c.value)), 44))
        ws.column_dimensions[get_column_letter(col)].width = width + 3
    ws.freeze_panes = "A2"
    ws.auto_filter.ref = f"A1:{get_column_letter(len(headers))}{ws.max_row}"


NOTES = [
    "All trades are synthetic test data, and prices are illustrative.",
    "Equity tickers and identifiers aim to match real listed securities but are not reference data.",
    "LEIs and bond, FX and swap identifiers are made up. All identifiers pass their check-digit rules.",
    "Bond principal = quantity x clean price / 100. Equity principal = quantity x price.",
    "FX forward principal = base notional x rate, in the quote currency.",
    "Swap principal = notional x fixed rate / 100 / 2, the first semi-annual fixed amount.",
    "Net = principal + accrued + fees when we pay; principal + accrued - fees when we receive. Cash amounts are unsigned.",
    "Accrued interest: Actual/Actual for US Treasuries, 30/360 for corporates, to the settlement date.",
    "Settlement: equities and bonds T+1, FX forwards on the value date, swaps effective T+2.",
    "Counterparties report no commission field, so fees appear only in Net_Cash_Amount.",
    "Rows not listed in this tab are unlabeled on purpose. Their ground truth is in the companion oracle JSON.",
]


def write_summary(ws, known: list) -> dict:
    """Write the summary beside the pairs table. Returns the expected value of every formula cell."""
    n = 2
    expected: dict = {}

    def section(title):
        nonlocal n
        for col in ("H", "I"):
            ws[f"{col}{n}"].fill = SECTION_FILL
            ws[f"{col}{n}"].font = ARIAL_BOLD
        ws[f"H{n}"] = title
        n += 1

    def line(label, formula, value):
        nonlocal n
        ws[f"H{n}"], ws[f"I{n}"] = label, formula
        ws[f"H{n}"].font, ws[f"I{n}"].font = ARIAL, ARIAL
        ws[f"I{n}"].number_format = "#,##0"
        expected[f"I{n}"] = value
        n += 1

    ws["H1"], ws["I1"] = "Summary", "Count"
    for col in ("H", "I"):
        ws[f"{col}1"].font, ws[f"{col}1"].fill = HEADER_FONT, HEADER_FILL
        ws[f"{col}1"].alignment = Alignment(horizontal="center", vertical="center")

    def tagged(tag):
        return sum(tag in k["tests"] for k in known)

    section("Row counts")
    first = n
    line("Internal_Trades rows", "=COUNTA(Internal_Trades!$A:$A)-1", 300)
    line("Counterparty_Trades rows", "=COUNTA(Counterparty_Trades!$A:$A)-1", 200)
    line("Known exact pairs (this tab)", "=COUNTA($B:$B)-1", 120)
    line("Unlabeled internal rows", f"=I{first}-I{first + 2}", 180)
    line("Unlabeled counterparty rows", f"=I{first + 1}-I{first + 2}", 80)
    section("Known exact pairs by asset class")
    for asset in ("Bond", "Equity", "Forward", "Swap"):
        line(asset, f'=COUNTIF($D:$D,"{asset}")', sum(ASSET_CLASS[k["t"].kind] == asset for k in known))
    section("Known exact pairs by match basis")
    for label, basis in (("Shared execution ref", "SHARED_EXEC_REF"), ("Natural business key", "NATURAL_KEY")):
        line(label, f'=COUNTIF($E:$E,"{basis}")', sum(k["basis"] == basis for k in known))
    section("Price and perspective tests")
    for label, tag in (("US Treasury 32nds pricing", "PRICE_32NDS"), ("Dirty price quotes", "PRICE_DIRTY"),
                       ("Dealer perspective inversion", "DEALER_INVERSION")):
        line(label, f'=COUNTIF($F:$F,"*{tag}*")', tagged(tag))
    section("Direction and quantity styles")
    for label, tag in (("B/S/P/R, unsigned quantity", "DIR_CODE_UNSIGNED"), ("Blank direction, signed quantity", "DIR_BLANK_SIGNED"),
                       ("B/S/P/R, signed quantity", "DIR_CODE_SIGNED"), ("BOT/SLD/PAY/REC, unsigned quantity", "DIR_WORD_UNSIGNED"),
                       ("Lowercase, unsigned quantity", "DIR_LOWER_UNSIGNED")):
        line(label, f'=COUNTIF($F:$F,"*{tag}*")', tagged(tag))
    section("Security identifier sent by broker")
    for label, tag in (("ISIN", "ID_ISIN"), ("CUSIP", "ID_CUSIP"), ("SEDOL", "ID_SEDOL"), ("Ticker", "ID_TICKER")):
        line(label, f'=COUNTIF($F:$F,"*{tag}*")', tagged(tag))
    section("Notes")
    for note in NOTES:
        ws[f"H{n}"] = note
        ws[f"H{n}"].font = ARIAL
        n += 1
    ws.column_dimensions["G"].width = 3
    ws.column_dimensions["H"].width = 38
    ws.column_dimensions["I"].width = 9
    return expected


def inject_cached_values(path: Path, sheet_part: str, expected: dict) -> None:
    """Add formula results to the file so readers that don't calculate still see them.

    openpyxl writes formulas with no stored result. Excel recalculates on load regardless
    (fullCalcOnLoad is set), so these values only serve non-calculating readers.
    """
    tmp = path.with_suffix(".tmp")
    with zipfile.ZipFile(path) as src, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as dst:
        for item in src.infolist():
            data = src.read(item.filename)
            if item.filename == sheet_part:
                xml = data.decode("utf-8")
                for ref, value in expected.items():
                    pattern = re.compile(rf'(<c r="{ref}"[^>]*>\s*<f>[^<]*</f>)\s*(?:<v\s*/>|<v></v>)')
                    xml, hits = pattern.subn(rf"\g<1><v>{value}</v>", xml)
                    assert hits == 1, f"could not store the result for {ref}"
                data = xml.encode("utf-8")
            dst.writestr(item, data)
    shutil.move(tmp, path)


def write_workbook(ds: dict, path: Path) -> None:
    wb = Workbook()
    wb.properties.title = "RecDiffy.NET trade reconciliation test dataset"
    wb.properties.creator = "RecDiffy.NET test data generator"
    ws1 = wb.active
    ws1.title = "Internal_Trades"
    write_table(ws1, TAB1, [t.row() for t in ds["internals"]], TAB1_FORMATS)
    ws2 = wb.create_sheet("Counterparty_Trades")
    write_table(ws2, TAB2, [s.row() for s in ds["streets"]], TAB2_FORMATS)
    ws3 = wb.create_sheet("Known_Exact_Matches")
    pair_rows = [[i, k["t"].id, k["s"].id, ASSET_CLASS[k["t"].kind], k["basis"], ";".join(k["tests"])]
                 for i, k in enumerate(ds["known"], 1)]
    write_table(ws3, TAB3, pair_rows, {})
    expected = write_summary(ws3, ds["known"])
    assert ws1.max_row == 301 and ws2.max_row == 201, "row counts changed while writing"
    assert ws3.max_row == 121 and ws3["B121"].value, "Tab 3 must hold exactly 120 pairs, with the summary alongside"
    wb.calculation.fullCalcOnLoad = True
    wb.save(path)
    inject_cached_values(path, "xl/worksheets/sheet3.xml", expected)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--xlsx", default="RecDiffy_Trade_Reconciliation_Dataset.xlsx", type=Path)
    parser.add_argument("--json", default="RecDiffy_Hidden_Test_Oracle.json", type=Path)
    args = parser.parse_args()
    ds = build_dataset()
    write_workbook(ds, args.xlsx)
    args.json.write_text(json.dumps(build_oracle(ds), indent=2), encoding="utf-8")
    print(f"Wrote {args.xlsx} and {args.json}: 300 internal, 200 counterparty, 120 known exact pairs.")


if __name__ == "__main__":
    main()
