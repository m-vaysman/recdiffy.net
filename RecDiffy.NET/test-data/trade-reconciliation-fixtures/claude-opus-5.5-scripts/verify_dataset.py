#!/usr/bin/env python3
"""Independent check of the RecDiffy fixtures. It imports nothing from the generator.

Structural checks read the workbook and oracle directly. The discovery check then reconciles
Tabs 1 and 2 using only the workbook and the oracle's normalization reference, and compares
its answer with the oracle's. If that passes, the oracle is reachable from the data alone.

Usage: python verify_dataset.py <dataset.xlsx> <oracle.json>
"""
from __future__ import annotations

import fnmatch
import json
import re
import sys
from collections import Counter, defaultdict
from datetime import date, datetime
from decimal import ROUND_HALF_UP, Decimal as D

import openpyxl

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

failures = []
passes = 0


def check(name: str, ok, detail="") -> None:
    global passes
    if ok:
        passes += 1
    else:
        failures.append(f"{name}: {detail}")
        print(f"  FAIL {name}: {detail}")


def clean(v):
    if isinstance(v, datetime):
        return v.date()
    if isinstance(v, (int, float)) and not isinstance(v, bool):
        return D(str(v))
    return v


def read_table(ws, headers):
    got = [c.value for c in ws[1]][:len(headers)]
    check(f"{ws.title} headers", got == headers, f"{got}")
    return [dict(zip(headers, (clean(c) for c in row[:len(headers)])))
            for row in ws.iter_rows(min_row=2, values_only=True) if row[0] is not None]


def cents(x):
    return x.quantize(D("0.01"), rounding=ROUND_HALF_UP)


def cusip_ok(s):
    total = 0
    for i, c in enumerate(s[:8]):
        v = int(c, 36) * (2 if i % 2 else 1)
        total += v // 10 + v % 10
    return len(s) == 9 and str((10 - total % 10) % 10) == s[8]


def isin_ok(s):
    digits = "".join(str(int(c, 36)) for c in s)
    total = 0
    for i, ch in enumerate(reversed(digits)):
        v = int(ch) * (2 if i % 2 else 1)
        total += v // 10 + v % 10
    return len(s) == 12 and total % 10 == 0


def sedol_ok(s):
    total = sum(int(c, 36) * w for c, w in zip(s, (1, 3, 1, 7, 3, 9, 1)))
    return len(s) == 7 and total % 10 == 0


def lei_ok(s):
    return len(s) == 20 and int("".join(str(int(c, 36)) for c in s)) % 97 == 1


def parse_32nds(text):
    m = re.fullmatch(r"(\d+)-(\d{2})(\+?)", text)
    assert m, f"bad 32nds price {text}"
    return D(m[1]) + (D(m[2]) + (D("0.5") if m[3] else 0)) / 32


FLIP = {"BUY": "SELL", "SELL": "BUY", "PAY_FIXED": "REC_FIXED", "REC_FIXED": "PAY_FIXED"}


