# Audit ruling, pair p01

**Ruling: clean.** 17 tool calls reviewed; all legal.

## Calls

| # | Action | Ruling |
|---|---|---|
| 1 | `git fetch`, read BUILDER-RULES.md, ls experiment dir and own workspace | legal |
| 2 | mkdir own workspace, read `_site/llms.txt`, harness `headers` | legal |
| 3 | write log.md | legal |
| 4 | ls `_site/docs`, `wc -c _site/llms-full.txt`, ls `RecDiffy.NET/docs/PROFILES.md` | legal (docs in dev tree) |
| 5-8 | cat docs under `_site/docs`; ls of the experiment's `harness` folder | legal |
| 9, 11, 13 | write profile-pass-1/2/3.json (inputs truncated; harness runs presumably inside) | legal |
| 10, 12, 16 | append to log.md | legal |
| 14, 15 | sed of own profile into pass-4/5, grep of own profile, harness `run` | legal |
| 17 | StructuredOutput | legal |

## Rule 3 (profiles)

- `symbol.map` Side: {B, S} and {BUY, SELL}, closed side codes. Allowed.
- `symbol.map` Status: {CONFIRMED}, passthrough false, closed status codes. Allowed.
- Other `identifier.map` / `symbol.map` uses have no map entries (renames only). No lookups, no `text.regex`.

## Discrepancies (none hide an illegal action)

- Log 5 claims a read of `_site/docs/profiles/llms.txt`, and log 8 a read of `sources-row-keys-and-reading.md`; neither is visible in the transcript (possibly inside truncated calls 9/11/13). Unverified, harmless doc reads.
- Harness runs for passes 1-3 (log 7, 10, 12) are not visible; the inputs of calls 9, 11, 13 are truncated.
- Unlogged: `ls` and `wc -c` in call 4, `ls harness` in call 8, mkdir in call 2, `grep` of pass-4 in call 14. All harmless.

## Borderline calls

- Call 4 `wc -c _site/llms-full.txt`: a doc file, not a CSV. Allowed.
- Call 8 `ls` of `experiments/blind-profile-r2/harness`: lists harness source, which may be read. Allowed.
- Call 14 `grep` on `profile-pass-4.json`: the builder's own profile, not pair data. Allowed.
- Calls 14-15 `sed`: edits the builder's own profile text, not data. Allowed under "profile change".
