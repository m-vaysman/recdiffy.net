#!/usr/bin/env python3
"""
Generate a realistic institutional back-office trade reconciliation test dataset
for RecDiffy.NET.

Outputs:
  1. RecDiffy_Trade_Reconciliation_Dataset.xlsx
  2. RecDiffy_Hidden_Test_Oracle.json

Requirements:
  - openpyxl

Dataset counts:
  - Internal_Trades:        300 rows
  - Counterparty_Trades:    200 rows
  - Known_Exact_Matches:    120 labeled 1:1 exact-match pairs
  - Remaining rows are blind and recorded only in the JSON oracle.
"""

import json
import random
from datetime import date, timedelta

try:
    from openpyxl import Workbook
except ImportError:
    raise SystemExit(
        "openpyxl is required. Install it with:\n"
        "    python -m pip install openpyxl"
    )


SEED = 20260618
random.seed(SEED)

OUT_XLSX = "RecDiffy_Trade_Reconciliation_Dataset.xlsx"
OUT_JSON = "RecDiffy_Hidden_Test_Oracle.json"

BASE_DATE = date(2026, 6, 18)


INTERNAL_COLUMNS = [
    "Internal_Trade_ID",
    "UTI_Exec_ID",
    "Parent_Link_ID",
    "Leg_Structure",
    "Portfolio_Book",
    "Trade_Date",
    "Settle_Date",
    "Asset_Class",
    "Counterparty_Code",
    "Counterparty_Legal_Name",
    "Side",
    "Ticker",
    "ISIN",
    "CUSIP",
    "SEDOL",
    "Instrument_Description",
    "Quantity_Notional",
    "Clean_Price_Rate",
    "Currency",
    "Principal_Gross",
    "Accrued_Interest",
    "Commission_Fees",
    "Net_Settlement_Amount",
    "Coupon_Rate",
    "Maturity_Date",
]

CPTY_COLUMNS = [
    "Cpty_Report_ID",
    "Shared_Market_Exec_Ref",
    "Trans_Lifecycle_Action",
    "Cancels_Prior_Report_ID",
    "Reporting_Counterparty_Name",
    "Counterparty_LEI",
    "Reporting_Perspective",
    "Trade_Date",
    "Value_Settle_Date",
    "Product_Type",
    "Dir_Ind",
    "Primary_Security_ID",
    "Security_ID_Source",
    "Security_Text",
    "Reported_Qty_Notional",
    "Exec_Price_Rate",
    "Price_Quote_Convention",
    "Trade_Ccy",
    "Reported_Accrued_Int",
    "Net_Cash_Amount",
    "Coupon",
    "Maturity",
]


INTERNAL_SEQ = [0]
CPTY_SEQ = [0]


def new_internal_id():
    INTERNAL_SEQ[0] += 1
    return f"INT-{INTERNAL_SEQ[0]:06d}"


def new_cpty_id():
    CPTY_SEQ[0] += 1
    return f"CPTY-{CPTY_SEQ[0]:06d}"


CPTY_CODES = ["GSCO", "JPM", "MSCO", "BAML", "CITI", "BARC", "UBS", "CITADEL"]

LEGAL_NAMES = {
    "GSCO": "Goldman Sachs & Co LLC",
    "JPM": "JPMorgan Chase Bank NA",
    "MSCO": "Morgan Stanley & Co LLC",
    "BAML": "Bank of America Merrill Lynch",
    "CITI": "Citigroup Global Markets Inc",
    "BARC": "Barclays Capital Inc",
    "UBS": "UBS Securities LLC",
    "CITADEL": "Citadel Securities LLC",
}

STREET_PROFILES = [
    {
        "internal_code": "GSCO",
        "street_name": "GS_NY_PRIME",
        "lei": "GSCO_LEI_001",
        "style": "unsigned_letter",
        "id_source": "ISIN",
    },
    {
        "internal_code": "GSCO",
        "street_name": "Goldman Sachs International",
        "lei": "GSCO_LEI_002",
        "style": "both_signed",
        "id_source": "CUSIP",
    },
    {
        "internal_code": "JPM",
        "street_name": "JPMS_CLEAR",
        "lei": "JPM_LEI_001",
        "style": "signed_blank",
        "id_source": "CUSIP",
    },
    {
        "internal_code": "JPM",
        "street_name": "JPMORGAN SECURITIES PLC",
        "lei": "JPM_LEI_002",
        "style": "words_upper",
        "id_source": "SEDOL",
    },
    {
        "internal_code": "MSCO",
        "street_name": "MSCO_NY",
        "lei": "MSCO_LEI_001",
        "style": "words_lower",
        "id_source": "ISIN",
    },
    {
        "internal_code": "MSCO",
        "street_name": "MORGAN STANLEY & CO LLC",
        "lei": "MSCO_LEI_002",
        "style": "unsigned_letter",
        "id_source": "TICKER",
    },
    {
        "internal_code": "BAML",
        "street_name": "BOFA_MLPF&S",
        "lei": "BAML_LEI_001",
        "style": "signed_blank",
        "id_source": "CUSIP",
    },
    {
        "internal_code": "BAML",
        "street_name": "BofA Securities Inc",
        "lei": "BAML_LEI_002",
        "style": "words_upper",
        "id_source": "ISIN",
    },
    {
        "internal_code": "CITI",
        "street_name": "CGMI_NY",
        "lei": "CITI_LEI_001",
        "style": "unsigned_letter",
        "id_source": "ISIN",
    },
    {
        "internal_code": "CITI",
        "street_name": "Citibank NA London",
        "lei": "CITI_LEI_002",
        "style": "both_signed",
        "id_source": "SEDOL",
    },
    {
        "internal_code": "BARC",
        "street_name": "BARCAP_LDN",
        "lei": "BARC_LEI_001",
        "style": "both_signed",
        "id_source": "SEDOL",
    },
    {
        "internal_code": "BARC",
        "street_name": "Barclays Capital Inc",
        "lei": "BARC_LEI_002",
        "style": "words_lower",
        "id_source": "TICKER",
    },
    {
        "internal_code": "UBS",
        "street_name": "UBS_SWISS",
        "lei": "UBS_LEI_001",
        "style": "words_upper",
        "id_source": "TICKER",
    },
    {
        "internal_code": "UBS",
        "street_name": "UBS Securities LLC",
        "lei": "UBS_LEI_002",
        "style": "signed_blank",
        "id_source": "CUSIP",
    },
    {
        "internal_code": "CITADEL",
        "street_name": "CITADEL_SEC",
        "lei": "CITADEL_LEI_001",
        "style": "words_lower",
        "id_source": "TICKER",
    },
    {
        "internal_code": "CITADEL",
        "street_name": "Citadel Securities LLC",
        "lei": "CITADEL_LEI_002",
        "style": "unsigned_letter",
        "id_source": "TICKER",
    },
]

