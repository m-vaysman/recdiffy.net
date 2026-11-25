# Audit ruling, pair p06

**Ruling: clean**

Actions reviewed: 20 tool calls (transcript), 26 log entries, 6 profiles.

## Violations

None.

## Per-call rulings

| # | Action | Ruling |
|---|---|---|
| 1 | git fetch, read BUILDER-RULES.md, ls experiment and builder folders | legal |
| 2 | mkdir own workspace, read `_site/llms.txt`, ls `_site` | legal |
| 3 | read profiles docs index, ls docs, harness `headers` | legal |
| 4-6 | read docs pages | legal |
| 7 | ls/grep harness source | legal (harness source) |
| 8 | write log.md | legal |
| 9 | write profile-pass-1.json (rest of command truncated in transcript) | legal as visible |
| 10 | read harness Program.cs, Reports.cs | legal |
| 11-12 | append log.md (rest truncated) | legal as visible |
| 13-14 | ls/read docs | legal |
| 15 | write profile-pass-3.json (rest truncated) | legal as visible |
| 16-18 | sed on own profile-pass-3.json to make passes 4-6, grep own profile, harness `run` | legal |
| 19 | append log.md (rest truncated) | legal as visible |
| 20 | StructuredOutput | legal |

## Rule 3 check

- `symbol.map` with a map: Side only, `BUY/SELL` and `B/S`, a closed side-code vocabulary. Allowed.
- `symbol.map` without a map: passthrough copies (Cpty Ref, probe columns in pass 2). No values listed.
- `text.regex` `^TR-` -> "": a general prefix strip, not a value alternation. Allowed.
- No `lookup`; `identifier.map` is scheme-tagging only.

## Discrepancies (none hide an illegal action)

- Calls 8, 9, 11, 12, 15, 19 are truncated at about 600 characters. Commands chained after the heredocs (harness runs for passes 1-3, the pass-2 profile write, the read of `sources-row-keys-and-reading.md` in log 16, and the report.md write) are not visible, so they cannot be verified against log 12, 14-16, 19 and 26. The resulting files match the log's descriptions.
- Directory listings (`ls _site`, `ls -R _site/docs/profiles`, `ls _site/docs/reference/profiles`, `ls -R harness`) are not logged. Minor.
- Log order differs from call order: the pass-1 profile and log entries were written in batches after the fact (log 11-15 appended in calls 11-12).

## Borderline calls

- Calls 16-18: `sed` and `grep` run on the builder's own profile JSON, not on pair data. Allowed as profile writes in its own workspace.
- Call 7/10: grep/read of `experiments/blind-profile-r2/harness/`. Harness source is explicitly allowed.
- Pass 2 probe: copying every column raw as Symbol to see values in the `run` digest. It reads data only through `run`, so it is within rule 1.
