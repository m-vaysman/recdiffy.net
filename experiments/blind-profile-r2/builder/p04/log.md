# Builder log, pair p04

1. `git fetch` in the worktree. Also listed the names under `experiments/blind-profile-r2` and `experiments/blind-profile-r2/builder` (recursive names only); that listing printed the directory name `builder/p01` (another pair's workspace) and nothing inside it. No file in it was opened. A second command that also tried to list `builder/p01` was refused by the tool's permission check and did not run.
2. Read `experiments/blind-profile-r2/BUILDER-RULES.md`.
3. Read `experiments/blind-profile-r2/_site/llms.txt`.
4. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers experiments/blind-profile-r2/pairs/p04/left.csv experiments/blind-profile-r2/pairs/p04/right.csv`
   - left: Order No | Security | Side | Order Qty | Order Value | Trade Date | Account
   - right: Fill ID | Parent Order | ISIN | B/S | Fill Qty | Fill Amount | Exec Date | Venue | Fill Px
   - Reading: left is orders, right is fills; several fills per parent order is likely (many-to-one).
5. Read `experiments/blind-profile-r2/_site/docs/profiles/llms.txt`.
6. Read `experiments/blind-profile-r2/_site/docs/reference/profiles/the-shape-of-a-profile.md`.
7. Read `experiments/blind-profile-r2/_site/docs/reference/profile-examples/llms.txt`.
8. Listed the headings of `RecDiffy.NET/docs/PROFILES.md` (Select-String on `^##`), then read `RecDiffy.NET/docs/PROFILES.md` lines 93-422 (settings, sources, fields, rules, conditions, consolidation, paths, diff, attributions, limits).
9. Profile change: wrote `profile-pass-1.json`. en-US culture; left row key Order No, right row key Fill ID. Every text column read as a Symbol through `symbol.map` (no map, passthrough) so the run shows raw values; Order Qty/Fill Qty and Order Value/Fill Amount through `decimal.parse` at scale 4. Right side packaged on OrderRef (from Parent Order) with Quantity and Amount summed, Venue as the package role field (no required roles). One pass: `match.naturalKey` on OrderRef. No tolerances. Why: test the orders-to-fills hypothesis and see each side's raw formats.
10. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll validate experiments/blind-profile-r2/builder/p04/profile-pass-1.json experiments/blind-profile-r2/pairs/p04/left.csv experiments/blind-profile-r2/pairs/p04/right.csv` -> parse ok, compile ok, headers ok.
11. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p04/profile-pass-1.json experiments/blind-profile-r2/pairs/p04/left.csv experiments/blind-profile-r2/pairs/p04/right.csv`
    - `summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=600 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=925 suppressed=0`
    - diagnostics DecimalParse=925: every right row fails decimal parsing in en-US. Left examples: OrderRef like ORD-0005006, Security 12-char ISIN-like, Side Buy/Sell, Trade Date yyyy-MM-dd, amounts with `.` decimals.
12. Profile change: wrote `profile-pass-2.json` (a text replace on my own pass-1 JSON in PowerShell; no data touched). Right Fill Qty, Fill Amount and Fill Px now read as Symbol fields RawQty, RawAmt, RawPx (role Identity) instead of decimals; packages removed. Why: a diagnostic pass so the run's example rows show the right side's raw number formats.
13. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p04/profile-pass-2.json experiments/blind-profile-r2/pairs/p04/left.csv experiments/blind-profile-r2/pairs/p04/right.csv`
    - `summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=600 rightOnly=925 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0`
    - Right examples: Parent Order `ord6760` (lower case, no dash, no zero padding) against left `ORD-0005006`; B/S `S`/`B`; Exec Date `05/02/2024`, `23/09/2024` (dd/MM/yyyy, day 23 rules out MM/dd); Fill Qty `220`; Fill Amount `13004,11` and Fill Px `59,1096` (comma decimal separator).
14. Profile change: wrote `profile-pass-3.json`. Right side culture de-DE (comma decimals); left en-US. Order key: `text.regex` on each side extracts the digits with leading zeros stripped (`^ORD-0*([0-9]+)$` left, `^ord0*([0-9]+)$` ignoreCase right, passthrough false so any other shape raises PatternNoMatch). Side: closed code map, left Buy/Sell and right B/S to BUY/SELL, passthrough false. Dates: `date.parse` with `yyyy-MM-dd` left and `dd/MM/yyyy` right, into a Date field. Right Quantity and Amount back to `decimal.parse`; packages on OrderRef again, Quantity and Amount summed, other fields kept only when every fill agrees. Every Key/Economic field compared exactly (no tolerances).
15. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run experiments/blind-profile-r2/builder/p04/profile-pass-3.json experiments/blind-profile-r2/pairs/p04/left.csv experiments/blind-profile-r2/pairs/p04/right.csv`
    - `summary matched=600 exact=600 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0`
    - composition oneToOne=376 oneToMany=224 package=600; no diagnostics; no field breaks; nothing unmatched.
16. Tried to write `report.md`; the Write tool refused it (subagents may not write report files). The report's content is returned in the builder's structured result instead. Final profile: `profile-pass-3.json`.