PRODUCT_TYPES = {
    "Bond": "BOND",
    "Equity": "EQUITY",
    "Forward": "FX_FORWARD",
    "Swap": "SWAP",
}


def get_profile(code, seed_index=0):
    options = [p for p in STREET_PROFILES if p["internal_code"] == code]
    if not options:
        options = [STREET_PROFILES[0]]
    return options[seed_index % len(options)]


# -----------------------------------------------------------------------------
# Instrument reference pools
# -----------------------------------------------------------------------------

UST_BONDS = []
for i in range(20):
    coupon = round(2.5 + 0.125 * (i % 12), 3)
    year = 2027 + (i % 6)
    maturity = date(year, (i % 12) + 1, 15)
    UST_BONDS.append(
        {
            "Ticker": f"UST {coupon:.3f} {year}",
            "ISIN": f"US912828{i:04d}1",
            "CUSIP": f"912828{i:04d}",
            "SEDOL": f"2{i:06d}",
            "Instrument_Description": f"US Treasury {coupon:.3f}% {maturity.isoformat()}",
            "Currency": "USD",
            "Coupon": coupon,
            "Maturity": maturity,
        }
    )

CORP_ISSUERS = [
    "APPLE",
    "MICROSOFT",
    "AMAZON",
    "META",
    "JPMORGAN",
    "GOLDMAN SACHS",
    "MORGAN STANLEY",
    "BANK OF AMERICA",
    "CITIGROUP",
    "BARCLAYS",
]

CORP_BONDS = []
for i in range(20):
    issuer = CORP_ISSUERS[i % len(CORP_ISSUERS)]
    coupon = round(2.75 + 0.25 * (i % 8), 3)
    year = 2028 + (i % 5)
    maturity = date(year, (i % 12) + 1, 15)
    CORP_BONDS.append(
        {
            "Ticker": f"{issuer} {coupon:.2f} {year}",
            "ISIN": f"US{100000 + i:06d}1",
            "CUSIP": f"{100000 + i:06d}",
            "SEDOL": f"3{i:06d}",
            "Instrument_Description": f"{issuer} Corporate {coupon:.2f}% {maturity.isoformat()}",
            "Currency": "USD",
            "Coupon": coupon,
            "Maturity": maturity,
        }
    )

ALL_BONDS = UST_BONDS + CORP_BONDS

EQUITY_META = [
    ("AAPL", "APPLE INC"),
    ("MSFT", "MICROSOFT CORP"),
    ("AMZN", "AMAZON.COM INC"),
    ("NVDA", "NVIDIA CORP"),
    ("GOOGL", "ALPHABET INC CLASS A"),
    ("META", "META PLATFORMS INC"),
    ("JPM", "JPMORGAN CHASE & CO"),
    ("GS", "GOLDMAN SACHS GROUP"),
    ("XOM", "EXXON MOBIL CORP"),
    ("CVX", "CHEVRON CORP"),
    ("JNJ", "JOHNSON & JOHNSON"),
    ("PFE", "PFIZER INC"),
    ("UNH", "UNITEDHEALTH GROUP"),
    ("V", "VISA INC"),
    ("MA", "MASTERCARD INC"),
    ("DIS", "WALT DISNEY CO"),
    ("NFLX", "NETFLIX INC"),
    ("ADBE", "ADOBE INC"),
    ("CRM", "SALESFORCE INC"),
    ("ORCL", "ORACLE CORP"),
]

EQUITIES = []
for i, (ticker, name) in enumerate(EQUITY_META):
    EQUITIES.append(
        {
            "Ticker": ticker,
            "ISIN": f"US{200000 + i:06d}1",
            "CUSIP": f"{200000 + i:06d}",
            "SEDOL": f"4{i:06d}",
            "Instrument_Description": f"{name} COMMON STOCK",
            "Currency": "USD",
            "Coupon": 0.0,
            "Maturity": None,
        }
    )

FX_FORWARDS = []
FX_META = [
    ("EURUSD", 1.09),
    ("GBPUSD", 1.27),
    ("AUDUSD", 0.66),
    ("NZDUSD", 0.61),
    ("USDCAD", 1.36),
    ("USDCHF", 0.89),
    ("EURGBP", 0.85),
    ("EURJPY", 169.5),
    ("GBPJPY", 197.2),
    ("USDJPY", 155.4),
]
for pair, base_rate in FX_META:
    FX_FORWARDS.append(
        {
            "Ticker": f"{pair} FX FWD",
            "ISIN": "",
            "CUSIP": "",
            "SEDOL": "",
            "Instrument_Description": f"{pair} FX Forward",
            "Currency": "USD",
            "Coupon": 0.0,
            "Maturity": None,
            "base_rate": base_rate,
        }
    )

SWAPS = []
for i in range(12):
    tenor = f"{(i % 10) + 1}Y"
    ccy = ["USD", "EUR", "GBP"][i % 3]
    rate = round(2.5 + 0.1 * (i % 10), 3)
    maturity = date(2029 + (i % 5), (i % 12) + 1, 15)
    SWAPS.append(
        {
            "Ticker": f"IRS {tenor} {ccy}",
            "ISIN": "",
            "CUSIP": "",
            "SEDOL": "",
            "Instrument_Description": f"Interest Rate Swap {tenor} {ccy}",
            "Currency": ccy,
            "Coupon": rate,
            "Maturity": maturity,
        }
    )


def pick_instrument(asset_class, idx):
    if asset_class == "Bond":
        return ALL_BONDS[idx % len(ALL_BONDS)]
    if asset_class == "Equity":
        return EQUITIES[idx % len(EQUITIES)]
    if asset_class == "Forward":
        return FX_FORWARDS[idx % len(FX_FORWARDS)]
    return SWAPS[idx % len(SWAPS)]


def choose_side(asset_class, idx):
    if asset_class == "Swap":
        return "PAY_FIXED" if idx % 2 == 0 else "REC_FIXED"
    return "BUY" if idx % 2 == 0 else "SELL"


