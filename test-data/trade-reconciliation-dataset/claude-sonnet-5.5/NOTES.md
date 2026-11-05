# Claude Sonnet 5.5: run notes

- **Model id:** `anthropic/claude-sonnet-5.5` (OpenRouter, provider Claude Platform on AWS)
- **Date:** 2026-11-04
- **Prompt:** `../prompt.md`, sent verbatim as the single user message

## Attempts
1. `max_tokens` 32000. Finish reason `length`: all 32,000 tokens went to reasoning and the reply was empty.
2. Rerun with `max_tokens` 100000. Finish reason `stop`, 3,223 prompt / 71,686 completion tokens (50,253 reasoning). This is `response.md`.

## Script
`generate.py` is the Python block of the reply, unmodified. It ran cleanly on the first try (Python 3.13, openpyxl 3.1.5), and its own assertions passed: `Internal=300 Counterparty=200 Known pairs=120`. **No fixes were applied.**

## Validation of the generated files
| Check | Result |
|---|---|
| Sheet names | `Internal_Trades`, `Counterparty_Trades`, `Known_Exact_Matches` ✔ |
| Internal_Trades data rows | 300 (all IDs unique) ✔ |
| Counterparty_Trades data rows | 200 (all IDs unique) ✔ |
| Known_Exact_Matches | 120 pairs in A:G, all IDs present in tabs 1 and 2. The summary table is in columns I:J. ✔ |
| Oracle JSON parses | ✔ |
| Oracle coverage | 275 groups covering all 300 internal and 200 counterparty IDs, each row in exactly one group ✔ |
| Scenario groups | exact 120 (120↔120), near-match 18 (18↔18), breaks 20 (20↔20), N-to-1 splits 12 (36↔12), multi-leg 8 (16↔8), cancel/correct 5 (5↔15), internal orphans 85, alleged trades 7 ✔ |
| Normalization mix | 12 `32NDS_UST`, 8 `DIRTY_PCT_PAR`, 15 `DEALER_BILATERAL_VIEW`, 5 `CANC` and 5 `CORR`, 51 blank `Dir_Ind`, 47 negative counterparty quantities ✔ |
| Exact pairs agree on net cash (absolute value) | 120 / 120 ✔. All 18 near-matches differ on net cash. |
| Leakage | Report and trade IDs are random, with no shared numbering. Tabs 1 and 2 have no scenario labels. ✔ |

## Notes
- Counterparty dates are strings in per-broker formats (ISO, `MM/DD/YYYY`, `YYYYMMDD` and others). The model chose this as extra noise and documents it in the oracle metadata.
