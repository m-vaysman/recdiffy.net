# Builder log, pair p02

1. `git fetch` (no checkout). Read `experiments/blind-profile-r2/BUILDER-RULES.md`.
2. Listed `builder/p02` (empty/new); read `_site/llms.txt`.
3. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers pairs/p02/left.csv pairs/p02/right.csv`
   - left: TradeId | Isin | Side | Shares | Px | Net | TradeDate | Ccy | Book
   - right: Ref | Security | Action | Size (000s) | Px (minor) | Proceeds (000s) | Dealt | ccy | Allocated
   - Hypothesis from headers: Ref~TradeId, Security~Isin, Action~Side, Size in thousands of shares, Px in minor units (x100), Proceeds in thousands, Dealt~TradeDate, ccy~Ccy, Allocated~Book.
4. Read docs: `_site/docs/profiles/llms.txt`, `_site/docs/reference/readme/reconcile-two-csv-files-with-a-profile.md`, `_site/docs/reference/profiles/the-shape-of-a-profile.md`, `_site/docs/profiles/normalization/llms.txt`, `_site/docs/profiles/fields/llms.txt`, `_site/docs/reference/profiles/core-rules.md`, `_site/docs/reference/profiles/trading-rules.md`.
5. Listed `experiments/blind-profile-r2` and `_bin`; read harness source `harness/Program.cs` and `harness/Reports.cs` (compiles with core + trading pack, ReferenceDataSet.Empty, comma CSV with header).
6. Read docs: `_site/docs/reference/profiles/fields-the-canonical-schema.md`, `sources-row-keys-and-reading.md`, `_site/docs/profiles/passes/llms.txt`, `_site/docs/profiles/diff/llms.txt`, `_site/docs/reference/profiles/passes.md`, `diff.md`, `conditions.md`.
7. Profile change: wrote `profile-pass-1.json`, a probe. Every column copied with `symbol.map` into Symbol fields (Isin as Key, the rest Economic), one `match.naturalKey` pass on raw Isin, to see each side's raw value shapes in the unmatched examples.
8. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p02/profile-pass-1.json experiments/blind-profile-r2/pairs/p02/left.csv experiments/blind-profile-r2/pairs/p02/right.csv`
   - summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=1000 rightOnly=1000 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - Seen in the engine's canonical examples: left Ref `TRD-0001007`, right `trd/3459`; right ISIN lower case, sometimes with a hyphen (`gb-04ejgrc4g3`); Side B/S vs BOT; right Size 3.33 (thousands), Px 77324.36 (minor, x100 of left's 4-dp price), Proceeds 2574.90119 (thousands); dates `20240313` vs `9-Sep-2024`; Ccy upper vs lower; left Book `ACC-nnnn`, right Allocated `Y` (not the same thing).
9. Read doc `_site/docs/reference/readme/declare-how-each-file-is-read.md`.
10. Profile change: wrote `profile-pass-2.json`. RefNo = digits after the prefix with leading zeros dropped (`text.regex ^\D*0*(\d+)$ -> $1`, passthrough false) on both sides, matched by `match.naturalKey` on RefNo. ISIN: strip non-alphanumerics, upper case (Key). Side: closed code map B/S and BOT/SLD to BUY/SELL, passthrough false so any other code quarantines. Ccy upper case. Qty/Px/Net: left `decimal.parse`; right `decimal.scale` factor 1000, 0.01, 1000. Dates: left `yyyyMMdd`, right `d-MMM-yyyy`. Book and Allocated kept as uncompared Text. Scales 0/4/2. No tolerances.
11. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p02/profile-pass-2.json experiments/blind-profile-r2/pairs/p02/left.csv experiments/blind-profile-r2/pairs/p02/right.csv`
   - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
12. Profile change: wrote `profile-pass-3.json` from pass 2. Raised scales (Qty 6, Px 10, Net 8) to check that rounding to scale does not hide differences; Allocated made a Symbol with `symbol.map {"Y":"Y"}` passthrough false, so any other Allocated code would quarantine (vocabulary probe).
13. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p02/profile-pass-3.json experiments/blind-profile-r2/pairs/p02/left.csv experiments/blind-profile-r2/pairs/p02/right.csv`
   - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - So no rounding hides a difference, and every right row has Allocated = Y.
14. Profile change: wrote `profile-pass-4.json` from pass 3. Cross-check of the ref mapping: RefNo made a compared Key field, and the pass keys on Isin+Side+Qty+TradeDate instead of RefNo, so the pairing is found independently of the ref transform.
15. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p02/profile-pass-4.json experiments/blind-profile-r2/pairs/p02/left.csv experiments/blind-profile-r2/pairs/p02/right.csv`
   - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - The economic pairing agrees with the ref pairing on all 1000 rows (RefNo compared Exact).
16. Final choice: `profile-pass-3.json`. Tried to write `report.md`; the agent tool refused it (subagents may not write report files), so the report content is returned in the builder's structured result instead.
