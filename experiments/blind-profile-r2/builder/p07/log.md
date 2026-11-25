# Builder log, pair p07

1. `git fetch` (no checkout).
2. Read `experiments/blind-profile-r2/BUILDER-RULES.md`.
3. Read `experiments/blind-profile-r2/_site/llms.txt`.
4. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers pairs/p07/left.csv pairs/p07/right.csv`
   - left: Deal | Pkg Link | Leg | Product | Notional | Leg Amount | Maturity | Party
   - right: Trade Ref | Product Type | Notional | Net Cash | Maturity Date
5. Read `_site/docs/profiles/llms.txt`.
6. Read `_site/docs/reference/profiles/the-shape-of-a-profile.md`, `_site/docs/reference/readme/reconcile-two-csv-files-with-a-profile.md`, `_site/docs/profiles/consolidation/llms.txt`.
7. Read `_site/docs/reference/profiles/packages.md`, `_site/docs/reference/profiles/consolidation-lifecycles-and-packages.md`.
8. Read `_site/docs/profiles/normalization/llms.txt`, `_site/docs/profiles/fields/llms.txt`.
9. Read `_site/docs/reference/profiles/core-rules.md`, `_site/docs/reference/profiles/fields-the-canonical-schema.md`.
10. Profile change: wrote `profile-pass-1.json`, a probe. Every column copied to Text fields, left rowKey `Deal`, right rowKey `Trade Ref`, a constant probe key per side so nothing matches and the harness prints canonical example rows.
11. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p07/profile-pass-1.json pairs/p07/left.csv pairs/p07/right.csv`
    - summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=560 rightOnly=780 ambiguous=0 searchIncomplete=0 quarantined=440 suppressed=0 (DuplicateConflict=440)
    - Learned: left is en-US (`2,125,000`, `yyyy-MM-dd`), right is de-DE (`3.253.000`, `-1.095,01`, `dd.MM.yyyy`); right refs are lower case (`tr-...`); `Deal` repeats on left for 440 rows (SW- deals), so it can't be the left row key.
12. Read `_site/docs/reference/profiles/sources-row-keys-and-reading.md`, `_site/docs/reference/profiles/conditions.md`.
13. Profile change: `profile-pass-2.json` = pass 1 with no left rowKey (positional) and the left probe key set to `SWAPLEG` only for `SW...` deals, to show a swap leg row.
14. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p07/profile-pass-2.json pairs/p07/left.csv pairs/p07/right.csv`
    - summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=1000 rightOnly=780 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
    - Learned: swap legs carry `Pkg Link` (PKG-....) and `Leg` (R seen), product XCCY. 1000 left rows vs 780 right: 560 singles + 440 legs = 220 packages.
15. Profile change: `profile-pass-3.json`, first real profile. TradeRef (Symbol, Key) from `Deal` / `Trade Ref` upper-cased with text.regex; Product (Symbol, Key); Notional Decimal scale 0; NetCash Decimal scale 2 from `Leg Amount` / `Net Cash`; Maturity Date with `yyyy-MM-dd` left, `dd.MM.yyyy` right, strict; Party Identity (left only, not compared); left package link PkgLink, role Leg, NetCash = Sum. Right culture de-DE. One naturalKey pass on TradeRef.
16. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p07/profile-pass-3.json pairs/p07/left.csv pairs/p07/right.csv`
    - summary matched=780 exact=560 withinTolerance=0 break=220 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
    - All 220 packages break on NetCash only, delta always negative: net cash is not the sum of the legs.
17. Profile change: `profile-pass-4.json`: NetCash = `DifferenceByRole(R,P)`, requiredRoles [R, P] (tests the guess that the other role is P and the right side nets the legs).
18. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p07/profile-pass-4.json pairs/p07/left.csv pairs/p07/right.csv`
    - summary matched=780 exact=560 withinTolerance=0 break=220 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
    - No IncompletePackage, so every package has an R and a P leg. NetCash ratio=-1 at 1.00: the sign is reversed.
19. Profile change: `profile-pass-5.json`: NetCash = `DifferenceByRole(P,R)`.
20. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p07/profile-pass-5.json pairs/p07/left.csv pairs/p07/right.csv`
    - summary matched=780 exact=780 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
21. Profile change: `profile-pass-6.json` = pass 5 with Notional and NetCash at scale 6, a check that rounding to scale 0/2 hides no difference.
22. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p07/profile-pass-6.json pairs/p07/left.csv pairs/p07/right.csv`
    - summary matched=780 exact=780 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
23. Tried to write `report.md`; the agent tool refused report files for subagents, so the report content is returned in the structured result instead. Final profile: `profile-pass-5.json`.
