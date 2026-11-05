# DeepSeek V4 Pro: run notes

- **Model id:** `deepseek/deepseek-v4-pro` (OpenRouter, served by Relace)
- **Date:** 2026-11-04
- **Prompt:** `../prompt.md`, sent verbatim as the single user message
- **Run:** `max_tokens` 32000. Finish reason `stop`, 2,042 prompt / 21,234 completion tokens (3,018 reasoning). One attempt.

## Script
`generate.py` is the Python block of the reply, **unmodified**. As written, it fails:

```
File "generate.py", line 650
    prim_id = inst["SEDOL"]
KeyError: 'SEDOL'
```

FX-forward and swap instruments have no `SEDOL`/`ISIN`/`CUSIP` key. The code right after the failing line already says "If inst doesn't have that field ... fallback to Ticker" (`if not prim_id: prim_id = inst["Ticker"]`), so the intent was a missing-key-tolerant lookup.

**Mechanical fix, in `generate.patched.py`:** the same change at 11 sites, `inst["ISIN"]` / `inst["CUSIP"]` / `inst["SEDOL"]` → `inst.get(...)` (lines 646, 648, 650 and 8 lines of the form `inst["ISIN"] or inst["Ticker"]`). A narrower patch (only line 650, then lines 646 to 650) still failed on the next site. No other changes were made. **The committed `.xlsx` and `.json` come from `generate.patched.py`.**

## Validation of the generated files (from the patched script)
| Check | Result |
|---|---|
| Sheet names | `Internal_Trades`, `Counterparty_Trades`, `Known_Exact_Matches` ✔ |
| Internal_Trades data rows | 300 (all IDs unique) ✔ |
| Counterparty_Trades data rows | 200 (all IDs unique) ✔ |
| Known_Exact_Matches | 120 pairs, all IDs present in tabs 1 and 2, plus a summary table below them ✔ |
| Oracle JSON parses | ✔ |
| Oracle coverage | all 300 internal and 200 counterparty IDs ✔ |
| Scenario groups | exact 120, near-match 18 (18↔18), breaks 20 (20↔20), N-to-1 splits 12 (36↔12), multi-leg 8 (16↔8), cancel/correct 5 (5↔15), internal orphans 85, alleged trades 7 ✔ |
| Normalization mix | 12 `32NDS_UST`, 8 `DIRTY`, 15 `DEALER_BILATERAL_VIEW`, 5 `CANC` and 5 `CORR` ✔ |
| Exact pairs agree on net cash (absolute value) | 120 / 120 ✔ |

## Problems found
- **Aligned IDs:** pairs share their number (`IT0121` ↔ `CP0121`, for all 120 exact, 18 near and 20 break pairs), and the number ranges map to the scenario bands, so the blind rows can be matched on the ID alone.
- **Weak scenarios:** 3 of the 18 near-matches and 3 of the 20 "breaks" show no difference in net cash, accrued, quantity or settle date. They may differ only in price or fees; this check didn't cover those fields.
- **Inconsistent leg labels:** `Leg_Structure` uses `LEG_1_PAY`/`LEG_2_RECEIVE` on 6 pairs but `LEG_1`/`LEG_2` on 2.
