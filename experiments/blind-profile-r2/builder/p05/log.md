# Builder log, pair p05

1. `git fetch` (from the worktree root). No output of note.
2. Read `experiments/blind-profile-r2/BUILDER-RULES.md` in full.
3. Created workspace `experiments/blind-profile-r2/builder/p05/`.
4. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers experiments/blind-profile-r2/pairs/p05/left.csv experiments/blind-profile-r2/pairs/p05/right.csv`
   - left: Alloc ID | Block Ref | Fund | ISIN | Side | Alloc Qty | Alloc Amount | Trade Date
   - right: Dealt | Block | Dir | Lots | Block Value (k) | Instr
   - First read: left looks like allocations, right like blocks (many-to-one). Right quantity is in lots and value in thousands.
5. Read `experiments/blind-profile-r2/_site/llms.txt`.
6. Read `experiments/blind-profile-r2/harness/Program.cs` and `harness/Reports.cs` (harness source, to learn what `run` prints: canonical examples show only declared fields).
7. Read `RecDiffy.NET/docs/PROFILES.md` lines 50-439 (shape, fields, rules, conditions, consolidation, paths, diff, limits).
8. Profile change: wrote `profile-pass-1.json`. Discovery profile: left row key `Alloc ID`; block reference copied to a Symbol key on both sides as-is (`Block Ref` / `Block`); every other column copied with `text.copy` into Text fields so the run's canonical examples show the raw formats. One `match.naturalKey` pass on the block key. Expected to match nothing; purpose is to see formats.
9. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p05/profile-pass-1.json pairs/p05/left.csv pairs/p05/right.csv` (from `experiments/blind-profile-r2`)
   - summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=1002 rightOnly=382 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - Seen formats: left block `BLK-2024-0004`, right block `404/2024` (number/year, no zero padding). Left side `S`; right dir `sell`/`buy`. Left qty `4,300` (comma grouping), amount `1,672,679.36`. Left trade date mixed: `25/07/2024` and `2024-07-25`. Right dealt `20240513`. Right `Lots` small integers; `Block Value (k)` like `5345.58256` (thousands, 5 dp = cents). `Instr` looks like the same ISIN form as left `ISIN`.
   - Hypothesis: 1002 allocations roll up to 382 blocks (many-to-one); left block ref normalizes to `<number without leading zeros>/<year>`; quantity = lots x lot size (unknown); amount = value x 1000.
10. Grep (engine source, allowed): `grep -n 'Leg_Structure\|RoleField\|\.Role\b' RecDiffy.NET/src/RecDiffy/Consolidation/*.cs`, to learn what the package `role` field is used for. It is read only by `requiredRoles` and `FromRole`; none are used here, so `Fund` serves as the role field.
11. Profile change: wrote `profile-pass-2.json`. Real fields: `BlockKey` (Symbol, Key), `Fund` (Symbol, Identity, left only), `Instrument` (Identifier ISIN, Economic), `Side` (Symbol, Economic), `Quantity` (Decimal scale 4), `Amount` (Decimal scale 2), `TradeDate` (Date). Culture invariant. Left: `text.regex` `^BLK-(\d{4})-0*(\d+)$` -> `$2/$1` (passthrough false); right: `^0*(\d+)/(\d{4})$` -> `$1/$2`. Side code maps `B/S` and `buy/sell` to `BUY/SELL` (closed vocabulary, passthrough false). Left dates `dd/MM/yyyy` or `yyyy-MM-dd`; right `yyyyMMdd`. Right amount `decimal.scale` factor 1000. Right `Lots` parsed raw (no factor) to let the run report the lot ratio. `required.present` on all economic fields. Left package on `BlockKey`: Sum qty and amount, Same instrument, side, date. One `match.naturalKey` pass on `BlockKey`, all fields exact.
12. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p05/profile-pass-2.json pairs/p05/left.csv pairs/p05/right.csv` -> parse fail (`InvalidJson`): the shell heredoc dropped one backslash from `\d`. Profile change: replaced `\d` with `[0-9]` in both patterns (same meaning; edited with sed then the Edit tool to remove a stray backslash). No logic change.
13. Harness: same command, re-run.
   - summary matched=382 exact=0 withinTolerance=0 break=382 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - composition oneToOne=90 manyToOne=292 package=382. No diagnostics.
   - Only `Quantity` breaks: 382, ratio=0.01@1.00 (e.g. L=4400 R=44). Instrument, Side, TradeDate and Amount all exact. Lot size is 100.
14. Profile change: `profile-pass-3.json` = pass 2 with right `Lots` read by `decimal.scale` factor 100. Why: pass 2 reported right/left = 0.01 on every block.
15. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p05/profile-pass-3.json pairs/p05/left.csv pairs/p05/right.csv`
   - summary matched=382 exact=382 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - composition oneToOne=90 manyToOne=292 package=382. No diagnostics, no field breaks, nothing unmatched.
16. Profile change: `profile-pass-4.json`, a control only (not the final profile): pass 3 with the match key changed from `BlockKey` to `Instrument, Side, TradeDate, Quantity, Amount`. Why: check that the block-reference normalization is independently right; `BlockKey` (role Key) is still compared after matching, so a wrong block mapping would show as BlockKey breaks.
17. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p05/profile-pass-4.json pairs/p05/left.csv pairs/p05/right.csv`
   - summary matched=382 exact=382 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - Same pairing by economics alone, with BlockKey exact on all 382. Final profile stays `profile-pass-3.json`.
18. Wrote `report.md`.