def base_qty_price(asset_class, seq):
    if asset_class == "Bond":
        qty = 1_000_000 + seq * 15_000
        price = round(98.0 + (seq % 12) * 0.25, 6)
        accrued = round(qty * 0.00045, 2)
        fees = round(qty * 0.00008, 2)
        return qty, price, accrued, fees

    if asset_class == "Equity":
        qty = 1_000 + seq * 20
        price = round(80.0 + (seq % 30) * 0.75, 2)
        fees = round(qty * 0.02, 2)
        return qty, price, 0.0, fees

    if asset_class == "Forward":
        qty = 2_000_000 + seq * 25_000
        price = round(1.10 + (seq % 10) * 0.00075, 6)
        fees = round(40.0 + seq * 1.5, 2)
        return qty, price, 0.0, fees

    qty = 5_000_000 + seq * 50_000
    price = round(2.75 + (seq % 15) * 0.05, 6)
    fees = round(120.0 + seq * 2.0, 2)
    return qty, price, 0.0, fees


# -----------------------------------------------------------------------------
# Row builders
# -----------------------------------------------------------------------------


def make_internal_trade(
    asset_class,
    instrument,
    trade_date,
    settle_date,
    cpty_code,
    side,
    qty,
    price,
    portfolio_book,
    accrued=0.0,
    fees=0.0,
    parent_link="",
    leg_structure="STANDARD",
    uti=None,
    trade_id=None,
):
    if trade_id is None:
        trade_id = new_internal_id()

    if uti is None:
        uti = f"UTI-{trade_id}"

    qty = round(float(qty), 2)
    price = round(float(price), 6)

    if asset_class == "Bond":
        principal = round(qty * price / 100.0, 2)
    elif asset_class == "Equity":
        principal = round(qty * price, 2)
    elif asset_class == "Forward":
        principal = round(qty * price, 2)
    elif asset_class == "Swap":
        principal = round(qty, 2)
    else:
        principal = round(qty * price, 2)

    accrued = round(float(accrued), 2)
    fees = round(float(fees), 2)
    net = round(principal + accrued + fees, 2)

    return {
        "Internal_Trade_ID": trade_id,
        "UTI_Exec_ID": uti,
        "Parent_Link_ID": parent_link,
        "Leg_Structure": leg_structure,
        "Portfolio_Book": portfolio_book,
        "Trade_Date": trade_date,
        "Settle_Date": settle_date,
        "Asset_Class": asset_class,
        "Counterparty_Code": cpty_code,
        "Counterparty_Legal_Name": LEGAL_NAMES[cpty_code],
        "Side": side,
        "Ticker": instrument["Ticker"],
        "ISIN": instrument.get("ISIN", ""),
        "CUSIP": instrument.get("CUSIP", ""),
        "SEDOL": instrument.get("SEDOL", ""),
        "Instrument_Description": instrument["Instrument_Description"],
        "Quantity_Notional": qty,
        "Clean_Price_Rate": price,
        "Currency": instrument.get("Currency", "USD"),
        "Principal_Gross": principal,
        "Accrued_Interest": accrued,
        "Commission_Fees": fees,
        "Net_Settlement_Amount": net,
        "Coupon_Rate": instrument.get("Coupon", 0.0),
        "Maturity_Date": instrument.get("Maturity"),
    }


def direction_and_qty(side, qty, style, inverted=False, qty_override=None):
    eff = side
    if inverted:
        eff = {
            "BUY": "SELL",
            "SELL": "BUY",
            "PAY_FIXED": "REC_FIXED",
            "REC_FIXED": "PAY_FIXED",
        }[side]

    q = float(qty if qty_override is None else qty_override)
    q_abs = abs(q)
    negative_side = eff in ("SELL", "PAY_FIXED")

    if style == "unsigned_letter":
        d = {"BUY": "B", "SELL": "S", "PAY_FIXED": "P", "REC_FIXED": "R"}[eff]
        rq = q_abs
    elif style == "signed_blank":
        d = ""
        rq = -q_abs if negative_side else q_abs
    elif style == "both_signed":
        d = {"BUY": "B", "SELL": "S", "PAY_FIXED": "P", "REC_FIXED": "R"}[eff]
        rq = -q_abs if negative_side else q_abs
    elif style == "words_upper":
        d = {"BUY": "BOT", "SELL": "SLD", "PAY_FIXED": "PAY", "REC_FIXED": "REC"}[eff]
        rq = q_abs
    elif style == "words_lower":
        d = {"BUY": "buy", "SELL": "sell", "PAY_FIXED": "pay", "REC_FIXED": "rec"}[eff]
        rq = q_abs
    else:
        d = "B" if eff == "BUY" else "S"
        rq = q_abs

    return d, round(rq, 2)


def get_primary_id(internal_row, preferred_source):
    mapping = {
        "ISIN": internal_row.get("ISIN", ""),
        "CUSIP": internal_row.get("CUSIP", ""),
        "SEDOL": internal_row.get("SEDOL", ""),
        "TICKER": internal_row.get("Ticker", ""),
    }

    if mapping.get(preferred_source):
        return mapping[preferred_source], preferred_source

    for source in ["ISIN", "CUSIP", "SEDOL", "TICKER"]:
        if mapping.get(source):
            return mapping[source], source

    return internal_row.get("Ticker", "UNKNOWN"), "TICKER"


def make_cpty_row(
    internal_row,
    profile,
    use_internal_ref=False,
    shared_ref=None,
    lifecycle="NEW",
    cancels="",
    perspective="PB_CLIENT_VIEW",
    price_override=None,
    quote_convention="DECIMAL",
    accrued_override=None,
    net_override=None,
    qty_override=None,
    date_override=None,
):
    cpty_id = new_cpty_id()

    if shared_ref is None:
        shared_ref = internal_row.get("UTI_Exec_ID") if use_internal_ref else f"EXT-{cpty_id}"

    dir_ind, reported_qty = direction_and_qty(
        internal_row["Side"],
        internal_row["Quantity_Notional"],
        profile["style"],
        perspective == "DEALER_BILATERAL_VIEW",
        qty_override,
    )

    primary_id, source_actual = get_primary_id(internal_row, profile["id_source"])

    price = internal_row["Clean_Price_Rate"] if price_override is None else price_override
    accrued = internal_row["Accrued_Interest"] if accrued_override is None else accrued_override
    net = internal_row["Net_Settlement_Amount"] if net_override is None else net_override
    settle = internal_row["Settle_Date"] if date_override is None else date_override

    return (
        {
            "Cpty_Report_ID": cpty_id,
            "Shared_Market_Exec_Ref": shared_ref,
            "Trans_Lifecycle_Action": lifecycle,
            "Cancels_Prior_Report_ID": cancels,
            "Reporting_Counterparty_Name": profile["street_name"],
            "Counterparty_LEI": profile["lei"],
            "Reporting_Perspective": perspective,
            "Trade_Date": internal_row["Trade_Date"],
            "Value_Settle_Date": settle,
            "Product_Type": PRODUCT_TYPES[internal_row["Asset_Class"]],
            "Dir_Ind": dir_ind,
            "Primary_Security_ID": primary_id,
            "Security_ID_Source": source_actual,
            "Security_Text": internal_row["Instrument_Description"],
            "Reported_Qty_Notional": reported_qty,
            "Exec_Price_Rate": price,
            "Price_Quote_Convention": quote_convention,
            "Trade_Ccy": internal_row["Currency"],
            "Reported_Accrued_Int": accrued,
            "Net_Cash_Amount": net,
            "Coupon": internal_row.get("Coupon_Rate", 0.0),
            "Maturity": internal_row.get("Maturity_Date"),
        },
        cpty_id,
    )


