# Builder log, pair p06

1. `git fetch` (no checkout).
2. Read `experiments/blind-profile-r2/BUILDER-RULES.md`.
3. Created workspace `experiments/blind-profile-r2/builder/p06/`.
4. Read `experiments/blind-profile-r2/_site/llms.txt`.
5. Read `experiments/blind-profile-r2/_site/docs/profiles/llms.txt`.
6. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers pairs/p06/left.csv pairs/p06/right.csv`
   - left: Our Ref | ISIN | Side | Qty | Price | Trade Date | Net
   - right: Cpty Ref | Security | Action | Size | Px | Trade Dt | Proceeds
7. Read `_site/docs/reference/readme/reconcile-two-csv-files-with-a-profile.md` and `_site/docs/reference/profiles/the-shape-of-a-profile.md`.
8. Read `_site/docs/profiles/normalization/llms.txt` and `_site/docs/profiles/fields/llms.txt`.
9. Read `_site/docs/reference/profiles/core-rules.md` and `_site/docs/reference/profiles/trading-rules.md`.
10. Read harness source `experiments/blind-profile-r2/harness/` (grep for registry and CSV options in Program.cs/Reports.cs): trading pack registered, comma CSV with header.
11. Profile change: wrote `builder/p06/profile-pass-1.json`. Key Isin(Identifier)+Side(symbol.map passthrough)+TradeDate(yyyy-MM-dd guess); Economic Quantity/Price/Net via decimal.parse; en-US; rowKeys Our Ref / Cpty Ref. Why: first guess from headers.
12. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p06/profile-pass-1.json pairs/p06/left.csv pairs/p06/right.csv`
    - summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=2000 suppressed=0
    - diagnostics DateParse=1656 DecimalParse=1408 MissingKey=344. Formats guessed wrong; quarantine shows no values.
13. Read harness source `experiments/blind-profile-r2/harness/Program.cs` and `Reports.cs` in full, to learn what the digest prints (canonical values only for matched diffs and unmatched examples).
14. Profile change: wrote `builder/p06/profile-pass-2.json`, a probe. Every column copied raw as Symbol (symbol.map, no map), key Isin only, others Economic, so the digest shows raw values side by side. Why: pass 1 quarantine showed no values.
15. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p06/profile-pass-2.json pairs/p06/left.csv pairs/p06/right.csv`
    - summary matched=70 exact=0 withinTolerance=0 break=70 leftOnly=62 rightOnly=0 ambiguous=236 searchIncomplete=0 quarantined=344 suppressed=0
    - Learned: left numbers en-US (1,506,751.29), right de-DE style (2.492.989,44; 129,8432); left date `dd MMM yyyy`, right `yyyy.MM.dd`; left side BUY/SELL, right B/S; row keys TR-100184 (left) vs 100184 (right) appear in the same ambiguous groups, so Cpty Ref looks like Our Ref without the `TR-` prefix; 344 rows have blank ISIN/Security (MissingKey).
16. Read `_site/docs/reference/profiles/sources-row-keys-and-reading.md`: a blank row key quarantines with MissingKey, so the 344 MissingKey rows are rows with blank Our Ref / Cpty Ref, not blank ISINs.
17. Read `_site/docs/profiles/passes/llms.txt`, `_site/docs/reference/profiles/passes.md` (null key leaves a row free for later passes; TradeRef veto, missing never vetoes) and `_site/docs/reference/profiles/diff.md`.
18. Profile change: wrote `builder/p06/profile-pass-3.json`. Dropped rowKey (blank refs); side cultures left en-US, right de-DE; dates `dd MMM yyyy` / `yyyy.MM.dd`, strict; Side map BUY/SELL and B->BUY, S->SELL with passthrough false (closed code vocabulary); field TradeRef (Symbol, Identity) from Our Ref with `^TR-` stripped by text.regex and from Cpty Ref as is; pass `ref` naturalKey [TradeRef], then pass `natural` reducedKey [Isin, Side, TradeDate, Quantity] (TradeRef veto applies). Quantity scale 0, Price 4, Net 2, all exact.
19. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p06/profile-pass-3.json pairs/p06/left.csv pairs/p06/right.csv`
    - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
20. Profile change: wrote `builder/p06/profile-pass-4.json` (control). Removed the ref pass and renamed TradeRef to OurRef so no veto; natural key only. Why: check how much the natural key does alone.
21. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p06/profile-pass-4.json pairs/p06/left.csv pairs/p06/right.csv`
    - summary matched=832 exact=832 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=75 searchIncomplete=0 quarantined=0 suppressed=0
    - Ambiguous groups are rows with identical economics on a side; the examples show the same OurRef on both sides, so the ref is what separates them.
22. Profile change: wrote `builder/p06/profile-pass-5.json` (control). Ref pass only. Why: count rows that need the fallback.
23. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p06/profile-pass-5.json pairs/p06/left.csv pairs/p06/right.csv`
    - summary matched=704 exact=704 withinTolerance=0 break=0 leftOnly=296 rightOnly=296 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
    - 296 pairs have a blank ref on at least one side; the fallback in pass 3 matched all of them exactly with no veto conflict.
24. Profile change: wrote `builder/p06/profile-pass-6.json`: pass 3 with scales raised (Quantity 6, Price 10, Net 8). Why: make sure rounding to a coarse scale hides no difference.
25. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p06/profile-pass-6.json pairs/p06/left.csv pairs/p06/right.csv`
    - summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
26. Wrote `builder/p06/report.md`. Final profile: profile-pass-6.json.
