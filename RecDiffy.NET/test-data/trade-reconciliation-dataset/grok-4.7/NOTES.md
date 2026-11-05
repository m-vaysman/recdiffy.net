# Grok 4.7: run notes

- **Model id:** `x-ai/grok-4.7` (OpenRouter, provider xAI), `reasoning.effort = high`
- **Date:** 2026-11-04
- **Prompt:** `../prompt.md`, sent verbatim as the single user message

## Attempts
1. `max_tokens` 32000. Finish reason `stop`, 35,403 completion tokens (35,364 reasoning). The visible reply was only a 225-character preamble ("Checking the environment, then generating both files."), with no script and no tool call. It is saved as `response-attempt1.md`.
2. Rerun once with `max_tokens` 80000. Finish reason `stop`, 3,114 prompt / 89,478 completion tokens (59,612 reasoning). This is `response.md`, and the script is extracted to `generate.py`.

## Result: the script does not run
`python3 generate.py` (Python 3.13, openpyxl 3.1.5) fails before writing anything:

```
File "generate.py", line 140, in cusip_check_digit
    raise ValueError(base8)
ValueError: X00001AA1
```

The FX-forward and swap reference rows pass 9-character literals (for example `"X00001AA1"`) where the code requires an 8-character CUSIP base. As a diagnostic only, trimming those 8 literals to 8 characters got past that point, but the script then hit a second data error in the LEI builder, `RuntimeError: JPM000000000001` (line 322). That is beyond a trivial mechanical fix, so no further patching was done and no patched script is committed.

**No `.xlsx` or `.json` was produced.**