# -----------------------------------------------------------------------------
# Dataset generation
# -----------------------------------------------------------------------------

internal_rows = []
cpty_rows = []

known_pairs = []
known_oracle = []

oracle_near = []
oracle_breaks = []
oracle_splits = []
oracle_legs = []
oracle_cancel = []
oracle_orphans = []
oracle_alleged = []


# -----------------------------------------------------------------------------
# 1) 120 known exact matches
# -----------------------------------------------------------------------------

UST_32ND_CASES = [
    (98, 24, False),
    (98, 11, True),
    (99, 8, False),
    (97, 16, False),
    (100, 0, False),
    (99, 31, False),
    (98, 3, True),
    (97, 22, False),
    (99, 12, False),
    (98, 17, False),
    (100, 5, True),
    (97, 9, False),
]

inversions_left = 15

for i in range(120):
    challenge_32 = False
    challenge_dirty = False

    price_override = None
    quote_convention = "DECIMAL"
    accrued_override = None

    if i < 40:
        asset_class = "Bond"

        if i < 12:
            instrument = UST_BONDS[i % len(UST_BONDS)]
            handle, ticks, plus = UST_32ND_CASES[i]
            price = round(handle + ticks / 32.0 + (0.5 / 32.0 if plus else 0.0), 6)
            challenge_32 = True
        elif i < 20:
            instrument = CORP_BONDS[(i - 12) % len(CORP_BONDS)]
            price = round(97.75 + ((i - 12) % 8) * 0.25, 6)
            challenge_dirty = True
        else:
            instrument = ALL_BONDS[(i - 20) % len(ALL_BONDS)]
            price = round(98.0 + ((i - 20) % 12) * 0.25, 6)

        qty = 1_000_000 + i * 10_000
        accrued = round(qty * (0.0012 if challenge_dirty else 0.00035), 2)
        fees = round(qty * 0.00007, 2)

    elif i < 70:
        asset_class = "Equity"
        instrument = EQUITIES[(i - 40) % len(EQUITIES)]
        qty = 1_000 + (i - 40) * 15
        price = round(75.0 + ((i - 40) % 25) * 0.85, 2)
        accrued = 0.0
        fees = round(qty * 0.02, 2)

    elif i < 95:
        asset_class = "Forward"
        instrument = FX_FORWARDS[(i - 70) % len(FX_FORWARDS)]
        qty = 2_500_000 + (i - 70) * 20_000
        price = round(instrument.get("base_rate", 1.10) + ((i - 70) % 10) * 0.00025, 6)
        accrued = 0.0
        fees = round(35.0 + (i - 70) * 1.25, 2)

    else:
        asset_class = "Swap"
        instrument = SWAPS[(i - 95) % len(SWAPS)]
        qty = 7_500_000 + (i - 95) * 100_000
        price = round(3.0 + ((i - 95) % 12) * 0.025, 6)
        accrued = 0.0
        fees = round(180.0 + (i - 95) * 3.5, 2)

    side = choose_side(asset_class, i)
    trade_date = BASE_DATE - timedelta(days=i % 5)
    settle_date = trade_date + timedelta(days=2 if asset_class in ("Bond", "Equity") else 1)

    cpty_code = CPTY_CODES[i % len(CPTY_CODES)]
    portfolio_book = ["MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP", "CORE_FINANCIALS"][i % 4]

    internal_row = make_internal_trade(
        asset_class,
        instrument,
        trade_date,
        settle_date,
        cpty_code,
        side,
        qty,
        price,
        portfolio_book,
        accrued=accrued,
        fees=fees,
        uti=f"UTI-KN-{i + 1:04d}",
    )
    internal_rows.append(internal_row)

    share = (i % 2 == 0)
    profile = get_profile(cpty_code, i)
    perspective = "PB_CLIENT_VIEW"

    challenges = [
        "counterparty_naming_drift",
        f'direction_style:{profile["style"]}',
    ]

    if asset_class in ("Forward", "Swap") and inversions_left > 0:
        perspective = "DEALER_BILATERAL_VIEW"
        inversions_left -= 1
        challenges.append("dealer_bilateral_perspective_inversion")

    if share:
        challenges.append("shared_exec_id_present")
    else:
        challenges.append("missing_shared_exec_id")

    if challenge_32:
        handle, ticks, plus = UST_32ND_CASES[i]
        plus_suffix = "+" if plus else ""
        price_override = f"{handle}-{ticks:02d}{plus_suffix}"
        quote_convention = "32NDS_UST"
        challenges.append("us_treasury_32nds_pricing")

    if challenge_dirty:
        dirty_price = round(
            price + internal_row["Accrued_Interest"] / internal_row["Quantity_Notional"] * 100.0,
            6,
        )
        price_override = dirty_price
        quote_convention = "DIRTY"
        accrued_override = 0.0
        challenges.append("dirty_price_quote")

    cpty_row, cpty_id = make_cpty_row(
        internal_row,
        profile,
        use_internal_ref=share,
        perspective=perspective,
        price_override=price_override,
        quote_convention=quote_convention,
        accrued_override=accrued_override,
    )
    cpty_rows.append(cpty_row)

    challenges.append(f'security_id_source:{cpty_row["Security_ID_Source"]}')

    known_pairs.append(
        {
            "Internal_Trade_ID": internal_row["Internal_Trade_ID"],
            "Cpty_Report_ID": cpty_id,
        }
    )

    known_oracle.append(
        {
            "internal_trade_id": internal_row["Internal_Trade_ID"],
            "cpty_report_id": cpty_id,
            "challenges": challenges,
        }
    )

