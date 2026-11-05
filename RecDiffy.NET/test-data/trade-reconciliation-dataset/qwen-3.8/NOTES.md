# Qwen 3.8: run notes

- **Model id:** `qwen/qwen3.8-2.4t-a95b` (OpenRouter, served by SiliconFlow on the final run)
- **Why this id:** the newest Qwen 3.8 flagship, `qwen/qwen3.8-max-prime`, returned a 404 because no endpoint meets this OpenRouter account's zero-data-retention (ZDR) setting. `qwen3.8-2.4t-a95b` is the largest Qwen 3.8 model with a ZDR-eligible endpoint.
- **Date:** 2026-11-04
- **Prompt:** `../prompt.md`, sent verbatim as the single user message

## Attempts
1. `max_tokens` 32000. Finish reason `length`: all 32,000 tokens went to reasoning and the reply was empty.
2. Rerun with `max_tokens` 120000. Finish reason `stop`, 1,986 prompt / 56,037 completion tokens (41,435 reasoning). This is `response.md`.

## Script
`generate.py` is the largest Python block of the reply, unmodified. It ran cleanly on the first try (Python 3.13, openpyxl 3.1.5). **No fixes were applied.**

## Validation of the generated files
| Check | Result |
|---|---|
| Sheet names | `Internal_Trades`, `Counterparty_Trades`, `Known_Exact_Matches` ✔ |
| Internal_Trades data rows | 300 (all IDs unique) ✔ |
| Counterparty_Trades data rows | 200 (all IDs unique) ✔ |
| Known_Exact_Matches | 120 pairs, all IDs present in tabs 1 and 2, plus a summary table below them ✔ |
| Oracle JSON parses | ✔ |
| Oracle coverage | all 300 internal and 200 counterparty IDs ✔ |
| Scenario groups | near-match 18 (18↔18), breaks 20 (20↔20), N-to-1 splits 12 (36↔12), multi-leg 8 (16↔8), cancel/correct 5 (5↔15), internal orphans 85, alleged trades 7 ✔ |
| Normalization mix | 12 `32NDS_UST`, 8 `DIRTY`, 15 `DEALER_BILATERAL_VIEW`, 5 `CANC` and 5 `CORR` ✔ |
| Exact pairs agree on net cash (absolute value) | 120 / 120 ✔ |

## Problems found: the blind rows are not blind
- **Labels in Tab 1:** `Portfolio_Book = DK_UNCONFIRMED` on all 85 orphan rows and `OPERATIONS_BREAKS` on 5 rows. `Leg_Structure = ALLOCATION` on all 36 allocation rows.
- **Aligned IDs:** every pair shares its numeric ID (`INT-000121` ↔ `CPTY-000121`, for all 120 exact, 18 near and 20 break pairs), and the ID ranges map to the scenario bands. An engine could "match" on the ID number alone.
