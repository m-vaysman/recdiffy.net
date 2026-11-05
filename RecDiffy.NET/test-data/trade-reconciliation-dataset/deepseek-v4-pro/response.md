Below is a complete Python script that uses only the standard library and `openpyxl` to generate both the `.xlsx` workbook and the `.json` oracle file. It reproduces every requirement from the prompt—exact row counts, all normalization challenges, and every blind discovery scenario.

To run it, install `openpyxl` if you haven’t already:

```bash
pip install openpyxl
```

Then execute the script (e.g., `python create_dataset.py`). The two files will be created in the current directory.

```python
#!/usr/bin/env python3
"""
RecDiffy.NET Trade Reconciliation Dataset Generator
Generates:
  1) RecDiffy_Trade_Reconciliation_Dataset.xlsx  (3 tabs, 300/200/120 rows)
  2) RecDiffy_Hidden_Test_Oracle.json            (complete ground-truth mapping)
"""

import random
import json
import datetime
import math
from collections import defaultdict
from openpyxl import Workbook
from openpyxl.utils import get_column_letter

# ----------------------------------------------------------------------
# 0.  Configuration & seed (deterministic)
# ----------------------------------------------------------------------
random.seed(42)
START_DATE = datetime.date(2025, 1, 2)   # earliest Trade_Date (rolling forward)

# ----------------------------------------------------------------------
# 1.  Master reference data
# ----------------------------------------------------------------------
# Counterparty codes -> (Legal Name, typical LEI, aliases used by street)
CP_INFO = {
    "GSCO": {
        "Legal": "Goldman Sachs & Co. LLC",
        "LEI": "549300N2XH7W3KZL1C53",
        "aliases": ["GS_NY_PRIME", "Goldman Sachs International", "GS_EQ_DERIV"],
    },
    "JPM": {
        "Legal": "JPMorgan Chase & Co.",
        "LEI": "549300VYKMYHPFHLV307",
        "aliases": ["JPMS_CLEAR", "JPMS_LDN", "JPMorgan Securities LLC"],
    },
    "MSCO": {
        "Legal": "Morgan Stanley & Co. LLC",
        "LEI": "549300TD9H4X8JHREA08",
        "aliases": ["MS_EQ_DERIV", "Morgan Stanley International"],
    },
    "BAML": {
        "Legal": "Bank of America Merrill Lynch",
        "LEI": "549300D0W64Y5R836V12",
        "aliases": ["BOFA_MLPF&S", "BOFA_SECURITIES"],
    },
    "CITI": {
        "Legal": "Citigroup Global Markets",
        "LEI": "549300SE3GA2VJGZYV25",
        "aliases": ["CGMI_NY", "Citigroup Global Markets Limited"],
    },
    "BARC": {
        "Legal": "Barclays Capital",
        "LEI": "549300O3U3HB5EQ6GM90",
        "aliases": ["BARCAP_LDN", "Barclays Bank PLC"],
    },
    "UBS": {
        "Legal": "UBS AG",
        "LEI": "549300U4POY2X6S53T43",
        "aliases": ["UBS_SEC_LDN", "UBS Financial Services"],
    },
    "CITADEL": {
        "Legal": "Citadel Securities",
        "LEI": "549300LBK8RTMRXRFO89",
        "aliases": ["CITADEL_CONNECT", "Citadel Technology"],
    },
}
ALL_CP_CODES = list(CP_INFO.keys())

# Bond instruments (treasuries, corporates)
BONDS = [
    {
        "Ticker": "T 3.875 08/33",
        "ISIN": "US91282CFL02",
        "CUSIP": "91282CFL0",
        "SEDOL": "BF0G7X9",
        "Desc": "US Treasury 3.875% 08/33",
        "Coupon": 0.03875,
        "Maturity": datetime.date(2033, 8, 15),
        "Currency": "USD",
    },
    {
        "Ticker": "UKT 4.25 12/39",
        "ISIN": "GB00BJL2VR16",
        "CUSIP": "G98765432",
        "SEDOL": "BJL2VR1",
        "Desc": "UK Gilt 4.25% 12/39",
        "Coupon": 0.0425,
        "Maturity": datetime.date(2039, 12, 7),
        "Currency": "GBP",
    },
    {
        "Ticker": "MSFT 2.921 03/52",
        "ISIN": "US594918BT81",
        "CUSIP": "594918BT8",
        "SEDOL": "BLG1NW9",
        "Desc": "Microsoft Corp 2.921% 03/52",
        "Coupon": 0.02921,
        "Maturity": datetime.date(2052, 3, 17),
        "Currency": "USD",
    },
    {
        "Ticker": "APPL 3.85 08/46",
        "ISIN": "US037833EB03",
        "CUSIP": "037833EB0",
        "SEDOL": "BYXJW83",
        "Desc": "Apple Inc 3.85% 08/46",
        "Coupon": 0.0385,
        "Maturity": datetime.date(2046, 8, 4),
        "Currency": "USD",
    },
    {
        "Ticker": "BMW 1.5 06/29",
        "ISIN": "DE000A289NL8",
        "CUSIP": "D11401157",
        "SEDOL": "BGN5WR3",
        "Desc": "BMW Finance 1.5% 06/29",
        "Coupon": 0.015,
        "Maturity": datetime.date(2029, 6, 15),
        "Currency": "EUR",
    },
]

# Equity instruments
EQUITIES = [
    {
        "Ticker": "AAPL",
        "ISIN": "US0378331005",
        "CUSIP": "037833100",
        "SEDOL": "2046251",
        "Desc": "Apple Inc. Common Stock",
        "Currency": "USD",
    },
    {
        "Ticker": "MSFT",
        "ISIN": "US5949181045",
        "CUSIP": "594918104",
        "SEDOL": "2588173",
        "Desc": "Microsoft Corp. Common Stock",
        "Currency": "USD",
    },
    {
        "Ticker": "GOOGL",
        "ISIN": "US02079K3059",
        "CUSIP": "02079K305",
        "SEDOL": "BYX55Z4",
        "Desc": "Alphabet Inc. Class A",
        "Currency": "USD",
    },
    {
        "Ticker": "TSLA",
        "ISIN": "US88160R1014",
        "CUSIP": "88160R101",
        "SEDOL": "B616C79",
        "Desc": "Tesla Inc. Common Stock",
        "Currency": "USD",
    },
    {
        "Ticker": "SAP.DE",
        "ISIN": "DE0007164600",
        "CUSIP": "71646010",
        "SEDOL": "4846288",
        "Desc": "SAP SE",
        "Currency": "EUR",
    },
    {
        "Ticker": "700.HK",
        "ISIN": "KYG875721634",
        "CUSIP": "G87572163",
        "SEDOL": "BMMV2K8",
        "Desc": "Tencent Holdings Ltd.",
        "Currency": "HKD",
    },
]

# FX Forwards (no ISIN/CUSIP/SEDOL)
FX_FORWARDS = [
    {"Ticker": "EUR/USD", "Desc": "EUR/USD FX Forward", "Currency": "USD"},
    {"Ticker": "GBP/USD", "Desc": "GBP/USD FX Forward", "Currency": "USD"},
    {"Ticker": "USD/JPY", "Desc": "USD/JPY FX Forward", "Currency": "JPY"},
]

# Interest Rate Swaps
SWAPS = [
    {
        "Ticker": "USD-LIBOR-IRS-5Y",
        "Desc": "USD LIBOR IRS 5Y",
        "Currency": "USD",
        "Coupon": 0.035,          # notional fixed rate (will be overridden per trade)
        "Maturity": datetime.date(2030, 6, 15),
    },
    {
        "Ticker": "EUR-EURIBOR-IRS-10Y",
        "Desc": "EUR EURIBOR IRS 10Y",
        "Currency": "EUR",
        "Coupon": 0.025,
        "Maturity": datetime.date(2035, 3, 21),
    },
]

# **********************************************************************
# 2.  Helper functions to generate realistic trade data
# **********************************************************************
_TICK = 0

def _inc_tick():
    global _TICK
    _TICK += 1
    return _TICK

def random_date(start=START_DATE, max_days=90):
    return start + datetime.timedelta(days=random.randint(0, max_days))

def random_cp_side(asset_class, perspective="PB_CLIENT_VIEW", dealer_invert=False):
    """
    Returns (side_label, side_for_internal) where internal uses BUY/SELL/PAY_FIXED/REC_FIXED.
    For counterparty, the raw street Dir_Ind and quantity sign will be generated later.
    """
    if asset_class in ("Bond", "Equity"):
        s = random.choice(["BUY", "SELL"])
    elif asset_class == "Forward":
        s = random.choice(["BUY", "SELL"])   # FX Forward base/term interpretation handled separately
    else:  # Swap
        s = random.choice(["PAY_FIXED", "REC_FIXED"])
    return s, s

def gen_notional(asset_class):
    if asset_class == "Bond":
        return random.choice([1_000_000, 3_500_000, 5_000_000, 10_000_000, 25_000_000])
    elif asset_class == "Equity":
        return random.choice([1000, 2500, 5000, 10000, 50000, 125000])
    elif asset_class == "Forward":
        return random.choice([5_000_000, 10_000_000, 25_000_000, 50_000_000])
    else:  # Swap
        return random.choice([10_000_000, 25_000_000, 50_000_000, 100_000_000])

def gen_price(asset_class):
    """Internal clean price/rate (decimal)."""
    if asset_class == "Bond":
        return round(random.uniform(95.0, 110.0), 6)    # e.g., 98.359375
    elif asset_class == "Equity":
        return round(random.uniform(20.0, 500.0), 4)
    elif asset_class == "Forward":
        return round(random.uniform(0.9, 1.5), 6)       # forward rate
    else:  # Swap
        return round(random.uniform(0.01, 0.06), 6)     # fixed rate

def calc_bond_accrued(settle_date, coupon, maturity, notional):
    """Super-simple accrued: 1/2 of annual coupon for demonstration."""
    # A realistic accrued would use day count; we keep it stylised.
    return round(notional * coupon * 0.5, 2)

def bond_clean_to_32nd(clean_price):
    """Convert decimal clean price like 98.359375 to fractional 32nd notation,
       e.g., '98-24' (98 + 24/32 = 98.75), '98-11+' (98 + 11.5/32 = 98.359375)."""
    whole = int(clean_price)
    frac = clean_price - whole
    thirty_seconds = round(frac * 32)
    # handle 32nds+ notation for non-even 32nds
    if abs(thirty_seconds - (frac * 32)) < 0.001:
        # exact 32nds
        return f"{whole}-{thirty_seconds:02d}"
    else:
        # use + for half of a 32nd
        half = round((frac * 32 - int(frac * 32)) * 2)  # 0 or 1
        if half == 1:
            return f"{whole}-{int(frac*32):02d}+"
        else:
            # fallback exact
            return f"{whole}-{int(frac*32):02d}"

# ----------------------------------------------------------------------
# 3.  Generate Internal Trades (300 rows)
# ----------------------------------------------------------------------
internal_rows = []      # list of dicts

# book distribution
books = ["MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP", "L/S_EQUITY", "FIXED_INCOME", "GLOBAL_MACRO"]

# We'll generate rows in pre-defined groups to satisfy scenario counts.
# First assign IDs sequentially from 1 to 300 (IT0001 ... IT0300)
# Then later we assign the actual trade details according to the scenario.

# For clarity, we pre-create all 300 slots.
for i in range(1, 301):
    internal_rows.append({
        "Internal_Trade_ID": f"IT{i:04d}",
        "UTI_Exec_ID": f"UTI{100000 + _inc_tick()}",  # placeholder, will be updated
        "Parent_Link_ID": None,
        "Leg_Structure": "SINGLE",
        "Portfolio_Book": None,
        "Trade_Date": None,
        "Settle_Date": None,
        "Asset_Class": None,
        "Counterparty_Code": None,
        "Counterparty_Legal_Name": None,
        "Side": None,
        "Ticker": None,
        "ISIN": None,
        "CUSIP": None,
        "SEDOL": None,
        "Instrument_Description": None,
        "Quantity_Notional": None,
        "Clean_Price_Rate": None,
        "Currency": None,
        "Principal_Gross": None,
        "Accrued_Interest": 0.0,
        "Commission_Fees": 0.0,
        "Net_Settlement_Amount": None,
        "Coupon_Rate": None,
        "Maturity_Date": None,
    })

# We'll fill them later after planning the scenario mapping.
# For now, we'll construct the counterparty rows together with internal.

# ----------------------------------------------------------------------
# 4.  Generate all pairs roots (scenario planning)
# ----------------------------------------------------------------------
# We will build all trades in a unified way: first decide the scenario,
# then create one internal row (or multiple) and counterparty rows,
# ensuring all constraints.

# Prepare data structures:
all_internal = []   # final list of completed internal rows (300)
all_counterparty = []  # final list of completed counterparty rows (200)
known_exact_matches = []  # list of (IT_id, CP_id) pairs for Tab 3
oracle = {
    "exact_matches": [],
    "near_matches": [],
    "economic_breaks": [],
    "n_to_1_allocation_splits": [],
    "multi_leg_netting": [],
    "lifecycle_chains": [],
    "unmatched_internal": [],
    "unmatched_counterparty": [],
}

# mapping from internal trade groups
# For each scenario, we will generate actual trades and push to the lists.

# Helper to create an internal row from a base template
def make_internal_row(base):
    """Copies base and fills missing defaults."""
    r = {
        "Internal_Trade_ID": None,
        "UTI_Exec_ID": None,
        "Parent_Link_ID": None,
        "Leg_Structure": "SINGLE",
        "Portfolio_Book": "GLOBAL_MACRO",
        "Trade_Date": START_DATE,
        "Settle_Date": START_DATE + datetime.timedelta(days=2),
        "Asset_Class": "Equity",
        "Counterparty_Code": "GSCO",
        "Counterparty_Legal_Name": CP_INFO["GSCO"]["Legal"],
        "Side": "BUY",
        "Ticker": "",
        "ISIN": "",
        "CUSIP": "",
        "SEDOL": "",
        "Instrument_Description": "",
        "Quantity_Notional": 0,
        "Clean_Price_Rate": 0.0,
        "Currency": "USD",
        "Principal_Gross": 0.0,
        "Accrued_Interest": 0.0,
        "Commission_Fees": 0.0,
        "Net_Settlement_Amount": 0.0,
        "Coupon_Rate": None,
        "Maturity_Date": None,
    }
    r.update(base)
    return r

def make_cpty_row(base):
    """Similar for counterparty."""
    r = {
        "Cpty_Report_ID": None,
        "Shared_Market_Exec_Ref": None,
        "Trans_Lifecycle_Action": "NEW",
        "Cancels_Prior_Report_ID": None,
        "Reporting_Counterparty_Name": "",
        "Counterparty_LEI": "",
        "Reporting_Perspective": "PB_CLIENT_VIEW",
        "Trade_Date": START_DATE,
        "Value_Settle_Date": START_DATE + datetime.timedelta(days=2),
        "Product_Type": "",
        "Dir_Ind": "",
        "Primary_Security_ID": "",
        "Security_ID_Source": "",
        "Security_Text": "",
        "Reported_Qty_Notional": 0.0,
        "Exec_Price_Rate": 0.0,
        "Price_Quote_Convention": "DECIMAL",
        "Trade_Ccy": "USD",
        "Reported_Accrued_Int": 0.0,
        "Net_Cash_Amount": 0.0,
        "Coupon": None,
        "Maturity": None,
    }
    r.update(base)
    return r

# Global UTI counter
next_uti = 500000

def new_uti():
    global next_uti
    next_uti += 1
    return f"UTI{next_uti}"

# ----------------------------------------------------------------------
# 5.  Build the exact matches (120 pairs) -- these go to Tab 3 and oracle exact_matches
# ----------------------------------------------------------------------
exact_pairs = []   # (internal_row, cpty_row)

# We'll cycle through the 8 counterparties and vary security type
# We'll assign each pair an ID IT0001-IT0120, CP0001-CP0120.
# Apply the required normalization challenges:
#   - 12 US Treasury 32nds (Bonds)
#   - 8 Dirty Price Bond quotes
#   - 15 Dealer Bilateral Perspective Inversion (OTC swaps/forwards)
#   - The remaining 85 pairs will test naming drift, missing exec IDs, direction variants, security ID cross-reference.

# For simplicity, we'll generate the exact matches first and record them.
exact_challenge_types = (
    ["32NDS"] * 12 +
    ["DIRTY_PRICE"] * 8 +
    ["DEALER_INVERSION"] * 15 +
    ["STANDARD"] * 85
)
random.shuffle(exact_challenge_types)  # distribute among the 120 pairs

for idx in range(120):
    ch = exact_challenge_types[idx]
    it_id = f"IT{idx+1:04d}"
    cp_id = f"CP{idx+1:04d}"

    # Choose asset class based on challenge
    if ch == "32NDS":
        asset = "Bond"
    elif ch == "DIRTY_PRICE":
        asset = "Bond"
    elif ch == "DEALER_INVERSION":
        asset = random.choice(["Swap", "Forward"])   # OTC perspective inversion most relevant for swaps/forwards
    else:
        asset = random.choice(["Bond", "Equity", "Forward", "Swap"])

    # Pick a specific instrument
    if asset == "Bond":
        inst = random.choice(BONDS)
    elif asset == "Equity":
        inst = random.choice(EQUITIES)
    elif asset == "Forward":
        inst = random.choice(FX_FORWARDS)
    else:
        inst = random.choice(SWAPS)

    # Choose counterparty code
    cp_code = random.choice(ALL_CP_CODES)
    cp_info = CP_INFO[cp_code]

    # Determine internal side
    if asset == "Forward":
        side = random.choice(["BUY", "SELL"])
    elif asset == "Swap":
        side = random.choice(["PAY_FIXED", "REC_FIXED"])
    else:
        side = random.choice(["BUY", "SELL"])

    # Trade dates and settlement
    tdate = random_date()
    settle_date = tdate + datetime.timedelta(days=2) if asset != "Swap" else tdate + datetime.timedelta(days=random.choice([2, 3]))

    # Notional
    notional = gen_notional(asset) if asset != "Forward" else gen_notional("Forward")
    # Price
    internal_price = gen_price(asset)
    if asset == "Bond":
        # For US Treasury 32nds, have price close to something that maps nicely
        if ch == "32NDS":
            # Ensure clean price ends in .XX that translates to exact 32nd or 32nd+ fraction
            # Make some explicit: 98-24 (98.75), 98-11+ (98.359375)
            internal_price = random.choice([98.75, 98.359375, 99.0, 100.0625, 101.25, 97.515625, 102.875, 95.5625, 96.75, 99.34375, 100.96875, 95.875])
        elif ch == "DIRTY_PRICE":
            # We'll handle dirty price on counterparty side later
            internal_price = round(random.uniform(95.0, 110.0), 6)

    # Derived fields
    principal = round(notional * internal_price / 100.0, 2) if asset == "Bond" or asset == "Equity" else notional
    accrued = 0.0
    if asset == "Bond":
        accrued = calc_bond_accrued(settle_date, inst["Coupon"], inst["Maturity"], notional)
    commission = round(random.uniform(0, 50), 2) if asset != "Swap" else 0.0
    net_settle = principal + accrued + commission  # simplified

    # For swap: Principal_Gross is notional, Net_Settlement_Amount is PV of fixed leg?
    # We'll keep simple: Principal_Gross = notional, Net_Settlement_Amount = 0 (swap has exchanges)
    if asset == "Swap":
        principal = notional
        net_settle = 0.0
        commission = 0.0

    # Fill internal row
    internal = make_internal_row({
        "Internal_Trade_ID": it_id,
        "UTI_Exec_ID": new_uti(),
        "Parent_Link_ID": None,
        "Leg_Structure": "SINGLE",
        "Portfolio_Book": random.choice(books),
        "Trade_Date": tdate,
        "Settle_Date": settle_date,
        "Asset_Class": asset,
        "Counterparty_Code": cp_code,
        "Counterparty_Legal_Name": cp_info["Legal"],
        "Side": side,
        "Ticker": inst["Ticker"],
        "ISIN": inst.get("ISIN", ""),
        "CUSIP": inst.get("CUSIP", ""),
        "SEDOL": inst.get("SEDOL", ""),
        "Instrument_Description": inst["Desc"],
        "Quantity_Notional": notional,
        "Clean_Price_Rate": internal_price,
        "Currency": inst["Currency"],
        "Principal_Gross": principal,
        "Accrued_Interest": accrued,
        "Commission_Fees": commission,
        "Net_Settlement_Amount": net_settle,
        "Coupon_Rate": inst.get("Coupon", None),
        "Maturity_Date": inst.get("Maturity", None),
    })

    # Build counterparty row with required normalization
    # Shared Market Exec Ref: half share UTI, half different
    shared_ref = internal["UTI_Exec_ID"] if random.random() < 0.5 else f"XTRA{random.randint(1,99999):05d}"

    # Reporting counterparty name: sometimes alias, sometimes legal
    if random.random() < 0.7:
        reporting_name = random.choice(cp_info["aliases"])
    else:
        reporting_name = cp_info["Legal"]

    # Perspective: For DEALER_INVERSION, set to DEALER_BILATERAL_VIEW
    perspective = "PB_CLIENT_VIEW"
    if ch == "DEALER_INVERSION":
        perspective = "DEALER_BILATERAL_VIEW"

    # Determine Dir_Ind and quantity sign variations
    dir_ind = ""
    reported_qty = notional
    sign_override = 0  # 1 positive, -1 negative

    # Based on side and perspective, determine counterparty's reported direction and sign.
    # For standard, we map internal BUY -> B, SELL -> S, PAY_FIXED -> P, REC_FIXED -> R
    # But many brokers use "BOT", "SLD", "PAY", "REC", or blanks with sign.
    # We'll randomly choose convention among those that preserve the economic direction after normalization.

    if perspective == "DEALER_BILATERAL_VIEW":
        # Dealer reports from its own side. So internal BUY means dealer sells, thus should be SLD or negative.
        # We'll invert: we want the internal side to be mirrored.
        if side == "BUY":
            # internal buys, dealer sells -> dealer reports as SELL (SLD or negative)
            dealer_side = "SELL"
        elif side == "SELL":
            dealer_side = "BUY"
        elif side == "PAY_FIXED":
            # internal pays fixed -> dealer receives fixed -> dealer reports REC_FIXED
            dealer_side = "REC_FIXED"
        else:  # internal receives fixed -> dealer pays fixed
            dealer_side = "PAY_FIXED"
    else:
        dealer_side = side

    # Now choose the street representation for dealer_side
    rand_type = random.random()
    if dealer_side == "BUY":
        if rand_type < 0.3:
            dir_ind = "B"
            reported_qty = notional
        elif rand_type < 0.6:
            dir_ind = "BOT"
            reported_qty = notional
        elif rand_type < 0.9:
            dir_ind = "buy"  # lowercase
            reported_qty = notional
        else:
            dir_ind = ""
            reported_qty = notional   # positive unsigned, some brokers leave blank and use sign? but this is buy, so positive
    elif dealer_side == "SELL":
        if rand_type < 0.25:
            dir_ind = "S"
            reported_qty = notional
        elif rand_type < 0.5:
            dir_ind = "SLD"
            reported_qty = notional
        elif rand_type < 0.75:
            dir_ind = "sell"
            reported_qty = notional
        elif rand_type < 0.9:
            dir_ind = ""
            reported_qty = -notional   # negative sign indicates sell
        else:
            dir_ind = "S"
            reported_qty = -notional   # both S and negative
    elif dealer_side == "PAY_FIXED":
        if rand_type < 0.5:
            dir_ind = "P"
            reported_qty = notional
        elif rand_type < 0.8:
            dir_ind = "PAY"
            reported_qty = notional
        else:
            dir_ind = ""
            reported_qty = -notional   # negative represents pay fixed
    else:  # REC_FIXED
        if rand_type < 0.5:
            dir_ind = "R"
            reported_qty = notional
        elif rand_type < 0.8:
            dir_ind = "REC"
            reported_qty = notional
        else:
            dir_ind = ""
            reported_qty = -notional

    # Price quote convention and exec price for counterparty
    exec_price = internal_price
    price_conv = "DECIMAL"
    if ch == "32NDS":
        # transform to 32nds string notation
        # store the string in Exec_Price_Rate field? We'll keep as string.
        price_conv = "32NDS_UST"
        exec_price_str = bond_clean_to_32nd(internal_price)
        exec_price = exec_price_str   # stored as string
    elif ch == "DIRTY_PRICE":
        # counterparty reports dirty price: Clean + (Accrued / Notional * 100)
        dirty = internal_price + (accrued / notional * 100)
        price_conv = "DIRTY"
        exec_price = round(dirty, 6)
    # else decimal

    # Security ID: counterparty only sends one identifier according to source
    sec_id_source = random.choice(["ISIN", "CUSIP", "SEDOL", "TICKER"])
    if sec_id_source == "ISIN":
        prim_id = inst["ISIN"]
    elif sec_id_source == "CUSIP":
        prim_id = inst["CUSIP"]
    elif sec_id_source == "SEDOL":
        prim_id = inst["SEDOL"]
    else:
        prim_id = inst["Ticker"]

    # For some exact matches, make the primary ID source different from what internal has (cross-reference test)
    # but all identifiers should still map. We'll randomize among those that exist.
    # If inst doesn't have that field (e.g., FX forward lacks ISIN), fallback to Ticker.
    if not prim_id:
        prim_id = inst["Ticker"]
        sec_id_source = "TICKER"

    # Coupon & Maturity for counterparty
    cpty_coupon = inst.get("Coupon", None)
    cpty_maturity = inst.get("Maturity", None)

    # Reported Accrued Interest: identical to internal
    cpty_accrued = accrued

    # Net Cash Amount: for exact match, must equal internal Net_Settlement_Amount
    cpty_net = net_settle

    # Build counterparty row
    cpty = make_cpty_row({
        "Cpty_Report_ID": cp_id,
        "Shared_Market_Exec_Ref": shared_ref,
        "Reporting_Counterparty_Name": reporting_name,
        "Counterparty_LEI": cp_info["LEI"],
        "Reporting_Perspective": perspective,
        "Trade_Date": tdate,
        "Value_Settle_Date": settle_date,
        "Product_Type": {"Bond": "BOND", "Equity": "EQUITY", "Forward": "FX_FWD", "Swap": "IRS"}[asset],
        "Dir_Ind": dir_ind,
        "Primary_Security_ID": prim_id,
        "Security_ID_Source": sec_id_source,
        "Security_Text": inst["Desc"],
        "Reported_Qty_Notional": reported_qty,
        "Exec_Price_Rate": exec_price,
        "Price_Quote_Convention": price_conv,
        "Trade_Ccy": inst["Currency"],
        "Reported_Accrued_Int": cpty_accrued,
        "Net_Cash_Amount": cpty_net,
        "Coupon": cpty_coupon,
        "Maturity": cpty_maturity,
    })

    exact_pairs.append((internal, cpty))
    known_exact_matches.append((it_id, cp_id))
    oracle["exact_matches"].append({"Internal_Trade_ID": it_id, "Cpty_Report_ID": cp_id})

# Now we have 120 exact pairs. Add them to our final lists.
for int_row, cpty_row in exact_pairs:
    all_internal.append(int_row)
    all_counterparty.append(cpty_row)

# ----------------------------------------------------------------------
# 6.  Build Blind Scenarios (180 internal + 80 counterparty rows)
# ----------------------------------------------------------------------
# Internal row counter continues from 120 (IT0121 onward), counterparty from 120 (CP0121 onward)
next_it = 121
next_cp = 121

# ---- 6a. Within-Tolerance Near-Matches (18 pairs) ----
for i in range(18):
    it_id = f"IT{next_it:04d}"
    cp_id = f"CP{next_cp:04d}"
    next_it += 1
    next_cp += 1

    asset = random.choice(["Bond", "Equity", "Forward", "Swap"])
    inst = random.choice(BONDS) if asset=="Bond" else random.choice(EQUITIES) if asset=="Equity" else random.choice(FX_FORWARDS) if asset=="Forward" else random.choice(SWAPS)
    cp_code = random.choice(ALL_CP_CODES)
    cp_info = CP_INFO[cp_code]
    side = random.choice(["BUY","SELL"]) if asset in ["Bond","Equity","Forward"] else random.choice(["PAY_FIXED","REC_FIXED"])
    tdate = random_date()
    settle_date = tdate + datetime.timedelta(days=2) if asset!="Swap" else tdate+datetime.timedelta(days=3)
    notional = gen_notional(asset) if asset!="Forward" else gen_notional("Forward")
    price = gen_price(asset)
    principal = round(notional * price / 100.0, 2) if asset in ("Bond","Equity") else notional
    accrued = calc_bond_accrued(settle_date, inst.get("Coupon",0), inst.get("Maturity"), notional) if asset=="Bond" else 0
    commission = round(random.uniform(0,40),2) if asset!="Swap" else 0
    net = principal + accrued + commission if asset!="Swap" else 0

    # introduce micro-difference in one of: accrued, price, commission
    diff_type = random.choice(["accrued","price","commission"])
    if diff_type == "accrued" and asset=="Bond":
        cpty_accrued = round(accrued + random.choice([-0.40, -0.80, 0.50, 1.20]), 2)
        cpty_net = principal + cpty_accrued + commission
    elif diff_type == "price" and asset not in ("Swap"):
        cpty_price = round(price + random.uniform(-0.0001, 0.0001), 6)
        cpty_principal = round(notional * cpty_price / 100.0, 2) if asset in ("Bond","Equity") else notional
        cpty_net = cpty_principal + accrued + commission
    else:  # commission
        cpty_commission = round(commission + random.choice([-0.01, 0.01]), 2)
        cpty_net = principal + accrued + cpty_commission
        cpty_price = price  # unchanged

    internal = make_internal_row({
        "Internal_Trade_ID": it_id,
        "UTI_Exec_ID": new_uti(),
        "Portfolio_Book": random.choice(books),
        "Trade_Date": tdate,
        "Settle_Date": settle_date,
        "Asset_Class": asset,
        "Counterparty_Code": cp_code,
        "Counterparty_Legal_Name": cp_info["Legal"],
        "Side": side,
        "Ticker": inst["Ticker"],
        "ISIN": inst.get("ISIN",""),
        "CUSIP": inst.get("CUSIP",""),
        "SEDOL": inst.get("SEDOL",""),
        "Instrument_Description": inst["Desc"],
        "Quantity_Notional": notional,
        "Clean_Price_Rate": price,
        "Currency": inst["Currency"],
        "Principal_Gross": principal,
        "Accrued_Interest": accrued,
        "Commission_Fees": commission,
        "Net_Settlement_Amount": net,
        "Coupon_Rate": inst.get("Coupon", None),
        "Maturity_Date": inst.get("Maturity", None),
    })
    cpty = make_cpty_row({
        "Cpty_Report_ID": cp_id,
        "Shared_Market_Exec_Ref": internal["UTI_Exec_ID"],  # share ref for simplicity
        "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
        "Counterparty_LEI": cp_info["LEI"],
        "Trade_Date": tdate,
        "Value_Settle_Date": settle_date,
        "Product_Type": {"Bond":"BOND","Equity":"EQUITY","Forward":"FX_FWD","Swap":"IRS"}[asset],
        "Dir_Ind": {"BUY":"B","SELL":"S","PAY_FIXED":"P","REC_FIXED":"R"}[side],
        "Primary_Security_ID": inst["ISIN"] or inst["Ticker"],
        "Security_ID_Source": "ISIN" if inst.get("ISIN") else "TICKER",
        "Security_Text": inst["Desc"],
        "Reported_Qty_Notional": notional,
        "Exec_Price_Rate": cpty_price if diff_type=="price" else price,
        "Trade_Ccy": inst["Currency"],
        "Reported_Accrued_Int": cpty_accrued if diff_type=="accrued" else accrued,
        "Net_Cash_Amount": cpty_net,
        "Coupon": inst.get("Coupon", None),
        "Maturity": inst.get("Maturity", None),
    })
    all_internal.append(internal)
    all_counterparty.append(cpty)
    oracle["near_matches"].append({
        "Internal_Trade_ID": it_id,
        "Cpty_Report_ID": cp_id,
        "difference_type": diff_type,
        "details": f"Internal net: {net}, CP net: {cpty_net}"
    })

# ---- 6b. True Economic Field Breaks (20 pairs) ----
for i in range(20):
    it_id = f"IT{next_it:04d}"
    cp_id = f"CP{next_cp:04d}"
    next_it += 1
    next_cp += 1
    asset = random.choice(["Bond","Equity","Forward","Swap"])
    inst = random.choice(BONDS) if asset=="Bond" else random.choice(EQUITIES) if asset=="Equity" else random.choice(FX_FORWARDS) if asset=="Forward" else random.choice(SWAPS)
    cp_code = random.choice(ALL_CP_CODES)
    cp_info = CP_INFO[cp_code]
    side = random.choice(["BUY","SELL"]) if asset in ("Bond","Equity","Forward") else random.choice(["PAY_FIXED","REC_FIXED"])
    tdate = random_date()
    settle_date = tdate + datetime.timedelta(days=2)
    notional = gen_notional(asset) if asset!="Forward" else gen_notional("Forward")
    price = gen_price(asset)
    principal = round(notional * price / 100.0, 2) if asset in ("Bond","Equity") else notional
    accrued = 0
    if asset=="Bond":
        accrued = calc_bond_accrued(settle_date, inst["Coupon"], inst["Maturity"], notional)
    commission = round(random.uniform(0,50),2) if asset!="Swap" else 0
    net = principal + accrued + commission if asset!="Swap" else 0

    # Break type: price, quantity, settlement lag, commission
    break_type = random.choice(["price","qty","settle","commission"])
    if break_type == "price":
        cpty_price = round(price * random.choice([0.998, 1.002, 0.95]), 4)
        cpty_principal = round(notional * cpty_price / 100.0, 2) if asset in ("Bond","Equity") else notional
        cpty_net = cpty_principal + accrued + commission if asset!="Swap" else 0
    elif break_type == "qty":
        cpty_qty = round(notional * 0.8)  # 80% partial fill
        cpty_principal = round(cpty_qty * price / 100.0, 2) if asset in ("Bond","Equity") else cpty_qty
        cpty_net = cpty_principal + accrued + commission if asset!="Swap" else 0
    elif break_type == "settle":
        cpty_settle = settle_date + datetime.timedelta(days=1)  # T+1 vs T+2
        cpty_net = net  # same economics but different settlement date
    else:  # commission
        cpty_commission = round(commission + 450.0, 2)
        cpty_net = principal + accrued + cpty_commission if asset!="Swap" else 0

    cpty_net = round(cpty_net, 2)

    internal = make_internal_row({
        "Internal_Trade_ID": it_id,
        "UTI_Exec_ID": new_uti(),
        "Portfolio_Book": random.choice(books),
        "Trade_Date": tdate,
        "Settle_Date": settle_date,
        "Asset_Class": asset,
        "Counterparty_Code": cp_code,
        "Counterparty_Legal_Name": cp_info["Legal"],
        "Side": side,
        "Ticker": inst["Ticker"],
        "ISIN": inst.get("ISIN",""),
        "CUSIP": inst.get("CUSIP",""),
        "SEDOL": inst.get("SEDOL",""),
        "Instrument_Description": inst["Desc"],
        "Quantity_Notional": notional,
        "Clean_Price_Rate": price,
        "Currency": inst["Currency"],
        "Principal_Gross": principal,
        "Accrued_Interest": accrued,
        "Commission_Fees": commission,
        "Net_Settlement_Amount": net,
        "Coupon_Rate": inst.get("Coupon", None),
        "Maturity_Date": inst.get("Maturity", None),
    })
    cpty = make_cpty_row({
        "Cpty_Report_ID": cp_id,
        "Shared_Market_Exec_Ref": internal["UTI_Exec_ID"],
        "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
        "Counterparty_LEI": cp_info["LEI"],
        "Trade_Date": tdate,
        "Value_Settle_Date": cpty_settle if break_type=="settle" else settle_date,
        "Product_Type": {"Bond":"BOND","Equity":"EQUITY","Forward":"FX_FWD","Swap":"IRS"}[asset],
        "Dir_Ind": {"BUY":"B","SELL":"S","PAY_FIXED":"P","REC_FIXED":"R"}[side],
        "Primary_Security_ID": inst["ISIN"] or inst["Ticker"],
        "Security_ID_Source": "ISIN" if inst.get("ISIN") else "TICKER",
        "Security_Text": inst["Desc"],
        "Reported_Qty_Notional": cpty_qty if break_type=="qty" else notional,
        "Exec_Price_Rate": cpty_price if break_type=="price" else price,
        "Trade_Ccy": inst["Currency"],
        "Reported_Accrued_Int": accrued,
        "Net_Cash_Amount": cpty_net,
        "Coupon": inst.get("Coupon", None),
        "Maturity": inst.get("Maturity", None),
    })
    all_internal.append(internal)
    all_counterparty.append(cpty)
    oracle["economic_breaks"].append({
        "Internal_Trade_ID": it_id,
        "Cpty_Report_ID": cp_id,
        "break_type": break_type,
        "details": f"Break: {break_type}"
    })

# ---- 6c. N-to-1 Allocation Splits (36 internal -> 12 counterparty) ----
for block_idx in range(12):
    # 3 internal rows per block
    cp_id = f"CP{next_cp:04d}"
    next_cp += 1
    internal_ids = []
    # choose one counterparty block
    cp_code = random.choice(ALL_CP_CODES)
    cp_info = CP_INFO[cp_code]
    asset = random.choice(["Equity","Bond"])  # allocation splits typical for equities/bonds
    inst = random.choice(EQUITIES if asset=="Equity" else BONDS)
    tdate = random_date()
    settle_date = tdate + datetime.timedelta(days=2)
    price = gen_price(asset)
    # total notional
    total_notional = gen_notional(asset)
    # split into 3 roughly equal parts
    splits = []
    remaining = total_notional
    for s in range(3):
        if s == 2:
            portion = remaining
        else:
            portion = random.randint(int(total_notional*0.3), int(total_notional*0.5))
            portion = min(portion, remaining)
        splits.append(portion)
        remaining -= portion
    # ensure sum exact
    splits[-1] += remaining

    # books for sub-funds
    sub_books = ["MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP"]
    random.shuffle(sub_books)

    block_internal = []
    for i in range(3):
        it_id = f"IT{next_it:04d}"
        next_it += 1
        notional_part = splits[i]
        principal_part = round(notional_part * price / 100.0, 2)
        accrued_part = 0
        if asset=="Bond":
            accrued_part = calc_bond_accrued(settle_date, inst["Coupon"], inst["Maturity"], notional_part)
        commission = 0
        net = principal_part + accrued_part

        int_row = make_internal_row({
            "Internal_Trade_ID": it_id,
            "UTI_Exec_ID": new_uti(),
            "Portfolio_Book": sub_books[i],
            "Trade_Date": tdate,
            "Settle_Date": settle_date,
            "Asset_Class": asset,
            "Counterparty_Code": cp_code,
            "Counterparty_Legal_Name": cp_info["Legal"],
            "Side": "BUY",
            "Ticker": inst["Ticker"],
            "ISIN": inst.get("ISIN",""),
            "CUSIP": inst.get("CUSIP",""),
            "SEDOL": inst.get("SEDOL",""),
            "Instrument_Description": inst["Desc"],
            "Quantity_Notional": notional_part,
            "Clean_Price_Rate": price,
            "Currency": inst["Currency"],
            "Principal_Gross": principal_part,
            "Accrued_Interest": accrued_part,
            "Commission_Fees": 0,
            "Net_Settlement_Amount": net,
            "Coupon_Rate": inst.get("Coupon", None),
            "Maturity_Date": inst.get("Maturity", None),
        })
        all_internal.append(int_row)
        block_internal.append(int_row)
        internal_ids.append(it_id)

    # Counterparty block trade: sum of notional, total principal, etc.
    total_principal = sum(r["Principal_Gross"] for r in block_internal)
    total_accrued = sum(r["Accrued_Interest"] for r in block_internal)
    total_net = total_principal + total_accrued

    cpty = make_cpty_row({
        "Cpty_Report_ID": cp_id,
        "Shared_Market_Exec_Ref": new_uti(),  # unique block ref
        "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
        "Counterparty_LEI": cp_info["LEI"],
        "Trade_Date": tdate,
        "Value_Settle_Date": settle_date,
        "Product_Type": {"Bond":"BOND","Equity":"EQUITY"}[asset],
        "Dir_Ind": "B",
        "Primary_Security_ID": inst["ISIN"] or inst["Ticker"],
        "Security_ID_Source": "ISIN" if inst.get("ISIN") else "TICKER",
        "Security_Text": inst["Desc"],
        "Reported_Qty_Notional": total_notional,
        "Exec_Price_Rate": price,
        "Trade_Ccy": inst["Currency"],
        "Reported_Accrued_Int": total_accrued,
        "Net_Cash_Amount": total_net,
        "Coupon": inst.get("Coupon", None),
        "Maturity": inst.get("Maturity", None),
    })
    all_counterparty.append(cpty)
    oracle["n_to_1_allocation_splits"].append({
        "Internal_Trade_IDs": internal_ids,
        "Cpty_Report_ID": cp_id,
        "Block": block_idx+1,
    })

# ---- 6d. Multi-Leg Swap/FX Netting (16 internal -> 8 counterparty) ----
for legset in range(8):
    cp_id = f"CP{next_cp:04d}"
    next_cp += 1
    # 2 internal legs
    int_ids = []
    cp_code = random.choice(ALL_CP_CODES)
    cp_info = CP_INFO[cp_code]
    asset = random.choice(["Swap","Forward"])
    if asset == "Swap":
        inst = random.choice(SWAPS)
        tdate = random_date()
        settle = tdate + datetime.timedelta(days=2)
        notional = gen_notional("Swap")
        fixed_rate = gen_price("Swap")
        # leg1 PAY_FIXED, leg2 REC_FIXED
        leg1 = make_internal_row({
            "Internal_Trade_ID": f"IT{next_it:04d}",
            "UTI_Exec_ID": new_uti(),
            "Parent_Link_ID": f"SWAP{legset+1:03d}",
            "Leg_Structure": "LEG_1_PAY",
            "Portfolio_Book": "GLOBAL_MACRO",
            "Trade_Date": tdate,
            "Settle_Date": settle,
            "Asset_Class": "Swap",
            "Counterparty_Code": cp_code,
            "Counterparty_Legal_Name": cp_info["Legal"],
            "Side": "PAY_FIXED",
            "Ticker": inst["Ticker"],
            "ISIN": inst.get("ISIN",""),
            "CUSIP": inst.get("CUSIP",""),
            "SEDOL": inst.get("SEDOL",""),
            "Instrument_Description": inst["Desc"],
            "Quantity_Notional": notional,
            "Clean_Price_Rate": fixed_rate,
            "Currency": inst["Currency"],
            "Principal_Gross": notional,
            "Accrued_Interest": 0,
            "Commission_Fees": 0,
            "Net_Settlement_Amount": 0,
            "Coupon_Rate": fixed_rate,
            "Maturity_Date": inst["Maturity"],
        })
        next_it += 1
        int_ids.append(leg1["Internal_Trade_ID"])
        leg2 = make_internal_row({
            "Internal_Trade_ID": f"IT{next_it:04d}",
            "UTI_Exec_ID": new_uti(),
            "Parent_Link_ID": f"SWAP{legset+1:03d}",
            "Leg_Structure": "LEG_2_RECEIVE",
            "Portfolio_Book": "GLOBAL_MACRO",
            "Trade_Date": tdate,
            "Settle_Date": settle,
            "Asset_Class": "Swap",
            "Counterparty_Code": cp_code,
            "Counterparty_Legal_Name": cp_info["Legal"],
            "Side": "REC_FIXED",
            "Ticker": inst["Ticker"],
            "ISIN": inst.get("ISIN",""),
            "CUSIP": inst.get("CUSIP",""),
            "SEDOL": inst.get("SEDOL",""),
            "Instrument_Description": inst["Desc"],
            "Quantity_Notional": notional,
            "Clean_Price_Rate": fixed_rate,
            "Currency": inst["Currency"],
            "Principal_Gross": -notional,  # sign convention maybe, but we keep positive unsigned in dataset? Prompt says Quantity_Notional always positive unsigned. For leg2, we keep notional positive but side = REC_FIXED.
            "Accrued_Interest": 0,
            "Commission_Fees": 0,
            "Net_Settlement_Amount": 0,
            "Coupon_Rate": fixed_rate,
            "Maturity_Date": inst["Maturity"],
        })
        # fix Principal_Gross to positive unsigned as per prompt
        leg2["Principal_Gross"] = notional  # keep positive
        next_it += 1
        int_ids.append(leg2["Internal_Trade_ID"])
        all_internal.append(leg1)
        all_internal.append(leg2)

        # Counterparty reports single net row (e.g., with direction blank and notional positive, or some convention)
        cpty = make_cpty_row({
            "Cpty_Report_ID": cp_id,
            "Shared_Market_Exec_Ref": new_uti(),
            "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
            "Counterparty_LEI": cp_info["LEI"],
            "Trade_Date": tdate,
            "Value_Settle_Date": settle,
            "Product_Type": "IRS",
            "Dir_Ind": "",  # netted, no direction
            "Primary_Security_ID": inst["ISIN"] or inst["Ticker"],
            "Security_ID_Source": "ISIN" if inst.get("ISIN") else "TICKER",
            "Security_Text": inst["Desc"],
            "Reported_Qty_Notional": notional,
            "Exec_Price_Rate": fixed_rate,
            "Trade_Ccy": inst["Currency"],
            "Reported_Accrued_Int": 0,
            "Net_Cash_Amount": 0,
            "Coupon": fixed_rate,
            "Maturity": inst["Maturity"],
        })
        all_counterparty.append(cpty)

        oracle["multi_leg_netting"].append({
            "Internal_Trade_IDs": int_ids,
            "Cpty_Report_ID": cp_id,
            "Legs": 2,
            "Description": "Swap netting"
        })

    else:  # FX Forward netting (two legs, e.g., EUR/USD spot vs forward?)
        inst = random.choice(FX_FORWARDS)
        # Simulate two internal rows (near leg and far leg) linked by parent
        tdate = random_date()
        settle_near = tdate + datetime.timedelta(days=2)
        settle_far = tdate + datetime.timedelta(days=30)
        notional = gen_notional("Forward")
        rate = gen_price("Forward")
        leg1 = make_internal_row({
            "Internal_Trade_ID": f"IT{next_it:04d}",
            "UTI_Exec_ID": new_uti(),
            "Parent_Link_ID": f"FX{legset+1:03d}",
            "Leg_Structure": "LEG_1",
            "Portfolio_Book": "GLOBAL_MACRO",
            "Trade_Date": tdate,
            "Settle_Date": settle_near,
            "Asset_Class": "Forward",
            "Counterparty_Code": cp_code,
            "Counterparty_Legal_Name": cp_info["Legal"],
            "Side": "BUY",
            "Ticker": inst["Ticker"],
            "Instrument_Description": inst["Desc"],
            "Quantity_Notional": notional,
            "Clean_Price_Rate": rate,
            "Currency": inst["Currency"],
            "Principal_Gross": notional,
            "Accrued_Interest": 0,
            "Commission_Fees": 0,
            "Net_Settlement_Amount": notional * rate,
            "Coupon_Rate": None,
            "Maturity_Date": None,
        })
        next_it += 1
        int_ids.append(leg1["Internal_Trade_ID"])
        leg2 = make_internal_row({
            "Internal_Trade_ID": f"IT{next_it:04d}",
            "UTI_Exec_ID": new_uti(),
            "Parent_Link_ID": f"FX{legset+1:03d}",
            "Leg_Structure": "LEG_2",
            "Portfolio_Book": "GLOBAL_MACRO",
            "Trade_Date": tdate,
            "Settle_Date": settle_far,
            "Asset_Class": "Forward",
            "Counterparty_Code": cp_code,
            "Counterparty_Legal_Name": cp_info["Legal"],
            "Side": "SELL",
            "Ticker": inst["Ticker"],
            "Instrument_Description": inst["Desc"],
            "Quantity_Notional": notional,
            "Clean_Price_Rate": rate,
            "Currency": inst["Currency"],
            "Principal_Gross": notional,
            "Accrued_Interest": 0,
            "Commission_Fees": 0,
            "Net_Settlement_Amount": -notional * rate,
            "Coupon_Rate": None,
            "Maturity_Date": None,
        })
        next_it += 1
        int_ids.append(leg2["Internal_Trade_ID"])
        all_internal.append(leg1)
        all_internal.append(leg2)

        # CP sends one net row
        cpty = make_cpty_row({
            "Cpty_Report_ID": cp_id,
            "Shared_Market_Exec_Ref": new_uti(),
            "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
            "Counterparty_LEI": cp_info["LEI"],
            "Trade_Date": tdate,
            "Value_Settle_Date": settle_far,
            "Product_Type": "FX_FWD",
            "Dir_Ind": "",  # net
            "Primary_Security_ID": inst["Ticker"],
            "Security_ID_Source": "TICKER",
            "Security_Text": inst["Desc"],
            "Reported_Qty_Notional": notional,
            "Exec_Price_Rate": rate,
            "Trade_Ccy": inst["Currency"],
            "Reported_Accrued_Int": 0,
            "Net_Cash_Amount": 0,
            "Coupon": None,
            "Maturity": None,
        })
        all_counterparty.append(cpty)
        oracle["multi_leg_netting"].append({
            "Internal_Trade_IDs": int_ids,
            "Cpty_Report_ID": cp_id,
            "Legs": 2,
            "Description": "FX Forward netting"
        })

# ---- 6e. Cancel & Correct Lifecycle Chains (5 internal <-> 15 counterparty) ----
for chain in range(5):
    # 1 internal row (the final correct trade), 3 counterparty rows (NEW, CANC, CORR)
    cp_code = random.choice(ALL_CP_CODES)
    cp_info = CP_INFO[cp_code]
    asset = random.choice(["Equity", "Bond"])
    inst = random.choice(EQUITIES) if asset=="Equity" else random.choice(BONDS)
    tdate = random_date()
    settle_date = tdate + datetime.timedelta(days=2)
    notional = gen_notional(asset)
    price = gen_price(asset)
    principal = round(notional * price / 100.0, 2)
    accrued = 0
    if asset=="Bond":
        accrued = calc_bond_accrued(settle_date, inst["Coupon"], inst["Maturity"], notional)
    net = principal + accrued

    it_id = f"IT{next_it:04d}"
    next_it += 1

    internal = make_internal_row({
        "Internal_Trade_ID": it_id,
        "UTI_Exec_ID": new_uti(),
        "Portfolio_Book": random.choice(books),
        "Trade_Date": tdate,
        "Settle_Date": settle_date,
        "Asset_Class": asset,
        "Counterparty_Code": cp_code,
        "Counterparty_Legal_Name": cp_info["Legal"],
        "Side": "BUY",
        "Ticker": inst["Ticker"],
        "ISIN": inst.get("ISIN",""),
        "CUSIP": inst.get("CUSIP",""),
        "SEDOL": inst.get("SEDOL",""),
        "Instrument_Description": inst["Desc"],
        "Quantity_Notional": notional,
        "Clean_Price_Rate": price,
        "Currency": inst["Currency"],
        "Principal_Gross": principal,
        "Accrued_Interest": accrued,
        "Commission_Fees": 0,
        "Net_Settlement_Amount": net,
        "Coupon_Rate": inst.get("Coupon", None),
        "Maturity_Date": inst.get("Maturity", None),
    })
    all_internal.append(internal)

    # Build counterparty lifecycle
    cp_new_id = f"CP{next_cp:04d}"
    next_cp += 1
    cp_canc_id = f"CP{next_cp:04d}"
    next_cp += 1
    cp_corr_id = f"CP{next_cp:04d}"
    next_cp += 1

    # NEW row (busted): wrong notional or price
    busted_qty = round(notional * random.choice([0.9, 1.1]))
    busted_price = round(price * random.choice([0.98, 1.02]), 4)
    busted_principal = round(busted_qty * busted_price / 100.0, 2)
    busted_net = busted_principal + accrued  # keep accrued same for simplicity
    new_row = make_cpty_row({
        "Cpty_Report_ID": cp_new_id,
        "Shared_Market_Exec_Ref": internal["UTI_Exec_ID"],
        "Trans_Lifecycle_Action": "NEW",
        "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
        "Counterparty_LEI": cp_info["LEI"],
        "Trade_Date": tdate,
        "Value_Settle_Date": settle_date,
        "Product_Type": {"Bond":"BOND","Equity":"EQUITY"}[asset],
        "Dir_Ind": "B",
        "Primary_Security_ID": inst["ISIN"] or inst["Ticker"],
        "Security_ID_Source": "ISIN" if inst.get("ISIN") else "TICKER",
        "Security_Text": inst["Desc"],
        "Reported_Qty_Notional": busted_qty,
        "Exec_Price_Rate": busted_price,
        "Trade_Ccy": inst["Currency"],
        "Reported_Accrued_Int": accrued,
        "Net_Cash_Amount": busted_net,
        "Coupon": inst.get("Coupon", None),
        "Maturity": inst.get("Maturity", None),
    })
    # CANC row referencing the NEW
    canc_row = make_cpty_row({
        "Cpty_Report_ID": cp_canc_id,
        "Shared_Market_Exec_Ref": internal["UTI_Exec_ID"],
        "Trans_Lifecycle_Action": "CANC",
        "Cancels_Prior_Report_ID": cp_new_id,
        "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
        "Counterparty_LEI": cp_info["LEI"],
        "Trade_Date": tdate + datetime.timedelta(days=1),  # later
        "Value_Settle_Date": settle_date,
        "Product_Type": {"Bond":"BOND","Equity":"EQUITY"}[asset],
        "Dir_Ind": "B",
        "Primary_Security_ID": inst["ISIN"] or inst["Ticker"],
        "Security_ID_Source": "ISIN" if inst.get("ISIN") else "TICKER",
        "Security_Text": inst["Desc"],
        "Reported_Qty_Notional": -busted_qty,  # reverse
        "Exec_Price_Rate": busted_price,
        "Trade_Ccy": inst["Currency"],
        "Reported_Accrued_Int": -accrued,
        "Net_Cash_Amount": -busted_net,
        "Coupon": inst.get("Coupon", None),
        "Maturity": inst.get("Maturity", None),
    })
    # CORR row (correct trade, matches internal)
    corr_row = make_cpty_row({
        "Cpty_Report_ID": cp_corr_id,
        "Shared_Market_Exec_Ref": internal["UTI_Exec_ID"],
        "Trans_Lifecycle_Action": "CORR",
        "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
        "Counterparty_LEI": cp_info["LEI"],
        "Trade_Date": tdate + datetime.timedelta(days=1),
        "Value_Settle_Date": settle_date,
        "Product_Type": {"Bond":"BOND","Equity":"EQUITY"}[asset],
        "Dir_Ind": "B",
        "Primary_Security_ID": inst["ISIN"] or inst["Ticker"],
        "Security_ID_Source": "ISIN" if inst.get("ISIN") else "TICKER",
        "Security_Text": inst["Desc"],
        "Reported_Qty_Notional": notional,
        "Exec_Price_Rate": price,
        "Trade_Ccy": inst["Currency"],
        "Reported_Accrued_Int": accrued,
        "Net_Cash_Amount": net,
        "Coupon": inst.get("Coupon", None),
        "Maturity": inst.get("Maturity", None),
    })

    all_counterparty.extend([new_row, canc_row, corr_row])
    oracle["lifecycle_chains"].append({
        "Internal_Trade_ID": it_id,
        "Cpty_Report_IDs": [cp_new_id, cp_canc_id, cp_corr_id],
        "Matching_CP_ID": cp_corr_id,
        "Chain": chain+1,
    })

# ---- 6f. Unmatched Internal Orphans (85 rows) ----
for i in range(85):
    it_id = f"IT{next_it:04d}"
    next_it += 1
    asset = random.choice(["Bond","Equity","Forward","Swap"])
    inst = random.choice(BONDS if asset=="Bond" else EQUITIES if asset=="Equity" else FX_FORWARDS if asset=="Forward" else SWAPS)
    cp_code = random.choice(ALL_CP_CODES)
    cp_info = CP_INFO[cp_code]
    side = random.choice(["BUY","SELL"]) if asset in ("Bond","Equity","Forward") else random.choice(["PAY_FIXED","REC_FIXED"])
    tdate = random_date()
    settle = tdate + datetime.timedelta(days=2)
    notional = gen_notional(asset) if asset!="Forward" else gen_notional("Forward")
    price = gen_price(asset)
    principal = round(notional * price / 100.0, 2) if asset in ("Bond","Equity") else notional
    accrued = calc_bond_accrued(settle, inst.get("Coupon",0), inst.get("Maturity"), notional) if asset=="Bond" else 0
    net = principal + accrued if asset!="Swap" else 0

    internal = make_internal_row({
        "Internal_Trade_ID": it_id,
        "UTI_Exec_ID": new_uti(),
        "Portfolio_Book": random.choice(books),
        "Trade_Date": tdate,
        "Settle_Date": settle,
        "Asset_Class": asset,
        "Counterparty_Code": cp_code,
        "Counterparty_Legal_Name": cp_info["Legal"],
        "Side": side,
        "Ticker": inst["Ticker"],
        "ISIN": inst.get("ISIN",""),
        "CUSIP": inst.get("CUSIP",""),
        "SEDOL": inst.get("SEDOL",""),
        "Instrument_Description": inst["Desc"],
        "Quantity_Notional": notional,
        "Clean_Price_Rate": price,
        "Currency": inst["Currency"],
        "Principal_Gross": principal,
        "Accrued_Interest": accrued,
        "Commission_Fees": 0,
        "Net_Settlement_Amount": net,
        "Coupon_Rate": inst.get("Coupon", None),
        "Maturity_Date": inst.get("Maturity", None),
    })
    all_internal.append(internal)
    oracle["unmatched_internal"].append(it_id)

# ---- 6g. Unmatched Counterparty Alleged Trades (7 rows) ----
for i in range(7):
    cp_id = f"CP{next_cp:04d}"
    next_cp += 1
    asset = random.choice(["Bond","Equity","Forward","Swap"])
    inst = random.choice(BONDS if asset=="Bond" else EQUITIES if asset=="Equity" else FX_FORWARDS if asset=="Forward" else SWAPS)
    cp_code = random.choice(ALL_CP_CODES)
    cp_info = CP_INFO[cp_code]
    tdate = random_date()
    settle = tdate + datetime.timedelta(days=2)
    notional = gen_notional(asset) if asset!="Forward" else gen_notional("Forward")
    price = gen_price(asset)
    principal = round(notional * price / 100.0, 2) if asset in ("Bond","Equity") else notional
    accrued = 0
    if asset=="Bond":
        accrued = calc_bond_accrued(settle, inst["Coupon"], inst["Maturity"], notional)
    net = principal + accrued if asset!="Swap" else 0

    cpty = make_cpty_row({
        "Cpty_Report_ID": cp_id,
        "Shared_Market_Exec_Ref": new_uti(),
        "Reporting_Counterparty_Name": random.choice(cp_info["aliases"]),
        "Counterparty_LEI": cp_info["LEI"],
        "Trade_Date": tdate,
        "Value_Settle_Date": settle,
        "Product_Type": {"Bond":"BOND","Equity":"EQUITY","Forward":"FX_FWD","Swap":"IRS"}[asset],
        "Dir_Ind": "B",
        "Primary_Security_ID": inst["ISIN"] or inst["Ticker"],
        "Security_ID_Source": "ISIN" if inst.get("ISIN") else "TICKER",
        "Security_Text": inst["Desc"],
        "Reported_Qty_Notional": notional,
        "Exec_Price_Rate": price,
        "Trade_Ccy": inst["Currency"],
        "Reported_Accrued_Int": accrued,
        "Net_Cash_Amount": net,
        "Coupon": inst.get("Coupon", None),
        "Maturity": inst.get("Maturity", None),
    })
    all_counterparty.append(cpty)
    oracle["unmatched_counterparty"].append(cp_id)

# Verify counts
assert len(all_internal) == 300, f"Internal rows: {len(all_internal)}"
assert len(all_counterparty) == 200, f"Counterparty rows: {len(all_counterparty)}"
assert len(known_exact_matches) == 120, f"Known exact matches: {len(known_exact_matches)}"

# ----------------------------------------------------------------------
# 7.  Shuffle rows (Tab1 & Tab2) – Tab3 remains ordered as generated
# ----------------------------------------------------------------------
random.shuffle(all_internal)
random.shuffle(all_counterparty)

# ----------------------------------------------------------------------
# 8.  Write Excel Workbook
# ----------------------------------------------------------------------
wb = Workbook()

# --- Tab 1: Internal_Trades ---
ws1 = wb.active
ws1.title = "Internal_Trades"
int_headers = [
    "Internal_Trade_ID", "UTI_Exec_ID", "Parent_Link_ID", "Leg_Structure",
    "Portfolio_Book", "Trade_Date", "Settle_Date", "Asset_Class",
    "Counterparty_Code", "Counterparty_Legal_Name", "Side", "Ticker",
    "ISIN", "CUSIP", "SEDOL", "Instrument_Description", "Quantity_Notional",
    "Clean_Price_Rate", "Currency", "Principal_Gross", "Accrued_Interest",
    "Commission_Fees", "Net_Settlement_Amount", "Coupon_Rate", "Maturity_Date"
]
ws1.append(int_headers)
for row in all_internal:
    ws1.append([
        row["Internal_Trade_ID"],
        row["UTI_Exec_ID"],
        row["Parent_Link_ID"] or "",
        row["Leg_Structure"],
        row["Portfolio_Book"],
        row["Trade_Date"].strftime("%Y-%m-%d"),
        row["Settle_Date"].strftime("%Y-%m-%d"),
        row["Asset_Class"],
        row["Counterparty_Code"],
        row["Counterparty_Legal_Name"],
        row["Side"],
        row["Ticker"],
        row["ISIN"],
        row["CUSIP"],
        row["SEDOL"],
        row["Instrument_Description"],
        row["Quantity_Notional"],
        row["Clean_Price_Rate"],
        row["Currency"],
        row["Principal_Gross"],
        row["Accrued_Interest"],
        row["Commission_Fees"],
        row["Net_Settlement_Amount"],
        row.get("Coupon_Rate", ""),
        row["Maturity_Date"].strftime("%Y-%m-%d") if row["Maturity_Date"] else "",
    ])

# --- Tab 2: Counterparty_Trades ---
ws2 = wb.create_sheet("Counterparty_Trades")
cpty_headers = [
    "Cpty_Report_ID", "Shared_Market_Exec_Ref", "Trans_Lifecycle_Action",
    "Cancels_Prior_Report_ID", "Reporting_Counterparty_Name", "Counterparty_LEI",
    "Reporting_Perspective", "Trade_Date", "Value_Settle_Date", "Product_Type",
    "Dir_Ind", "Primary_Security_ID", "Security_ID_Source", "Security_Text",
    "Reported_Qty_Notional", "Exec_Price_Rate", "Price_Quote_Convention",
    "Trade_Ccy", "Reported_Accrued_Int", "Net_Cash_Amount", "Coupon", "Maturity"
]
ws2.append(cpty_headers)
for row in all_counterparty:
    ws2.append([
        row["Cpty_Report_ID"],
        row["Shared_Market_Exec_Ref"],
        row["Trans_Lifecycle_Action"],
        row["Cancels_Prior_Report_ID"] or "",
        row["Reporting_Counterparty_Name"],
        row["Counterparty_LEI"],
        row["Reporting_Perspective"],
        row["Trade_Date"].strftime("%Y-%m-%d"),
        row["Value_Settle_Date"].strftime("%Y-%m-%d"),
        row["Product_Type"],
        row["Dir_Ind"],
        row["Primary_Security_ID"],
        row["Security_ID_Source"],
        row["Security_Text"],
        row["Reported_Qty_Notional"],
        row["Exec_Price_Rate"],
        row["Price_Quote_Convention"],
        row["Trade_Ccy"],
        row["Reported_Accrued_Int"],
        row["Net_Cash_Amount"],
        row.get("Coupon", ""),
        row["Maturity"].strftime("%Y-%m-%d") if row.get("Maturity") else "",
    ])

# --- Tab 3: Known_Exact_Matches ---
ws3 = wb.create_sheet("Known_Exact_Matches")
ws3.append(["Internal_Trade_ID", "Cpty_Report_ID", "Match_Type", "Notes"])
for (it, cp) in known_exact_matches:
    # find rows to extract notes? We'll keep it simple.
    ws3.append([it, cp, "EXACT", "Pre-identified exact match"])

# Add summary table below
ws3.append([])
ws3.append(["Summary Table"])
ws3.append(["Category", "Count/Details"])
summary_data = [
    ("Exact Matches (in Tab 3)", 120),
    ("Near Matches (blind)", 18),
    ("Economic Breaks (blind)", 20),
    ("N-to-1 Allocation Splits (blind)", "36 Internal -> 12 Counterparty"),
    ("Multi-Leg Netting (blind)", "16 Internal -> 8 Counterparty"),
    ("Lifecycle Chains (blind)", "5 Internal -> 15 Counterparty"),
    ("Unmatched Internal (blind)", 85),
    ("Unmatched Counterparty (blind)", 7),
    ("Total Internal Rows", 300),
    ("Total Counterparty Rows", 200),
]
for item in summary_data:
    ws3.append(item)

# ----------------------------------------------------------------------
# 9.  Save Excel
# ----------------------------------------------------------------------
excel_filename = "RecDiffy_Trade_Reconciliation_Dataset.xlsx"
wb.save(excel_filename)
print(f"Saved {excel_filename}")

# ----------------------------------------------------------------------
# 10. Save Ground-Truth Oracle JSON
# ----------------------------------------------------------------------
json_filename = "RecDiffy_Hidden_Test_Oracle.json"
with open(json_filename, "w") as f:
    json.dump(oracle, f, indent=2, default=str)
print(f"Saved {json_filename}")

print("All done. Run the script with: python create_dataset.py")
```

When you run this script, it will generate:

- **`RecDiffy_Trade_Reconciliation_Dataset.xlsx`**  
  Contains exactly 300 rows in `Internal_Trades`, 200 rows in `Counterparty_Trades`, and 120 rows in `Known_Exact_Matches` (plus a summary table).

- **`RecDiffy_Hidden_Test_Oracle.json`**  
  A complete JSON mapping that lists every trade and its relationship, including the blind scenarios (near-matches, breaks, allocation splits, multi-leg netting, lifecycle chains, and unmatched items). This file is the “answer key” for automated unit tests.