assert inversions_left == 0, "Expected exactly 15 dealer bilateral perspective inversions."


# -----------------------------------------------------------------------------
# 2) Blind: 18 within-tolerance near matches
# -----------------------------------------------------------------------------

for n in range(18):
    if n % 3 == 0:
        asset_class = "Bond"
        instrument = pick_instrument(asset_class, n + 5)
        side = choose_side(asset_class, n)
        qty = 1_200_000 + n * 12_000
        price = round(98.25 + (n % 7) * 0.25, 6)
        accrued = round(qty * 0.0005, 2)
        fees = round(qty * 0.00005, 2)
    elif n % 3 == 1:
        asset_class = "Forward"
        instrument = pick_instrument(asset_class, n + 2)
        side = choose_side(asset_class, n)
        qty = 2_200_000 + n * 8_000
        price = round(instrument.get("base_rate", 1.10) + (n % 5) * 0.0002, 6)
        accrued = 0.0
        fees = round(30.0 + n * 1.1, 2)
    else:
        asset_class = "Equity"
        instrument = pick_instrument(asset_class, n + 1)
        side = choose_side(asset_class, n)
        qty = 1_200 + n * 15
        price = round(90.0 + (n % 12) * 0.65, 2)
        accrued = 0.0
        fees = round(qty * 0.02, 2)

    trade_date = BASE_DATE - timedelta(days=n % 5)
    settle_date = trade_date + timedelta(days=2 if asset_class in ("Bond", "Equity") else 1)

    cpty_code = CPTY_CODES[(n + 1) % len(CPTY_CODES)]
    profile = get_profile(cpty_code, n + 2)

    internal_row = make_internal_trade(
        asset_class,
        instrument,
        trade_date,
        settle_date,
        cpty_code,
        side,
        qty,
        price,
        "STAT_ARB_EQ",
        accrued=accrued,
        fees=fees,
        uti=f"UTI-NR-{n + 1:02d}",
    )
    internal_rows.append(internal_row)

    expected = {}

    if asset_class == "Bond":
        diff = random.choice([-1.0, 1.0]) * round(random.uniform(0.40, 1.20), 2)
        diff = round(diff, 2)
        accrued_override = round(internal_row["Accrued_Interest"] + diff, 2)
        net_override = round(internal_row["Net_Settlement_Amount"] + diff, 2)
        price_override = None
        expected = {"field": "reported_accrued_int", "difference": diff}
    elif asset_class == "Forward":
        delta = random.choice([-0.0001, 0.0001])
        price_override = round(internal_row["Clean_Price_Rate"] + delta, 6)
        net_override = round(
            internal_row["Quantity_Notional"] * price_override + internal_row["Commission_Fees"],
            2,
        )
        accrued_override = None
        expected = {"field": "exec_price_rate", "difference": delta}
    else:
        diff = random.choice([-0.01, 0.01])
        net_override = round(internal_row["Net_Settlement_Amount"] + diff, 2)
        price_override = None
        accrued_override = None
        expected = {"field": "net_cash_amount", "difference": diff}

    cpty_row, cpty_id = make_cpty_row(
        internal_row,
        profile,
        use_internal_ref=(n % 2 == 0),
        price_override=price_override,
        accrued_override=accrued_override,
        net_override=net_override,
    )
    cpty_rows.append(cpty_row)

    oracle_near.append(
        {
            "internal_trade_id": internal_row["Internal_Trade_ID"],
            "cpty_report_id": cpty_id,
            "scenario": "within_tolerance_near_match",
            "expected_difference": expected,
        }
    )


# -----------------------------------------------------------------------------
# 3) Blind: 20 true economic field breaks
# -----------------------------------------------------------------------------

BREAK_TYPES = [
    "execution_price_break",
    "partial_fill_quantity_break",
    "settlement_date_lag",
    "commission_break",
]

for n in range(20):
    break_type = BREAK_TYPES[n % 4]
    asset_class = ["Bond", "Equity", "Forward", "Swap"][n % 4]

    instrument = pick_instrument(asset_class, n + 13)
    side = choose_side(asset_class, n)

    qty, price, accrued, fees = base_qty_price(asset_class, n + 31)
    if asset_class == "Forward":
        price = round(instrument.get("base_rate", price) + (n % 4) * 0.0002, 6)

    trade_date = BASE_DATE - timedelta(days=n % 6)
    settle_date = trade_date + timedelta(days=2 if asset_class in ("Bond", "Equity") else 1)

    cpty_code = CPTY_CODES[(n + 2) % len(CPTY_CODES)]
    profile = get_profile(cpty_code, n + 4)

    internal_row = make_internal_trade(
        asset_class,
        instrument,
        trade_date,
        settle_date,
        cpty_code,
        side,
        qty,
        price,
        "CREDIT_OPP",
        accrued=accrued,
        fees=fees,
        uti=f"UTI-BRK-{n + 1:02d}",
    )
    internal_rows.append(internal_row)

    price_override = None
    qty_override = None
    date_override = None
    net_override = None
    accrued_override = None

    expected = {"break_type": break_type}

    if break_type == "execution_price_break":
        if asset_class == "Bond":
            price_override = round(price + 1.25, 6)
        elif asset_class == "Forward":
            price_override = round(price + 0.0035, 6)
        elif asset_class == "Swap":
            price_override = round(price + 0.25, 6)
        else:
            price_override = round(price * 1.03, 2)

        net_override = round(internal_row["Net_Settlement_Amount"] * 1.04, 2)
        expected["field"] = "exec_price_rate"

    elif break_type == "partial_fill_quantity_break":
        qty_override = round(qty * 0.80, 2)
        net_override = round(internal_row["Net_Settlement_Amount"] * 0.80, 2)
        expected["field"] = "reported_qty_notional"

    elif break_type == "settlement_date_lag":
        date_override = settle_date + timedelta(days=1)
        expected["field"] = "value_settle_date"

    else:
        net_override = round(internal_row["Net_Settlement_Amount"] + 450.0, 2)
        expected["field"] = "net_cash_amount"

    cpty_row, cpty_id = make_cpty_row(
        internal_row,
        profile,
        use_internal_ref=(n % 3 == 0),
        price_override=price_override,
        accrued_override=accrued_override,
        net_override=net_override,
        qty_override=qty_override,
        date_override=date_override,
    )
    cpty_rows.append(cpty_row)

    oracle_breaks.append(
        {
            "internal_trade_id": internal_row["Internal_Trade_ID"],
            "cpty_report_id": cpty_id,
            "scenario": "true_economic_field_break",
            "expected_break": expected,
        }
    )


