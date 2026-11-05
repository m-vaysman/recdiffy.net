# Trade reconciliation test dataset: model outputs

On 2026-11-04 the prompt in [`prompt.md`](prompt.md) was sent verbatim through OpenRouter to four models. Each model replied with a Python/openpyxl generator script, because none can produce files over the API. Each script was run as returned, and the resulting files were validated. Each folder holds the verbatim reply (`response.md`), the extracted `generate.py`, any generated `RecDiffy_Trade_Reconciliation_Dataset.xlsx` / `RecDiffy_Hidden_Test_Oracle.json`, and `NOTES.md` with token counts, fixes and the full validation.

| Model | OpenRouter id | Script ran? | Valid files? | Internal / Counterparty / Known pairs | Oracle JSON | Caveats |
|---|---|---|---|---|---|---|
| [Grok 4.7](grok-4.7/) | `x-ai/grok-4.7` (reasoning high) | No: `ValueError` on its own 9-char CUSIP literals, then a second LEI error | No files | n/a | n/a | First attempt returned only a preamble; reran once |
| [Qwen 3.8](qwen-3.8/) | `qwen/qwen3.8-2.4t-a95b` | Yes, unmodified | Yes | 300 / 200 / 120 | Parses; all 7 blind groups at the requested counts | Blind rows leak: `DK_UNCONFIRMED` / `ALLOCATION` labels and aligned ID numbers |
| [Claude Sonnet 5.5](claude-sonnet-5.5/) | `anthropic/claude-sonnet-5.5` | Yes, unmodified | Yes | 300 / 200 / 120 | Parses; all 7 blind groups at the requested counts; every row in exactly one group | No leaks found |
| [DeepSeek V4 Pro](deepseek-v4-pro/) | `deepseek/deepseek-v4-pro` | Only after a mechanical fix (`inst[...]` → `inst.get(...)`, 11 sites; see NOTES) | Yes (from `generate.patched.py`) | 300 / 200 / 120 | Parses; all 7 blind groups at the requested counts | Aligned ID numbers; some near-matches and breaks show no difference in the fields checked |

The requested blind groups were: near-matches 18↔18, breaks 20↔20, N-to-1 splits 36↔12, multi-leg 16↔8, cancel/correct 5↔15, internal orphans 85, alleged trades 7.
