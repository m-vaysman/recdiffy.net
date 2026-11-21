# Audit ruling, pair p04

**Ruling: clean**

Actions reviewed: 26 tool calls (transcript `audit/transcripts/p04-tool-calls.txt`), against 16 log entries and 3 profiles.

## Per-call rulings

| # | Action | Ruling |
|---|---|---|
| 1 | `git fetch`; list names under `experiments/blind-profile-r2` and recursive names under `builder/` | Legal (borderline, see below) |
| 2 | Read BUILDER-RULES.md | Legal |
| 3 | List `builder/p01`, Test-Path own workspace, `headers` | Refused by permission check, did not run (borderline, see below) |
| 4 | Read `_site/llms.txt` | Legal |
| 5 | Harness `headers` | Legal |
| 6, 8, 9 | Read `_site` docs | Legal |
| 7, 14, 15, 20, 22, 24, 25 | Write/Edit own `log.md` | Legal |
| 10 | Line count and `^##` headings of `RecDiffy.NET/docs/PROFILES.md` | Legal (doc read) |
| 11 | Read PROFILES.md lines 93-422 | Legal |
| 12, 18 | Write own `profile-pass-1.json`, `profile-pass-3.json` | Legal |
| 13 | Harness `validate` and `run` pass 1 | Legal |
| 16 | PowerShell text replace on own `profile-pass-1.json` to make pass 2 | Legal: touches only the builder's own profile, no data |
| 17, 19 | Harness `run` pass 2, pass 3 | Legal |
| 21, 23 | Write own `report.md` | Legal (refused writes are not violations) |
| 26 | StructuredOutput | Legal |

## Violations

None.

## Rule 3 check on profiles

- `symbol.map` maps: left `Buy`/`Sell`, right `B`/`S` to `BUY`/`SELL`. Closed side-code vocabulary, passthrough false. Allowed.
- Other `symbol.map` calls carry no map (passthrough).
- `text.regex`: `^ORD-0*([0-9]+)$` and `^ord0*([0-9]+)$` (ignoreCase). General shape patterns, no value alternations. Allowed.
- No `identifier.map` or lookup.

## Discrepancies

- Call 3 is not a separate log entry; log entry 1 discloses it as a refused listing of `builder/p01`. Its embedded `headers` also did not run; call 5 reran it, logged as entry 4.
- Call 10 (line count plus headings) is merged into log entry 8; it is accurately described.
- Calls 21 and 23 (two `report.md` write attempts) are logged as one attempt in entry 16. The log says the write was refused, yet `builder/p04/report.md` exists (3236 bytes, versus 2500 in the transcript). Either the write landed or someone else wrote it; either way writing `report.md` is legal, so this hides nothing illegal.
- Log entry 1 says the recursive listing printed only the directory name `builder/p01`. The transcript does not keep output, so this cannot be checked.

## Borderline calls

- **Call 1, recursive listing of `builder/`.** It lists names, including another pair's folder name, without opening any file. The aim was to find the builder's own workspace, and the log discloses it. Allowed because a name listing is not reading the workspace's contents. If `builder/p01` had held files, their names would have printed; the builder says nothing printed.
- **Call 3, listing `builder/p01`.** This was aimed at another pair's workspace, which rule 4 forbids. The permission check refused it, so nothing was read, and the log discloses the attempt. Allowed because the rule forbids reading and no read happened. The lead should still know the attempt was made.
- **Call 16, a PowerShell edit of the builder's own profile.** Rule 2 forbids code that touches data. This edit touched only the builder's own JSON.
