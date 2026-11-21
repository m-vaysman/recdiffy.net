# Builder log, pair p09

1. `git fetch` (no checkout). Read `experiments/blind-profile-r2/BUILDER-RULES.md`. Created workspace `builder/p09/`.
2. Read `experiments/blind-profile-r2/_site/llms.txt`.
3. Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll headers pairs/p09/left.csv pairs/p09/right.csv`
   - left: Acct | ISIN | Asset Class | Side | Qty | Price | Accrued | Trade Date | Settle Date | Desk | Trader
   - right: Security ID | Type | Dir | Face/Shares | Clean Px | Accrued Int | TD | SD | Cpty
4. Read `_site/docs/profiles/llms.txt`, `_site/docs/reference/profiles/the-shape-of-a-profile.md`, `_site/docs/reference/readme/reconcile-two-csv-files-with-a-profile.md`, `_site/docs/reference/profiles/core-rules.md`, `fields-the-canonical-schema.md`, `sources-row-keys-and-reading.md`, `trading-rules.md`, `diff.md` (all under `_site/docs/reference/profiles/` unless noted).
5. Read harness source `experiments/blind-profile-r2/harness/Program.cs` and `harness/Reports.cs` (to learn what `run` prints: canonical example rows show every declared field).
6. Profile change: wrote `builder/p09/profile-pass-1.json`, a probe. Key = Instrument (symbol.map of ISIN / Security ID); every other column copied as a Text-role field so the engine shows how each side writes it.
7. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p09/profile-pass-1.json pairs/p09/left.csv pairs/p09/right.csv`
   - `summary matched=128 exact=128 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=296 searchIncomplete=0 quarantined=0 suppressed=0`
   - Learned from engine-shown example rows: ISIN = Security ID (same text). Left is en-US (`5,925`, `2,730,000`, `50,006.24`), right is de-DE (`5.925`, `2.730.000`, `50.006,24`, `21,0841`). Asset class EQ/BD vs Equity/Fixed Income. Side B vs BUY. Left price decimal; right bond price in 32nds (`114-25` = 114.78125 vs left `114.781250`). Dates: left yyyy-MM-dd both; right TD yyyyMMdd, SD dd-MM-yy. Accrued blank for equities. Acct/Desk/Trader left only; Cpty right only. Instrument alone is not unique (ambiguous groups).
8. Read `_site/docs/reference/profiles/conditions.md` and `normalization-from-raw-cells-to-fields.md`.
9. Profile change: wrote `builder/p09/profile-pass-2.json`. Side cultures left en-US, right de-DE. Closed code maps with passthrough false: AssetClass (EQ/BD, Equity/Fixed Income -> Equity/Bond), Side (B/S, BUY/SELL -> BUY/SELL). Right Price: `price.from32nds` when AssetClass = Bond, `decimal.parse` otherwise. Dates: left yyyy-MM-dd, right TD yyyyMMdd and SD dd-MM-yy. Quantity scale 4, Price scale 6, Accrued scale 2, all compared exactly (no tolerances). `required.present` on Instrument, Quantity, Price, TradeDate, SettleDate. Passes: naturalKey [Instrument, Side, TradeDate, Quantity], then [Instrument, Side, TradeDate]. Acct/Desk/Trader/Cpty left out: no counterpart column on the other side.
10. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p09/profile-pass-2.json pairs/p09/left.csv pairs/p09/right.csv`
    - `summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0`
    - No diagnostics (no UnknownSymbol, PriceParse, DecimalParse, DateParse).
11. Profile change: wrote `builder/p09/profile-pass-3.json`, a robustness check only (not a candidate). Same as pass 2 but one pass with key [Instrument, TradeDate], so Side, AssetClass, Quantity, Price, Accrued and SettleDate are all compared rather than keyed.
12. Harness: `dotnet _bin/BlindDiscovery.Harness.dll run builder/p09/profile-pass-3.json pairs/p09/left.csv pairs/p09/right.csv`
    - `summary matched=976 exact=976 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=12 searchIncomplete=0 quarantined=0 suppressed=0`
    - All 976 pairs found without Side/Quantity in the key compare exact on every field. The 12 ambiguous groups are two trades in one instrument on one date; pass 2's full key separates them.
13. Decision: final profile is `profile-pass-2.json`. Wrote `builder/p09/report.md`.