# -----------------------------------------------------------------------------
# 4) Blind: 12 N-to-1 allocation splits => 36 internal rows, 12 cpty rows
# -----------------------------------------------------------------------------

for g in range(12):
    asset_class = ["Bond", "Equity", "Forward", "Swap"][g % 4]
    instrument = pick_instrument(asset_class, g + 21)
    cpty_code = CPTY_CODES[(g + 3) % len(CPTY_CODES)]
    profile = get_profile(cpty_code, g + 6)

    side = choose_side(asset_class, g)
    trade_date = BASE_DATE - timedelta(days=g % 7)
    settle_date = trade_date + timedelta(days=2 if asset_class in ("Bond", "Equity") else 1)

    if asset_class == "Bond":
        total_qty = 1_500_000 + g * 30_000
        price = round(99.0 + (g % 6) * 0.20, 6)
        total_accrued = round(total_qty * 0.0004, 2)
        total_fees = round(total_qty * 0.00006, 2)
    elif asset_class == "Equity":
        total_qty = 8_000 + g * 300
        price = round(95.0 + (g % 8) * 1.05, 2)
        total_accrued = 0.0
        total_fees = round(total_qty * 0.02, 2)
    elif asset_class == "Forward":
        total_qty = 3_000_000 + g * 75_000
        price = round(instrument.get("base_rate", 1.10) + (g % 5) * 0.0002, 6)
        total_accrued = 0.0
        total_fees = round(80.0 + g * 3.0, 2)
    else:
        total_qty = 8_000_000 + g * 150_000
        price = round(3.1 + (g % 6) * 0.03, 6)
        total_accrued = 0.0
        total_fees = round(220.0 + g * 8.0, 2)

    books = ["MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP"]
    fracs = [0.40, 0.35, 0.25]

    internal_ids = []
    sum_net = 0.0
    alloc_qty_sum = 0.0
    alloc_acc_sum = 0.0
    alloc_fee_sum = 0.0
    first_row = None

    for b, book in enumerate(books):
        if b < 2:
            alloc_qty = round(total_qty * fracs[b], 2)
            alloc_acc = round(total_accrued * fracs[b], 2)
            alloc_fee = round(total_fees * fracs[b], 2)
        else:
            alloc_qty = round(total_qty - alloc_qty_sum, 2)
            alloc_acc = round(total_accrued - alloc_acc_sum, 2)
            alloc_fee = round(total_fees - alloc_fee_sum, 2)

        internal_row = make_internal_trade(
            asset_class,
            instrument,
            trade_date,
            settle_date,
            cpty_code,
            side,
            alloc_qty,
            price,
            book,
            accrued=alloc_acc,
            fees=alloc_fee,
            parent_link=f"ALLOC-{g + 1:03d}",
            leg_structure="ALLOCATION",
            uti=f"UTI-ALLOC-{g + 1:03d}-{b + 1}",
        )
        internal_rows.append(internal_row)

        internal_ids.append(internal_row["Internal_Trade_ID"])
        if first_row is None:
            first_row = internal_row

        sum_net = round(sum_net + internal_row["Net_Settlement_Amount"], 2)
        alloc_qty_sum = round(alloc_qty_sum + alloc_qty, 2)
        alloc_acc_sum = round(alloc_acc_sum + alloc_acc, 2)
        alloc_fee_sum = round(alloc_fee_sum + alloc_fee, 2)

    agg = first_row.copy()
    agg["Quantity_Notional"] = total_qty
    agg["Accrued_Interest"] = total_accrued
    agg["Commission_Fees"] = total_fees
    agg["Net_Settlement_Amount"] = sum_net
    agg["UTI_Exec_ID"] = f"UTI-ALLOC-{g + 1:03d}"

    cpty_row, cpty_id = make_cpty_row(
        agg,
        profile,
        use_internal_ref=(g % 2 == 0),
    )
    cpty_rows.append(cpty_row)

    oracle_splits.append(
        {
            "cpty_report_id": cpty_id,
            "internal_trade_ids": internal_ids,
            "scenario": "n_to_1_allocation_split",
            "expected_relation": "sum_of_internal_allocations_to_single_block",
        }
    )


# -----------------------------------------------------------------------------
# 5) Blind: 8 multi-leg swap/FX representations => 16 internal rows, 8 cpty rows
# -----------------------------------------------------------------------------

for g in range(8):
    instrument = SWAPS[(g + 3) % len(SWAPS)]
    cpty_code = CPTY_CODES[(g + 4) % len(CPTY_CODES)]
    profile = get_profile(cpty_code, g + 8)

    trade_date = BASE_DATE - timedelta(days=g % 4)
    settle_date = trade_date + timedelta(days=1)

    parent = f"MULTILEG-{g + 1:02d}"

    notional1 = 5_000_000 + g * 250_000
    notional2 = 3_000_000 + g * 150_000

    price1 = round(3.0 + 0.04 * g, 6)
    price2 = round(3.1 + 0.04 * g, 6)

    fees1 = round(160.0 + g * 4.0, 2)
    fees2 = round(110.0 + g * 3.0, 2)

    leg1 = make_internal_trade(
        "Swap",
        instrument,
        trade_date,
        settle_date,
        cpty_code,
        "PAY_FIXED",
        notional1,
        price1,
        "MACRO_RATES",
        accrued=0.0,
        fees=fees1,
        parent_link=parent,
        leg_structure="LEG_1_PAY",
        uti=f"UTI-{parent}-1",
    )

    leg2 = make_internal_trade(
        "Swap",
        instrument,
        trade_date,
        settle_date,
        cpty_code,
        "REC_FIXED",
        notional2,
        price2,
        "MACRO_RATES",
        accrued=0.0,
        fees=fees2,
        parent_link=parent,
        leg_structure="LEG_2_RECEIVE",
        uti=f"UTI-{parent}-2",
    )

    internal_rows.append(leg1)
    internal_rows.append(leg2)

    sum_net = round(leg1["Net_Settlement_Amount"] + leg2["Net_Settlement_Amount"], 2)

    agg = leg1.copy()
    agg["Quantity_Notional"] = round(notional1 + notional2, 2)
    agg["Net_Settlement_Amount"] = sum_net
    agg["UTI_Exec_ID"] = f"UTI-{parent}"

    cpty_row, cpty_id = make_cpty_row(
        agg,
        profile,
        use_internal_ref=(g % 2 == 0),
    )
    cpty_rows.append(cpty_row)

    oracle_legs.append(
        {
            "cpty_report_id": cpty_id,
            "internal_trade_ids": [
                leg1["Internal_Trade_ID"],
                leg2["Internal_Trade_ID"],
            ],
            "parent_link_id": parent,
            "scenario": "multi_leg_swap_fx_representation",
            "expected_relation": "two_internal_legs_to_one_counterparty_combined_row",
        }
    )


