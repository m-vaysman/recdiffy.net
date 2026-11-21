# Builder log, pair p01

1. `git fetch` (no checkout).
2. Read `experiments/blind-profile-r2/BUILDER-RULES.md`.
3. Read `experiments/blind-profile-r2/_site/llms.txt`.
4. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers pairs/p01/left.csv pairs/p01/right.csv`
   - left: Trade ID | Account | ISIN | Buy/Sell | Quantity | Price | Currency | Trade Date | Net Amount | Desk | Comment
   - right: Confirm Ref | Dealt On | Dir | Qty | Px | Total | CCY | ISIN | Venue | Status
5. Read `_site/docs/profiles/llms.txt`, `_site/docs/profiles/{normalization,fields,diff}/llms.txt`, `_site/docs/reference/readme/reconcile-two-csv-files-with-a-profile.md`, `_site/docs/reference/profiles/{the-shape-of-a-profile,core-rules,trading-rules,fields-the-canonical-schema,passes,diff}.md`.
6. Profile change: wrote `profile-pass-1.json`, a probe. Every column read raw as a Symbol via `symbol.map` (no map), keyed by ISIN only, to see how each side writes values.
7. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run builder/p01/profile-pass-1.json pairs/p01/left.csv pairs/p01/right.csv`
   - summary matched=34 exact=0 withinTolerance=0 break=34 leftOnly=0 rightOnly=0 ambiguous=255 searchIncomplete=0 quarantined=0 suppressed=0
   - Observed: right numbers are de-DE (`24.819,40`, `636,3948`, `1.625`), right dates `dd.MM.yyyy`, left dates `yyyy-MM-dd`, left side `B/S` vs right `BUY/SELL`. Currency never broke. Ambiguous examples show right row keys (Confirm Ref) with the same `T1xxxxx` values as left Trade IDs.
8. Read `_site/docs/reference/profiles/sources-row-keys-and-reading.md`.
9. Profile change: wrote `profile-pass-2.json`. Left culture en-US, right de-DE, both strict dates (`yyyy-MM-dd` left, `dd.MM.yyyy` right). Side mapped over the closed code vocabulary (B/S -> BUY/SELL, right BUY/SELL kept, passthrough false). Decimals parsed (Qty scale 0, Price 4, Net 2). Matched one-to-one on `TradeRef` (Trade ID / Confirm Ref, same values seen in pass 1). Two probe fields compare Comment vs Status and Desk vs Venue to see their values.
10. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run builder/p01/profile-pass-2.json pairs/p01/left.csv pairs/p01/right.csv`
   - summary matched=1000 exact=0 withinTolerance=0 break=1000 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - Only the two probe fields broke. Every real field (Isin, Side, Qty, Price, Ccy, TradeDate, Net) agreed exactly on all 1000. Probes: Desk (EQ-LDN, PB-2) vs Venue (XLON, OTC) are different concepts; Comment is free text (462 blank) vs Status (CONFIRMED seen).
11. Profile change: wrote `profile-pass-3.json`. Dropped the probes. Raised scales well past the data (Qty 6, Price 10, Net 6) so rounding cannot hide a difference. Right `Status` read into a Lifecycle symbol with a closed map {CONFIRMED} and passthrough false, so any other status would quarantine its row. `required.present` on every compared field and the ref on both sides, so a blank cell cannot slip through.
12. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run builder/p01/profile-pass-3.json pairs/p01/left.csv pairs/p01/right.csv`
   - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
13. Profile change: wrote `profile-pass-4.json`, a cross-check. Renamed the ref field to `Ref` (so the `TradeRef` veto does not apply) and matched on the natural key Isin+Side+TradeDate+Qty+Price instead of the ref, to test the ref pairing independently.
14. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run builder/p01/profile-pass-4.json pairs/p01/left.csv pairs/p01/right.csv`
   - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
15. Profile change: wrote `profile-pass-5.json`, same as pass 4 with `Ref` made a Key so it is compared on the natural-key pairs.
16. `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll run builder/p01/profile-pass-5.json pairs/p01/left.csv pairs/p01/right.csv`
   - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
   - The natural-key pairing gives the same Trade ID = Confirm Ref on all 1000 pairs. Final profile: `profile-pass-3.json`.
17. Wrote `report.md`.
