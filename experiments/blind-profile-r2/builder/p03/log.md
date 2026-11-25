# Builder log, pair p03

1. Command: `git fetch` (no checkout). AGENTS.md was already in context.
2. Read `experiments/blind-profile-r2/BUILDER-RULES.md`.
3. Listed `builder/p03` (did not exist yet); read `_site/llms.txt`.
4. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers pairs/p03/left.csv pairs/p03/right.csv`
   - left: Entry ID | Post Date | Amount | Currency | Rate | Counterparty | Memo
   - right: Value Date | Bank Ref | Minor Units | D/C | Ccy | Rate % | Narrative
5. Read `_site/docs/profiles/llms.txt`, `_site/docs/reference/readme/reconcile-two-csv-files-with-a-profile.md`, `_site/docs/reference/profiles/the-shape-of-a-profile.md`, `_site/docs/profiles/normalization/llms.txt`.
6. Read `_site/docs/reference/profiles/core-rules.md`, `_site/docs/reference/profiles/conditions.md`.
7. Read index lines of `_site/docs/profiles/fields/llms.txt`, `passes/llms.txt`, `diff/llms.txt`.
8. Read `_site/docs/reference/profiles/fields-the-canonical-schema.md`, `passes.md`, `sources-row-keys-and-reading.md`.
9. Created `builder/p03/`; listed `experiments/blind-profile-r2` top-level names (builder, harness, pairs, _bin, _site, BUILDER-RULES.md).
10. Read `experiments/blind-profile-r2/harness/Program.cs` and `harness/Reports.cs` (harness source, to understand the digest).
11. Read `_site/docs/reference/profiles/diff.md`.
12. Profile change: wrote `profile-pass-1.json`, a probe. Row keys Entry ID / Bank Ref; Ref as an identifier key; every other column copied into Text fields so the digest shows canonical values per side.
13. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p03/profile-pass-1.json experiments/blind-profile-r2/pairs/p03/left.csv experiments/blind-profile-r2/pairs/p03/right.csv`
   - summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=1000 rightOnly=1000 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - Seen in canonical examples: left Ref `GL-2024-400029`, right `2024/402595`; left date `yyyy-MM-dd`, right `M/d/yyyy`; left Amount signed with thousands commas, right Minor Units unsigned integer plus D/C (C, D); left Rate like 0.0042, right Rate % like 1.96; Memo and Narrative share a vocabulary ("Wire in"); right has no counterparty column.
14. Profile change: wrote `profile-pass-2.json`. Ref: left `text.regex` `^GL-(\d{4})-(\d+)$` -> `$1/$2` (passthrough false), right `symbol.map` of Bank Ref. Date: left `yyyy-MM-dd`, right `M/d/yyyy`, strict. Amount scale 2: left `decimal.parse`; right `decimal.scale` of Minor Units with factor 0.01 when D/C = C and -0.01 when D/C = D (closed two-code vocabulary; guess D = debit = negative), plus `required.present` on Amount so any other D/C code quarantines. Rate scale 6: left parse, right Rate % times 0.01. Currency and Memo/Narrative as Economic symbols, compared exactly. Counterparty left-only Text (right has no such column). No tolerances.
15. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p03/profile-pass-2.json experiments/blind-profile-r2/pairs/p03/left.csv experiments/blind-profile-r2/pairs/p03/right.csv`
   - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
16. Profile change: wrote `profile-pass-3.json`, a copy of pass 2 with Amount scale 8 and Rate scale 12, to check that rounding to scale 2 / 6 is not hiding differences. Wrote `profile-pass-4.json`, a copy of pass 2 with the D/C signs swapped (C negative, D positive), as a negative control that the sign rule and the Amount comparison really bite. Both made by string replacement of pass 2 in PowerShell (profile text only; no data read).
17. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p03/profile-pass-3.json experiments/blind-profile-r2/pairs/p03/left.csv experiments/blind-profile-r2/pairs/p03/right.csv`
   - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
18. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p03/profile-pass-4.json experiments/blind-profile-r2/pairs/p03/left.csv experiments/blind-profile-r2/pairs/p03/right.csv`
   - summary matched=1000 exact=0 withinTolerance=0 break=1000 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - Amount break=1000, ratio=-1@1.00, sameSign=0.00: every amount is non-zero and the swap flips each one, so D = negative (pass 2) is the right convention and the comparison is live.
19. Decision: final profile is `profile-pass-2.json`.
20. Tried to write `report.md` with the Write tool; the tool refused it ("Subagents should return findings as text, not write report files"). I did not work around the refusal. The report content is returned in the structured result to the orchestrator instead.