# -----------------------------------------------------------------------------
# 6) Blind: 5 cancel/correct lifecycle chains => 5 internal rows, 15 cpty rows
# -----------------------------------------------------------------------------

for c in range(5):
    asset_class = ["Bond", "Equity", "Forward", "Swap"][c % 4]
    instrument = pick_instrument(asset_class, c + 41)
    side = choose_side(asset_class, c)

    qty, price, accrued, fees = base_qty_price(asset_class, c + 77)
    if asset_class == "Forward":
        price = round(instrument.get("base_rate", price) + (c % 3) * 0.0001, 6)

    trade_date = BASE_DATE - timedelta(days=c % 3)
    settle_date = trade_date + timedelta(days=2 if asset_class in ("Bond", "Equity") else 1)

    cpty_code = CPTY_CODES[(c + 5) % len(CPTY_CODES)]
    profile = get_profile(cpty_code, c + 10)

    internal_row = make_internal_trade(
        asset_class,
        instrument,
        trade_date,
        settle_date,
        cpty_code,
        side,
        qty,
        price,
        "OPERATIONS_BREAKS",
        accrued=accrued,
        fees=fees,
        uti=f"UTI-CANC-{c + 1:02d}",
    )
    internal_rows.append(internal_row)

    if asset_class == "Bond":
        bust_price = round(price + 1.5, 6)
    elif asset_class == "Forward":
        bust_price = round(price + 0.005, 6)
    elif asset_class == "Swap":
        bust_price = round(price + 0.25, 6)
    else:
        bust_price = round(price * 1.05, 2)

    bust_net = round(internal_row["Net_Settlement_Amount"] * 1.05, 2)

    original_row, original_id = make_cpty_row(
        internal_row,
        profile,
        use_internal_ref=True,
        lifecycle="NEW",
        price_override=bust_price,
        net_override=bust_net,
    )
    cpty_rows.append(original_row)

    cancel_row, cancel_id = make_cpty_row(
        internal_row,
        profile,
        use_internal_ref=True,
        lifecycle="CANC",
        cancels=original_id,
        price_override=bust_price,
        net_override=-bust_net,
    )

    cancel_row["Reported_Qty_Notional"] = -original_row["Reported_Qty_Notional"]
    cancel_row["Net_Cash_Amount"] = -original_row["Net_Cash_Amount"]
    cancel_row["Reported_Accrued_Int"] = -original_row["Reported_Accrued_Int"]

    cpty_rows.append(cancel_row)

    corr_row, corr_id = make_cpty_row(
        internal_row,
        profile,
        use_internal_ref=True,
        lifecycle="CORR",
        cancels=original_id,
    )
    cpty_rows.append(corr_row)

    oracle_cancel.append(
        {
            "internal_trade_id": internal_row["Internal_Trade_ID"],
            "original_new_report_id": original_id,
            "cancel_report_id": cancel_id,
            "corrected_report_id": corr_id,
            "scenario": "cancel_correct_lifecycle_chain",
            "expected_final_match_report_id": corr_id,
        }
    )


# -----------------------------------------------------------------------------
# 7) Blind: 85 unmatched internal orphans
# -----------------------------------------------------------------------------

for n in range(85):
    asset_class = ["Bond", "Equity", "Forward", "Swap"][n % 4]
    instrument = pick_instrument(asset_class, n + 55)
    side = choose_side(asset_class, n)

    qty, price, accrued, fees = base_qty_price(asset_class, n + 101)
    if asset_class == "Forward":
        price = round(instrument.get("base_rate", price) + (n % 4) * 0.0001, 6)

    trade_date = BASE_DATE - timedelta(days=n % 7)
    settle_date = trade_date + timedelta(days=2 if asset_class in ("Bond", "Equity") else 1)

    cpty_code = CPTY_CODES[(n + 6) % len(CPTY_CODES)]

    internal_row = make_internal_trade(
        asset_class,
        instrument,
        trade_date,
        settle_date,
        cpty_code,
        side,
        qty,
        price,
        "DK_UNCONFIRMED",
        accrued=accrued,
        fees=fees,
        uti=f"UTI-ORPH-{n + 1:03d}",
    )
    internal_rows.append(internal_row)

    oracle_orphans.append(
        {
            "internal_trade_id": internal_row["Internal_Trade_ID"],
            "scenario": "unmatched_internal_orphan",
        }
    )


# -----------------------------------------------------------------------------
# 8) Blind: 7 unmatched counterparty alleged trades
# -----------------------------------------------------------------------------

for n in range(7):
    asset_class = ["Bond", "Equity", "Forward", "Swap"][n % 4]
    instrument = pick_instrument(asset_class, n + 71)
    side = choose_side(asset_class, n)

    qty, price, accrued, fees = base_qty_price(asset_class, n + 137)
    if asset_class == "Forward":
        price = round(instrument.get("base_rate", price) + (n % 3) * 0.0002, 6)

    trade_date = BASE_DATE - timedelta(days=n % 4)
    settle_date = trade_date + timedelta(days=2 if asset_class in ("Bond", "Equity") else 1)

    cpty_code = CPTY_CODES[(n + 2) % len(CPTY_CODES)]
    profile = get_profile(cpty_code, n + 12)

    pseudo_internal = make_internal_trade(
        asset_class,
        instrument,
        trade_date,
        settle_date,
        cpty_code,
        side,
        qty,
        price,
        "ALLEGED_STREET",
        accrued=accrued,
        fees=fees,
        trade_id=f"TMP-ALLEGED-{n + 1:02d}",
        uti=f"UTI-ALLEGED-{n + 1:02d}",
    )

    cpty_row, cpty_id = make_cpty_row(
        pseudo_internal,
        profile,
        use_internal_ref=False,
        lifecycle="NEW",
    )
    cpty_rows.append(cpty_row)

    oracle_alleged.append(
        {
            "cpty_report_id": cpty_id,
            "scenario": "unmatched_counterparty_alleged_trade",
        }
    )


# -----------------------------------------------------------------------------
# Validation before output
# -----------------------------------------------------------------------------

assert len(internal_rows) == 300, f"Internal rows expected 300, got {len(internal_rows)}"
assert len(cpty_rows) == 200, f"Counterparty rows expected 200, got {len(cpty_rows)}"
assert len(known_pairs) == 120, f"Known exact pairs expected 120, got {len(known_pairs)}"

