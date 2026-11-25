# Builder log, pair p08

1. Ran `git fetch` in the worktree root. No output of note.
2. Read `experiments/blind-profile-r2/BUILDER-RULES.md`.
3. Listed `experiments/blind-profile-r2/builder/p08` (empty/absent) and read `experiments/blind-profile-r2/_site/llms.txt`.
4. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers pairs/p08/left.csv pairs/p08/right.csv` (run from `experiments/blind-profile-r2`).
   - left: Event ID | Event Type | Replaces | Party | Deal Ref | ISIN | Side | Qty | Price | Trade Date
   - right: Trade No | Instrument | B/S | Quantity | Price | Booked On
   - Reading: left is an event log (lifecycle chain via Event Type + Replaces, scoped by Party); right is one row per trade, no party column.
5. Read `_site/docs/profiles/llms.txt`.
6. Read `_site/docs/reference/readme/reconcile-two-csv-files-with-a-profile.md` and `_site/docs/profiles/consolidation/llms.txt`.
7. Read `_site/docs/reference/profiles/lifecycle.md`, `_site/docs/reference/readme/consolidation.md`, `_site/docs/reference/profile-examples/a-lifecycle-chain.md`.
8. Read `_site/docs/profiles/normalization/llms.txt` and `_site/docs/reference/profiles/llms.txt`.
9. Read `_site/docs/reference/profiles/core-rules.md`, `trading-rules.md`, `diff.md`, `known-gaps.md`.
10. Profile change: wrote `profile-pass-1.json`. Exploratory: key ISIN only, every other shared column as Text economic, no lifecycle, culture en-US both sides, to see raw values.
11. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p08/profile-pass-1.json pairs/p08/left.csv pairs/p08/right.csv`
    - summary matched=71 exact=0 withinTolerance=0 break=71 leftOnly=18 rightOnly=0 ambiguous=269 searchIncomplete=0 quarantined=0 suppressed=0
    - Learned from engine-reported values: right numbers are de-DE style (`242,5604`, `2.700`), left en-US (`2,700`, `242.5604`); right ref `D-102570` vs left `D102570`; side `B/S` vs `BUY/SELL`; dates `2024-05-24` vs `20240524`. Ambiguity is expected: ISIN alone is not unique and left holds several events per trade.
12. Read `_site/docs/reference/profiles/sources-row-keys-and-reading.md` and `top-level-settings.md` (per-side `culture`).
13. Profile change: wrote `profile-pass-2.json`. Per-side cultures (left en-US, right de-DE); Ref key from `Deal Ref` / `Trade No` with `text.regex` removing `-`; Side as closed code map (left B/S -> BUY/SELL, right BUY/SELL, passthrough false); Quantity Decimal scale 0, Price Decimal scale 4, TradeDate Date (`yyyy-MM-dd` left, `yyyyMMdd` right). Three temporary probe Text fields (left Event Type, Replaces, Party; right a constant `-` via text.regex) only to make the engine report the left lifecycle vocabulary. No lifecycle yet.
14. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p08/profile-pass-2.json pairs/p08/left.csv pairs/p08/right.csv`
    - summary matched=600 exact=0 withinTolerance=0 break=600 leftOnly=160 rightOnly=0 ambiguous=160 searchIncomplete=0 quarantined=0 suppressed=0
    - Only the three probe fields broke; Isin, Side, Quantity, Price, TradeDate reported no breaks on the 600 matches. Event types seen: NEW, AMEND, CANCEL; Replaces names an Event ID (e.g. E002144); Party codes like BARC, SOCG. Left-only rows are CANCEL events; ambiguous groups are NEW+AMEND chains on one Ref (e.g. right qty 1080 vs a left AMEND 1090 in a 3-event chain), so lifecycle collapse is needed.
15. Profile change: wrote `profile-pass-3.json`. Removed the probe fields. Added lifecycle on left: Event Type mapped as a closed code vocabulary NEW->NEW, AMEND->CORR, CANCEL->CANC (passthrough false, so any other code would quarantine); Replaces -> EventTarget; Party -> Counterparty (Identity role, scope). corrTarget CANC (default). Match on Ref only; Isin, Side, Quantity, Price, TradeDate compared exactly, no tolerances.
16. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p08/profile-pass-3.json pairs/p08/left.csv pairs/p08/right.csv`
    - summary matched=760 exact=760 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=360
    - composition oneToOne=760 lifecycle=160; no field breaks, no diagnostics.
17. Profile change: wrote `profile-pass-4.json` (generated from pass 3 by a text replace of the profile file only): Quantity scale 0 -> 6, Price scale 4 -> 10, to check that rounding to scale is not hiding sub-unit differences.
18. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p08/profile-pass-4.json pairs/p08/left.csv pairs/p08/right.csv`
    - summary matched=760 exact=760 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=360
19. Profile change: wrote `profile-pass-5.json` = pass 4 with corrTarget `DIRECT` instead of `CANC`, to test whether the result depends on how an AMEND that names a CANCEL is read.
20. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p08/profile-pass-5.json pairs/p08/left.csv pairs/p08/right.csv`
    - summary matched=760 exact=760 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=360
    - Identical, so no AMEND targets a CANCEL in this data; the setting does not affect the result. Final profile: `profile-pass-4.json` (default corrTarget, finest scales).
21. Wrote `report.md`.
22. Correction to 21: the Write tool refused to create `report.md` (subagent report files blocked). Not worked around; the report content is returned in the structured result instead.