def main(xlsx_path, json_path):
    oracle = json.load(open(json_path, encoding="utf-8"))
    wb = openpyxl.load_workbook(xlsx_path)
    cached = openpyxl.load_workbook(xlsx_path, data_only=True)

    print("A. Structure")
    check("sheet names and order", wb.sheetnames == ["Internal_Trades", "Counterparty_Trades", "Known_Exact_Matches"],
          f"{wb.sheetnames}")
    check("no hidden sheets", all(ws.sheet_state == "visible" for ws in wb.worksheets))
    check("no defined names", len(wb.defined_names) == 0)
    internal = read_table(wb["Internal_Trades"], TAB1)
    street = read_table(wb["Counterparty_Trades"], TAB2)
    ws3 = wb["Known_Exact_Matches"]
    pairs = [(r[1], r[2]) for r in ws3.iter_rows(min_row=2, max_col=3, values_only=True) if r[1]]
    check("exactly 300 internal rows", len(internal) == 300, len(internal))
    check("exactly 200 counterparty rows", len(street) == 200, len(street))
    check("exactly 120 known exact pairs", len(pairs) == 120, len(pairs))
    check("sheet dimensions match row counts",
          (wb["Internal_Trades"].max_row, wb["Counterparty_Trades"].max_row, ws3.max_row) == (301, 201, 121))
    check("no cell comments", not any(c.comment for ws in wb.worksheets for row in ws.iter_rows() for c in row))
    fonts = {c.font.name for ws in wb.worksheets for row in ws.iter_rows() for c in row if c.value is not None}
    check("Arial throughout", fonts == {"Arial"}, fonts)

    by_i = {r["Internal_Trade_ID"]: r for r in internal}
    by_s = {r["Cpty_Report_ID"]: r for r in street}
    check("unique internal IDs", len(by_i) == 300)
    check("unique counterparty IDs", len(by_s) == 200)

    print("B. Field values")
    check("internal ID format", all(re.fullmatch(r"IT-\d{6}", i) for i in by_i))
    check("counterparty ID format", all(re.fullmatch(r"CR-\d{8}", i) for i in by_s))
    ref_re = r"UTI[0-9A-F]{20}"
    check("all internal refs share one format", all(re.fullmatch(ref_re, r["UTI_Exec_ID"]) for r in internal))
    check("all street refs share one format", all(re.fullmatch(ref_re, r["Shared_Market_Exec_Ref"]) for r in street))
    check("Asset_Class values", {r["Asset_Class"] for r in internal} == {"Bond", "Equity", "Forward", "Swap"})
    check("Side values", {r["Side"] for r in internal} == {"BUY", "SELL", "PAY_FIXED", "REC_FIXED"})
    check("all eight counterparty codes used", {r["Counterparty_Code"] for r in internal} ==
          {"GSCO", "JPM", "MSCO", "BAML", "CITI", "BARC", "UBS", "CITADEL"})
    check("internal quantities positive", all(r["Quantity_Notional"] > 0 for r in internal))
    check("lifecycle values", Counter(r["Trans_Lifecycle_Action"] for r in street) ==
          Counter({"NEW": 190, "CANC": 5, "CORR": 5}), Counter(r["Trans_Lifecycle_Action"] for r in street))
    check("perspective values", {r["Reporting_Perspective"] for r in street} == {"PB_CLIENT_VIEW", "DEALER_BILATERAL_VIEW"})
    check("ID source values", {r["Security_ID_Source"] for r in street} == {"ISIN", "CUSIP", "SEDOL", "TICKER"})
    check("ISINs valid", all(isin_ok(r["ISIN"]) for r in internal))
    check("CUSIPs valid or blank on OTC only", all(
        cusip_ok(r["CUSIP"]) if r["Asset_Class"] in ("Bond", "Equity") else r["CUSIP"] is None for r in internal))
    check("SEDOLs valid or blank on OTC only", all(
        sedol_ok(r["SEDOL"]) if r["Asset_Class"] in ("Bond", "Equity") else r["SEDOL"] is None for r in internal))
    check("LEIs valid", all(lei_ok(r["Counterparty_LEI"]) for r in street))
    check("dates are real weekdays", all(
        r[k].weekday() < 5 for r in internal for k in ("Trade_Date", "Settle_Date")) and all(
        r[k].weekday() < 5 for r in street for k in ("Trade_Date", "Value_Settle_Date")))
    check("settlement after trade date", all(r["Settle_Date"] > r["Trade_Date"] for r in internal) and all(
        r["Value_Settle_Date"] > r["Trade_Date"] for r in street))

    # Row-level economic identities on the internal book.
    bad_principal, bad_net, bad_accrued = [], [], []
    for r in internal:
        q, p = r["Quantity_Notional"], r["Clean_Price_Rate"]
        principal = {"Bond": q * p / 100, "Equity": q * p, "Forward": q * p, "Swap": q * p / 200}[r["Asset_Class"]]
        if cents(principal) != r["Principal_Gross"]:
            bad_principal.append(r["Internal_Trade_ID"])
        sign = 1 if r["Side"] in ("BUY", "PAY_FIXED") else -1
        if r["Principal_Gross"] + r["Accrued_Interest"] + sign * r["Commission_Fees"] != r["Net_Settlement_Amount"]:
            bad_net.append(r["Internal_Trade_ID"])
        if r["Asset_Class"] == "Bond":
            m = r["Maturity_Date"]
            next_c = m
            while True:
                month = next_c.month - 7
                prev_c = date(next_c.year + month // 12, month % 12 + 1, m.day)
                if prev_c <= r["Settle_Date"] < next_c:
                    break
                next_c = prev_c
            s = r["Settle_Date"]
            coupon = r["Coupon_Rate"]
            if r["Instrument_Description"].startswith("US Treasury"):  # not the ticker: AT&T bonds also trade as "T"
                amount = q * coupon / 200 * (s - prev_c).days / (next_c - prev_c).days
            else:
                days = (s.year - prev_c.year) * 360 + (s.month - prev_c.month) * 30 + min(s.day, 30) - min(prev_c.day, 30)
                amount = q * coupon / 100 * days / 360
            if abs(cents(amount) - r["Accrued_Interest"]) > D("0.02"):  # allocation plugs may move a cent or two
                bad_accrued.append(r["Internal_Trade_ID"])
        elif r["Accrued_Interest"] != 0:
            bad_accrued.append(r["Internal_Trade_ID"])
    check("principal identity on all 300 rows", not bad_principal, bad_principal[:5])
    check("net settlement identity on all 300 rows", not bad_net, bad_net[:5])
    check("accrued interest formula on all bonds", not bad_accrued, bad_accrued[:5])

    print("C. Oracle partitions the data")
    sc = oracle["blind_scenarios"]
    i_ids, s_ids = [], []
    for k in oracle["known_exact_matches"]:
        i_ids.append(k["internal_trade_id"])
        s_ids.append(k["cpty_report_id"])
    for name in ("within_tolerance_near_matches", "true_economic_breaks"):
        i_ids += [x["internal_trade_id"] for x in sc[name]]
        s_ids += [x["cpty_report_id"] for x in sc[name]]
    for name in ("n_to_1_allocations", "multi_leg_representations"):
        for x in sc[name]:
            i_ids += x["internal_trade_ids"]
            s_ids.append(x["cpty_report_id"])
    for x in sc["cancel_correct_chains"]:
        i_ids.append(x["internal_trade_id"])
        s_ids += [x["new_report_id"], x["canc_report_id"], x["corr_report_id"]]
    i_ids += [x["internal_trade_id"] for x in sc["unmatched_internal_orphans"]]
    s_ids += [x["cpty_report_id"] for x in sc["unmatched_counterparty_alleged"]]
    check("oracle covers each internal row exactly once", sorted(i_ids) == sorted(by_i), f"{len(i_ids)} ids")
    check("oracle covers each counterparty row exactly once", sorted(s_ids) == sorted(by_s), f"{len(s_ids)} ids")
    expected = {"within_tolerance_near_matches": (18, 18), "true_economic_breaks": (20, 20),
                "n_to_1_allocations": (36, 12), "multi_leg_representations": (16, 8),
                "cancel_correct_chains": (5, 15), "unmatched_internal_orphans": (85, 0),
                "unmatched_counterparty_alleged": (0, 7)}
    for name, (ni, nc) in expected.items():
        e = oracle["expected_counts"][name]
        check(f"counts for {name}", (e["internal"], e["counterparty"]) == (ni, nc), e)
    check("scenario sizes", (len(sc["within_tolerance_near_matches"]), len(sc["true_economic_breaks"]),
                             len(sc["n_to_1_allocations"]), len(sc["multi_leg_representations"]),
                             len(sc["cancel_correct_chains"]), len(sc["unmatched_internal_orphans"]),
                             len(sc["unmatched_counterparty_alleged"])) == (18, 20, 12, 8, 5, 85, 7))
    check("Tab 3 pairs equal oracle pairs", sorted(pairs) == sorted(
        (k["internal_trade_id"], k["cpty_report_id"]) for k in oracle["known_exact_matches"]))

    print("D. Normalization")
    ref = oracle["normalization_reference"]
    aliases, dir_codes, products = ref["counterparty_aliases"], ref["direction_codes"], ref["product_types"]
    id_index = {}
    conflicts = []
    for r in internal:
        for source, col in (("ISIN", "ISIN"), ("CUSIP", "CUSIP"), ("SEDOL", "SEDOL"), ("TICKER", "Ticker")):
            if r[col]:
                if id_index.setdefault((source, r[col]), r["ISIN"]) != r["ISIN"]:
                    conflicts.append(r[col])
    check("every security identifier resolves to one instrument", not conflicts, conflicts[:5])

    def norm(r):
        raw = r["Reported_Qty_Notional"]
        qty = abs(raw)
        conv = r["Price_Quote_Convention"]
        pay_rec = products[r["Product_Type"]] == "Swap" or conv == "FX_SWAP_POINTS"
        if r["Dir_Ind"]:
            side = dir_codes[r["Dir_Ind"]]
            if raw < 0:
                assert side in ("SELL", "PAY_FIXED"), f"sign contradicts code on {r['Cpty_Report_ID']}"
        elif raw < 0:
            side = "PAY_FIXED" if pay_rec else "SELL"
        else:
            side = "REC_FIXED" if pay_rec else "BUY"
        if r["Reporting_Perspective"] == "DEALER_BILATERAL_VIEW":
            side = FLIP[side]
        px = r["Exec_Price_Rate"]
        if conv == "32NDS_UST":
            px = parse_32nds(px)
        elif conv == "DIRTY_PCT":
            px = (px - r["Reported_Accrued_Int"] / qty * 100).quantize(D("0.000001"), rounding=ROUND_HALF_UP)
        return dict(id=r["Cpty_Report_ID"], code=aliases[r["Reporting_Counterparty_Name"]], td=r["Trade_Date"],
                    sec=id_index.get((r["Security_ID_Source"], r["Primary_Security_ID"])), side=side, qty=qty, px=px,
                    settle=r["Value_Settle_Date"], accrued=r["Reported_Accrued_Int"], net=r["Net_Cash_Amount"],
                    ccy=r["Trade_Ccy"], conv=conv, coupon=r["Coupon"], maturity=r["Maturity"],
                    ref=r["Shared_Market_Exec_Ref"], action=r["Trans_Lifecycle_Action"], cancels=r["Cancels_Prior_Report_ID"])

    N = {r["Cpty_Report_ID"]: norm(r) for r in street}

    mismatched, wrong_basis = [], []
    tag_counts = Counter()
    street_refs = {r["Shared_Market_Exec_Ref"] for r in street}
    internal_refs = {r["UTI_Exec_ID"] for r in internal}
    for k in oracle["known_exact_matches"]:
        i, s = by_i[k["internal_trade_id"]], N[k["cpty_report_id"]]
        same = (i["Counterparty_Code"], i["Trade_Date"], i["ISIN"], i["Side"], i["Quantity_Notional"], i["Clean_Price_Rate"],
                i["Settle_Date"], i["Accrued_Interest"], i["Net_Settlement_Amount"], i["Currency"], i["Coupon_Rate"],
                i["Maturity_Date"]) == (s["code"], s["td"], s["sec"], s["side"], s["qty"], s["px"], s["settle"],
                                        s["accrued"], s["net"], s["ccy"], s["coupon"], s["maturity"])
        if not same:
            mismatched.append(k["pair_no"])
        shared = i["UTI_Exec_ID"] == s["ref"]
        if shared != (k["match_basis"] == "SHARED_EXEC_REF"):
            wrong_basis.append(k["pair_no"])
        if not shared and (s["ref"] in internal_refs or i["UTI_Exec_ID"] in street_refs):
            wrong_basis.append(k["pair_no"])
        tag_counts.update(k["normalization_tests"])
        raw = by_s[k["cpty_report_id"]]
        tag_counts[f"view:{raw['Reporting_Perspective']}"] += 1
        if raw["Reporting_Counterparty_Name"] == i["Counterparty_Code"]:
            mismatched.append(f"{k['pair_no']} alias equals code")
    check("all 120 pairs are identical after normalization", not mismatched, mismatched[:10])
    check("exec ref sharing matches Match_Basis", not wrong_basis, wrong_basis[:10])
    basis = Counter(k["match_basis"] for k in oracle["known_exact_matches"])
    check("60 pairs share an exec ref, 60 do not", basis == Counter(SHARED_EXEC_REF=60, NATURAL_KEY=60), basis)
    check("12 32nds pairs", tag_counts["PRICE_32NDS"] == 12, tag_counts["PRICE_32NDS"])
    check("8 dirty price pairs", tag_counts["PRICE_DIRTY"] == 8, tag_counts["PRICE_DIRTY"])
    check("15 dealer inversion pairs", tag_counts["DEALER_INVERSION"] == 15, tag_counts["DEALER_INVERSION"])
    check("dealer pairs are the only known pairs in dealer view",
          tag_counts["view:DEALER_BILATERAL_VIEW"] == 15, tag_counts["view:DEALER_BILATERAL_VIEW"])
    otc_dealer = [k for k in oracle["known_exact_matches"] if "DEALER_INVERSION" in k["normalization_tests"]]
    check("dealer pairs are all OTC", all(k["asset_class"] in ("Forward", "Swap") for k in otc_dealer))
    prices = {by_s[k["cpty_report_id"]]["Exec_Price_Rate"] for k in oracle["known_exact_matches"]
              if "PRICE_32NDS" in k["normalization_tests"]}
    check("spec's 32nds examples present", {"98-24", "98-11+"} <= prices, prices)
    check("32nds prices stored as text", all(isinstance(p, str) for p in prices))
    styles = {k: v for k, v in tag_counts.items() if k.startswith("DIR_")}
    check("all five direction styles tested", len(styles) == 5, styles)
    sources = {k: v for k, v in tag_counts.items() if k.startswith("ID_")}
    check("all four identifier sources tested", len(sources) == 4, sources)

    print("E. Blind discovery from the data alone")
    # 1. Collapse lifecycle chains: a CANC removes itself and the row it cancels.
    canc = [s for s in N.values() if s["action"] == "CANC"]
    removed = {s["cancels"] for s in canc} | {s["id"] for s in canc}
    check("each CANC cancels a NEW row", all(N[s["cancels"]]["action"] == "NEW" for s in canc))
    chain_of_cancelled = {s["cancels"]: s["id"] for s in canc}
    open_s = {k: v for k, v in N.items() if k not in removed}
    open_i = {r["Internal_Trade_ID"]: r for r in internal}
    found = {"exact": [], "chain": [], "alloc": [], "multi": [], "near": [], "break": []}
    ambiguous = []

    # 2. Exact match on full normalized economics.
    for sid, s in list(open_s.items()):
        if s["conv"] in ("FX_SWAP_POINTS", "NET_FIXED_SPREAD_PCT"):
            continue
        cands = [r for r in open_i.values() if r["Leg_Structure"] == "SINGLE" and
                 (r["Counterparty_Code"], r["Trade_Date"], r["ISIN"], r["Side"], r["Quantity_Notional"], r["Clean_Price_Rate"],
                  r["Settle_Date"], r["Accrued_Interest"], r["Net_Settlement_Amount"], r["Currency"]) ==
                 (s["code"], s["td"], s["sec"], s["side"], s["qty"], s["px"], s["settle"], s["accrued"], s["net"], s["ccy"])]
        if len(cands) > 1:
            ambiguous.append(sid)
        if len(cands) == 1:
            found["chain" if s["action"] == "CORR" else "exact"].append((cands[0]["Internal_Trade_ID"], sid))
            del open_i[cands[0]["Internal_Trade_ID"]], open_s[sid]
    check("exact tier is never ambiguous", not ambiguous, ambiguous[:5])

    # 3. Multi-leg packages: linked rows against one combined net row.
    by_parent = defaultdict(list)
    for r in open_i.values():
        if r["Parent_Link_ID"]:
            by_parent[r["Parent_Link_ID"]].append(r)
    check("8 linked packages of 2 rows", len(by_parent) == 8 and all(len(v) == 2 for v in by_parent.values()), len(by_parent))
    for parent, legs in by_parent.items():
        leg1 = next(l for l in legs if l["Leg_Structure"] == "LEG_1_PAY")
        leg2 = next(l for l in legs if l["Leg_Structure"] == "LEG_2_RECEIVE")
        for sid, s in list(open_s.items()):
            if s["conv"] not in ("FX_SWAP_POINTS", "NET_FIXED_SPREAD_PCT"):
                continue
            if (s["code"], s["td"], s["sec"]) != (leg1["Counterparty_Code"], leg1["Trade_Date"], leg1["ISIN"]):
                continue
            direction = "PAY_FIXED" if leg1["Net_Settlement_Amount"] > leg2["Net_Settlement_Amount"] else "REC_FIXED"
            rate = leg2["Clean_Price_Rate"] - leg1["Clean_Price_Rate"]
            rate = rate if s["conv"] == "FX_SWAP_POINTS" else abs(rate)
            ok = (s["qty"] == leg1["Quantity_Notional"] == leg2["Quantity_Notional"] and s["px"] == rate and
                  s["net"] == abs(leg1["Net_Settlement_Amount"] - leg2["Net_Settlement_Amount"]) and s["side"] == direction)
            check(f"package {parent} nets to its street row", ok, sid)
            found["multi"].append((sorted([leg1["Internal_Trade_ID"], leg2["Internal_Trade_ID"]]), sid))
            del open_i[leg1["Internal_Trade_ID"]], open_i[leg2["Internal_Trade_ID"]], open_s[sid]
            break

    # 4. N-to-1: internal rows with one key and price that sum to one street row.
    groups = defaultdict(list)
    for r in open_i.values():
        groups[(r["Counterparty_Code"], r["Trade_Date"], r["ISIN"], r["Side"], r["Clean_Price_Rate"])].append(r)
    for key, rows in groups.items():
        if len(rows) < 2:
            continue
        for sid, s in list(open_s.items()):
            if (s["code"], s["td"], s["sec"], s["side"], s["px"]) == key and s["qty"] == sum(r["Quantity_Notional"] for r in rows) \
                    and s["net"] == sum(r["Net_Settlement_Amount"] for r in rows) \
                    and s["accrued"] == sum(r["Accrued_Interest"] for r in rows):
                check("allocation books", {r["Portfolio_Book"] for r in rows} == {"MACRO_RATES", "STAT_ARB_EQ", "CREDIT_OPP"}, sid)
                found["alloc"].append((sorted(r["Internal_Trade_ID"] for r in rows), sid))
                for r in rows:
                    del open_i[r["Internal_Trade_ID"]]
                del open_s[sid]
                break

    # 5. Reduced key: one candidate on counterparty, date, security and side. Tolerance decides near or break.
    tol = oracle["tolerances"]
    for sid, s in list(open_s.items()):
        cands = [r for r in open_i.values() if (r["Counterparty_Code"], r["Trade_Date"], r["ISIN"], r["Side"]) ==
                 (s["code"], s["td"], s["sec"], s["side"])]
        if len(cands) > 1:
            ambiguous.append(sid)
        if len(cands) != 1:
            continue
        i = cands[0]
        dq, dp, ds = s["qty"] - i["Quantity_Notional"], s["px"] - i["Clean_Price_Rate"], s["settle"] != i["Settle_Date"]
        da, dn = s["accrued"] - i["Accrued_Interest"], s["net"] - i["Net_Settlement_Amount"]
        label = None
        if dq == 0 and not ds:
            if dp == 0 and da != 0 and abs(da) <= D(str(tol["accrued_interest_abs"])) and dn == da:
                label = ("near", "ACCRUED_INTEREST")
            elif i["Asset_Class"] == "Forward" and dp != 0 and abs(dp) <= D(str(tol["fx_rate_abs"])) and da == 0 \
                    and s["net"] == cents(s["qty"] * s["px"]):
                label = ("near", "FX_RATE_4TH_DECIMAL")
            elif dp == 0 and da == 0 and dn != 0 and abs(dn) <= D(str(tol["fee_rounding_net_cash_abs"])):
                label = ("near", "FEE_ROUNDING")
        if label is None:
            if dq != 0:
                label = ("break", "PARTIAL_FILL_80_PCT" if s["qty"] == i["Quantity_Notional"] * 4 / 5 else "UNCLASSIFIED_QTY")
            elif ds:
                label = ("break", "SETTLE_DATE_LAG_T1_T2")
            elif dp != 0:
                label = ("break", "PRICE_BUST")
            elif abs(dn) == 450:
                label = ("break", "COMMISSION_450")
            else:
                label = ("break", "UNCLASSIFIED")
        found[label[0]].append((i["Internal_Trade_ID"], sid, label[1]))
        del open_i[i["Internal_Trade_ID"]], open_s[sid]
    check("reduced-key tier is never ambiguous", not ambiguous, ambiguous[:5])

    # 6. Compare the discovered answer with the oracle.
    def compare(name, mine, theirs):
        check(name, sorted(map(str, mine)) == sorted(map(str, theirs)),
              f"found {len(mine)}, oracle {len(theirs)}; only found: "
              f"{sorted(set(map(str, mine)) - set(map(str, theirs)))[:3]}; only oracle: "
              f"{sorted(set(map(str, theirs)) - set(map(str, mine)))[:3]}")

    compare("discovered 120 known exact pairs", found["exact"],
            [(k["internal_trade_id"], k["cpty_report_id"]) for k in oracle["known_exact_matches"]])
    compare("discovered 18 near matches", found["near"],
            [(x["internal_trade_id"], x["cpty_report_id"], x["tolerance_type"]) for x in sc["within_tolerance_near_matches"]])
    compare("discovered 20 breaks with the right type", found["break"],
            [(x["internal_trade_id"], x["cpty_report_id"], x["break_type"]) for x in sc["true_economic_breaks"]])
    compare("discovered 12 allocation groups", found["alloc"],
            [(sorted(x["internal_trade_ids"]), x["cpty_report_id"]) for x in sc["n_to_1_allocations"]])
    compare("discovered 8 multi-leg groups", found["multi"],
            [(sorted(x["internal_trade_ids"]), x["cpty_report_id"]) for x in sc["multi_leg_representations"]])
    compare("discovered 5 corrected chains", found["chain"],
            [(x["internal_trade_id"], x["corr_report_id"]) for x in sc["cancel_correct_chains"]])
    compare("discovered 85 internal orphans", list(open_i),
            [x["internal_trade_id"] for x in sc["unmatched_internal_orphans"]])
    compare("discovered 7 alleged trades", list(open_s),
            [x["cpty_report_id"] for x in sc["unmatched_counterparty_alleged"]])
    for x in sc["cancel_correct_chains"]:
        check("chain IDs link", chain_of_cancelled.get(x["new_report_id"]) == x["canc_report_id"], x)
    decoys = Counter(x["decoy_type"] for x in sc["unmatched_internal_orphans"] if x["decoy_type"])
    check("15 orphans resemble a known trade", sum(decoys.values()) == 15, decoys)

    # Recompute the oracle's stated field differences from the workbook.
    wrong_diffs = []
    for x in sc["within_tolerance_near_matches"] + sc["true_economic_breaks"]:
        i, s = by_i[x["internal_trade_id"]], N[x["cpty_report_id"]]
        recomputed = {}
        for field, left, right in (("Settle_Date", i["Settle_Date"], s["settle"]), ("Quantity_Notional", i["Quantity_Notional"], s["qty"]),
                                   ("Clean_Price_Rate", i["Clean_Price_Rate"], s["px"]), ("Accrued_Interest", i["Accrued_Interest"], s["accrued"]),
                                   ("Net_Settlement_Amount", i["Net_Settlement_Amount"], s["net"])):
            if left != right:
                recomputed[field] = abs((left - right).days) if isinstance(left, date) else float(abs(left - right))
        stated = {d["internal_field"]: d.get("abs_diff", d.get("abs_diff_days")) for d in x["differences"]}
        if recomputed != stated:
            wrong_diffs.append((x["internal_trade_id"], recomputed, stated))
    check("oracle differences match the workbook", not wrong_diffs, wrong_diffs[:3])

    print("F. Spec magnitudes")
    accrued = [d["abs_diff"] for x in sc["within_tolerance_near_matches"] if x["tolerance_type"] == "ACCRUED_INTEREST"
               for d in x["differences"] if d["internal_field"] == "Accrued_Interest"]
    check("accrued differences within 0.40 to 1.20", len(accrued) == 6 and all(0.40 <= a <= 1.20 for a in accrued), accrued)
    rates = [d["abs_diff"] for x in sc["within_tolerance_near_matches"] if x["tolerance_type"] == "FX_RATE_4TH_DECIMAL"
             for d in x["differences"] if d["internal_field"] == "Clean_Price_Rate"]
    check("FX differences sit in the 4th decimal", len(rates) == 6 and all(0.0001 <= r <= 0.0009 for r in rates), rates)
    fees = [d["abs_diff"] for x in sc["within_tolerance_near_matches"] if x["tolerance_type"] == "FEE_ROUNDING"
            for d in x["differences"]]
    check("fee differences are pennies", len(fees) == 6 and all(0.01 <= f <= 0.05 for f in fees), fees)
    types = Counter(x["break_type"] for x in sc["true_economic_breaks"])
    check("all four break types present", len(types) == 4 and sum(types.values()) == 20, types)
    fills = [by_s[x["cpty_report_id"]] for x in sc["true_economic_breaks"] if x["break_type"] == "PARTIAL_FILL_80_PCT"]
    check("partial fills are exactly 80%", all(
        abs(s["Reported_Qty_Notional"]) * 5 == by_i[x["internal_trade_id"]]["Quantity_Notional"] * 4
        for x in sc["true_economic_breaks"] if x["break_type"] == "PARTIAL_FILL_80_PCT"
        for s in [by_s[x["cpty_report_id"]]]) and len(fills) == 5)
    commissions = [d["abs_diff"] for x in sc["true_economic_breaks"] if x["break_type"] == "COMMISSION_450" for d in x["differences"]]
    check("commission breaks are exactly $450", commissions == [450.0] * 5, commissions)
    lags = [x for x in sc["true_economic_breaks"] if x["break_type"] == "SETTLE_DATE_LAG_T1_T2"]
    check("settlement lags are T+1 against T+2", all(
        sorted(((by_i[x["internal_trade_id"]]["Settle_Date"] - by_i[x["internal_trade_id"]]["Trade_Date"]).days,
                (by_s[x["cpty_report_id"]]["Value_Settle_Date"] - by_s[x["cpty_report_id"]]["Trade_Date"]).days))[1] -
        sorted(((by_i[x["internal_trade_id"]]["Settle_Date"] - by_i[x["internal_trade_id"]]["Trade_Date"]).days,
                (by_s[x["cpty_report_id"]]["Value_Settle_Date"] - by_s[x["cpty_report_id"]]["Trade_Date"]).days))[0] >= 1
        for x in lags) and len(lags) == 5)
    leg_rows = [r for r in internal if r["Parent_Link_ID"]]
    check("only the 16 package rows carry a link", len(leg_rows) == 16 and Counter(r["Leg_Structure"] for r in internal) ==
          Counter(SINGLE=284, LEG_1_PAY=8, LEG_2_RECEIVE=8), Counter(r["Leg_Structure"] for r in internal))

    print("G. Unlabeled rows stay blind")
    blind_i = set(by_i) - {p[0] for p in pairs}
    blind_s = set(by_s) - {p[1] for p in pairs}
    check("180 internal rows unlabeled", len(blind_i) == 180, len(blind_i))
    check("80 counterparty rows unlabeled", len(blind_s) == 80, len(blind_s))
    tab3_text = [str(c.value) for row in ws3.iter_rows() for c in row if c.value is not None]
    listed = [t for t in tab3_text if re.fullmatch(r"(IT-\d{6}|CR-\d{8})", t)]
    check("Tab 3 names only the 120 pairs", len(listed) == 240 and not (set(listed) & (blind_i | blind_s)), len(listed))
    words = re.compile(r"ORPHAN|ALLEGED|DECOY|NEAR.?MATCH|ALLOCATION|PARTIAL|TOLERANCE|BUST|\bDK\b", re.I)
    leaked = [t for ws in (wb["Internal_Trades"], wb["Counterparty_Trades"]) for row in ws.iter_rows(values_only=True)
              for t in row if isinstance(t, str) and words.search(t)]
    check("no scenario words in Tabs 1 and 2", not leaked, leaked[:5])
    alloc_ids = {i for x in sc["n_to_1_allocations"] for i in x["internal_trade_ids"]}
    combos_alloc = {(by_i[i]["Portfolio_Book"], by_i[i]["Asset_Class"]) for i in alloc_ids}
    combos_other = {(r["Portfolio_Book"], r["Asset_Class"]) for r in internal if r["Internal_Trade_ID"] not in alloc_ids}
    check("allocation books also appear on other rows of the same asset class", combos_alloc <= combos_other,
          combos_alloc - combos_other)
    scenario_of = {}
    for k in oracle["known_exact_matches"]:
        scenario_of[k["internal_trade_id"]] = "known"
    for x in sc["unmatched_internal_orphans"]:
        scenario_of[x["internal_trade_id"]] = "orphan"
    run, longest, last = 0, 0, None
    for r in internal:
        s = scenario_of.get(r["Internal_Trade_ID"], "other")
        run = run + 1 if s == last else 1
        longest, last = max(longest, run), s
    check("Tab 1 is shuffled (no long run of one scenario)", longest <= 12, longest)
    ids = [r["Internal_Trade_ID"] for r in internal]
    check("Tab 1 is not in ID order", ids != sorted(ids))

    print("H. Tab 3 summary formulas")
    formulas = {c.coordinate: c.value for row in ws3.iter_rows(min_col=9, max_col=9) for c in row
                if isinstance(c.value, str) and c.value.startswith("=")}
    values = {c.coordinate: c.value for row in cached["Known_Exact_Matches"].iter_rows(min_col=9, max_col=9) for c in row}
    cols = {col: [c.value for c in ws3[col][1:]] for col in "BDEF"}

    def evaluate(formula):
        if m := re.fullmatch(r"=COUNTA\((\w+)!\$A:\$A\)-1", formula):
            return wb[m[1]].max_row - 1
        if m := re.fullmatch(r"=COUNTA\(\$([A-Z]):\$\1\)-1", formula):
            return sum(v is not None for v in cols[m[1]])
        if m := re.fullmatch(r'=COUNTIF\(\$([A-Z]):\$\1,"([^"]+)"\)', formula):
            return sum(v is not None and fnmatch.fnmatchcase(str(v), m[2]) for v in cols[m[1]])
        if m := re.fullmatch(r"=(I\d+)-(I\d+)", formula):
            return evaluate(formulas[m[1]]) - evaluate(formulas[m[2]])
        raise ValueError(formula)

    check("summary has formulas", len(formulas) >= 20, len(formulas))
    wrong = {k: (evaluate(f), values[k]) for k, f in formulas.items() if evaluate(f) != values[k]}
    check("stored formula results equal evaluated results", not wrong, wrong)

    print("\nDistribution")
    print("  Tab 1 by asset class:", dict(Counter(r["Asset_Class"] for r in internal)))
    print("  Tab 1 by counterparty:", dict(Counter(r["Counterparty_Code"] for r in internal)))
    print("  Tab 1 by trade date:", {str(k): v for k, v in sorted(Counter(r["Trade_Date"] for r in internal).items())})
    print("  Tab 2 by perspective:", dict(Counter(r["Reporting_Perspective"] for r in street)))
    print("  Tab 2 by quote convention:", dict(Counter(r["Price_Quote_Convention"] for r in street)))
    print("  Tab 2 blank Dir_Ind rows:", sum(r["Dir_Ind"] is None for r in street),
          "| negative quantity rows:", sum(r["Reported_Qty_Notional"] < 0 for r in street))
    print("  Known pairs, direction styles:", styles)
    print("  Known pairs, identifier sources:", sources)
    print("  Orphan decoys:", dict(decoys))

    print(f"\n{passes} checks passed, {len(failures)} failed.")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
