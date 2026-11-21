# Audit ruling, pair p05

**Ruling: clean**

**Actions reviewed:** 26 tool calls (transcript calls 1-26), plus 4 profiles (`profile-pass-1..4.json`) checked against rule 3.

## Violations

None.

- CSVs were touched only through the harness (`headers` in call 2; `run` in calls 13, 14, 18, 22, 23, 24).
- Other reads: `BUILDER-RULES.md`, `_site/llms.txt`, `RecDiffy.NET/docs/PROFILES.md` (wc/grep/Read), `harness/Program.cs`, `harness/Reports.cs`. All docs or harness source.
- Writes and edits went only to `builder/p05/` (log, profiles). `sed`, `diff`, `cat -A` and Edit were used on its own profile files only.
- Rule 3: the only maps are `Side` `B/S -> BUY/SELL` and `Dir` `buy/sell -> BUY/SELL`, both closed code vocabularies. `symbol.map` and `identifier.map` without entries are plain copies. The `text.regex` patterns (`^BLK-([0-9]{4})-0*([0-9]+)$`, `^0*([0-9]+)/([0-9]{4})$`) are structural, with no value alternations.
- No scripts touched data. The engine and harness were not changed. No other branches, pairs, `_hidden`, or temp folders were read.

## Log discrepancies

1. Log item 10 claims a `grep` of `RecDiffy.NET/src/RecDiffy/Consolidation/*.cs`. No such call appears in the transcript. A claimed action that did not happen. It would have been legal anyway.
2. The transcript shows four `run` calls of `profile-pass-2.json` (13, 14, 18, 22), with two sed fixes (13, 14) and two Read+Edit rounds (15-17, 19-21). The log (items 12-13) describes one failure and one rerun.
3. Not logged: call 4 (`wc -c` and `grep '^#'` on PROFILES.md), call 6 (`ls -R harness`), call 2's and call 1's `ls` of `pairs/p05` and the experiment folder, and the `diff` checks in calls 23-24.
4. Log items 6-18 were appended in batches (calls 9 and 25), not as each action happened. Item 18 says `report.md` was written, but no visible call writes it. The truncated call 25 probably did. The file exists in the workspace.

None of these hides an illegal action.

## Borderline calls (allowed)

- **`ls experiments/blind-profile-r2/pairs/p05` (call 2), plus `ls` of the experiment folder (call 1).** These list file names only. No CSV content was read, so rule 1 is not broken.
- **`git fetch` (call 1).** Explicitly legal.
- **Pass 4, a control profile keyed on economic fields.** It is a harness run of an engine profile with no hand-built mapping, so it is allowed.
- **`cat -A` / `sed -n` on `profile-pass-2.json` (calls 11-12).** These read the builder's own file, not data.