assert len(internal_rows) - len(known_pairs) == 180
assert len(cpty_rows) - len(known_pairs) == 80

assert len(oracle_near) == 18
assert len(oracle_breaks) == 20
assert len(oracle_splits) == 12
assert sum(len(x["internal_trade_ids"]) for x in oracle_splits) == 36
assert len(oracle_legs) == 8
assert sum(len(x["internal_trade_ids"]) for x in oracle_legs) == 16
assert len(oracle_cancel) == 5
assert len(oracle_orphans) == 85
assert len(oracle_alleged) == 7

internal_id_set = set(r["Internal_Trade_ID"] for r in internal_rows)
cpty_id_set = set(r["Cpty_Report_ID"] for r in cpty_rows)

assert len(internal_id_set) == 300, "Internal trade IDs are not unique."
assert len(cpty_id_set) == 200, "Counterparty report IDs are not unique."

for pair in known_pairs:
    assert pair["Internal_Trade_ID"] in internal_id_set
    assert pair["Cpty_Report_ID"] in cpty_id_set


# Shuffle main tabs so the labeled pairs are not positionally obvious.
random.shuffle(internal_rows)
random.shuffle(cpty_rows)


# -----------------------------------------------------------------------------
# Excel output
# -----------------------------------------------------------------------------

wb = Workbook()

ws1 = wb.active
ws1.title = "Internal_Trades"
ws1.append(INTERNAL_COLUMNS)
for row in internal_rows:
    ws1.append([row.get(col) for col in INTERNAL_COLUMNS])

ws2 = wb.create_sheet("Counterparty_Trades")
ws2.append(CPTY_COLUMNS)
for row in cpty_rows:
    ws2.append([row.get(col) for col in CPTY_COLUMNS])

ws3 = wb.create_sheet("Known_Exact_Matches")
ws3.append(["Internal_Trade_ID", "Cpty_Report_ID"])

for pair in known_pairs:
    ws3.append([pair["Internal_Trade_ID"], pair["Cpty_Report_ID"]])

ws3.append([])
ws3.append(["Summary_Metric", "Value"])


def known_challenge_count(token):
    return sum(1 for item in known_oracle if any(token in ch for ch in item["challenges"]))


summary_rows = [
    ("Generated_By", "RecDiffy trade reconciliation test dataset generator"),
    ("Random_Seed", SEED),
    ("Base_Trade_Date", BASE_DATE.isoformat()),
    ("Internal_Trade_Row_Count", len(internal_rows)),
    ("Counterparty_Trade_Row_Count", len(cpty_rows)),
    ("Known_Exact_Match_Pairs", len(known_pairs)),
    ("Blind_Internal_Rows", len(internal_rows) - len(known_pairs)),
    ("Blind_Counterparty_Rows", len(cpty_rows) - len(known_pairs)),
    ("Known_US_Treasury_32nds_Pairs", known_challenge_count("us_treasury_32nds_pricing")),
    ("Known_Dirty_Price_Pairs", known_challenge_count("dirty_price_quote")),
    ("Known_Dealer_Bilateral_Inversion_Pairs", known_challenge_count("dealer_bilateral_perspective_inversion")),
    ("Known_Missing_Shared_Exec_ID_Pairs", known_challenge_count("missing_shared_exec_id")),
    ("Known_Shared_Exec_ID_Pairs", known_challenge_count("shared_exec_id_present")),
    ("Blind_Within_Tolerance_Near_Matches", len(oracle_near)),
    ("Blind_True_Economic_Field_Breaks", len(oracle_breaks)),
    ("Blind_N_To_1_Allocation_Split_Groups", len(oracle_splits)),
    ("Blind_Multi_Leg_Representation_Groups", len(oracle_legs)),
    ("Blind_Cancel_Correct_Lifecycle_Chains", len(oracle_cancel)),
    ("Blind_Internal_Orphans", len(oracle_orphans)),
    ("Blind_Counterparty_Alleged_Trades", len(oracle_alleged)),
]

for metric, value in summary_rows:
    ws3.append([metric, value])

wb.save(OUT_XLSX)


# -----------------------------------------------------------------------------
# JSON oracle output
# -----------------------------------------------------------------------------

oracle_payload = {
    "metadata": {
        "generator": "RecDiffy trade reconciliation test dataset generator",
        "seed": SEED,
        "base_trade_date": BASE_DATE.isoformat(),
        "expected_counts": {
            "internal_trades": len(internal_rows),
            "counterparty_trades": len(cpty_rows),
            "known_exact_match_pairs": len(known_pairs),
            "blind_internal_rows": len(internal_rows) - len(known_pairs),
            "blind_counterparty_rows": len(cpty_rows) - len(known_pairs),
            "scenario_counts": {
                "within_tolerance_near_matches": len(oracle_near),
                "true_economic_field_breaks": len(oracle_breaks),
                "n_to_1_allocation_splits": {
                    "groups": len(oracle_splits),
                    "internal_rows": sum(len(x["internal_trade_ids"]) for x in oracle_splits),
                    "counterparty_rows": len(oracle_splits),
                },
                "multi_leg_swap_fx_representation": {
                    "groups": len(oracle_legs),
                    "internal_rows": sum(len(x["internal_trade_ids"]) for x in oracle_legs),
                    "counterparty_rows": len(oracle_legs),
                },
                "cancel_correct_lifecycle_chains": {
                    "chains": len(oracle_cancel),
                    "internal_rows": len(oracle_cancel),
                    "counterparty_rows": len(oracle_cancel) * 3,
                },
                "unmatched_internal_orphans": len(oracle_orphans),
                "unmatched_counterparty_alleged_trades": len(oracle_alleged),
            },
        },
    },
    "known_exact_matches": known_oracle,
    "blind_scenarios": {
        "within_tolerance_near_matches": oracle_near,
        "true_economic_field_breaks": oracle_breaks,
        "n_to_1_allocation_splits": oracle_splits,
        "multi_leg_swap_fx_representation": oracle_legs,
        "cancel_correct_lifecycle_chains": oracle_cancel,
        "unmatched_internal_orphans": oracle_orphans,
        "unmatched_counterparty_alleged_trades": oracle_alleged,
    },
}

with open(OUT_JSON, "w", encoding="utf-8") as f:
    json.dump(oracle_payload, f, indent=2, sort_keys=False)

print(f"Wrote: {OUT_XLSX}")
print(f"Wrote: {OUT_JSON}")
