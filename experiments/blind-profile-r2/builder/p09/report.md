# Report, pair p09

**Final profile:** `profile-pass-2.json`

## Match rate per pass

| Pass | Summary | Match rate |
|---|---|---|
| 1 (probe, key Instrument only) | matched=128 exact=128 ambiguous=296 | 128 of 1000 |
| 2 (final) | matched=1000 exact=1000 break=0 leftOnly=0 rightOnly=0 ambiguous=0 quarantined=0 | 1000 of 1000, all exact |
| 3 (check, key Instrument+TradeDate) | matched=976 exact=976 ambiguous=12 | 976 one-to-one, all exact |

## Confidence: high

Evidence:
- Every row on both sides matches one-to-one, with no orphans, no quarantine and no diagnostics. Every compared field (Instrument, AssetClass, Side, Quantity, Price, Accrued, TradeDate, SettleDate) is exact: no tolerances are set, so nothing is hidden by a tolerance.
- The code maps use `passthrough: false`, so an unexpected asset or side code would have raised `UnknownSymbol`; none did.
- Pass 3 keys only on Instrument and TradeDate. The 976 pairs it finds still agree exactly on Side, Quantity, Price (including every 32nds bond price), Accrued and SettleDate. The match therefore does not depend on putting those fields in the key, so it is not coincidental.
- Right SettleDate is read as dd-MM-yy. Read as MM-dd-yy, the many rows with a day above 12 would fail or break; all 1000 compare exactly to the left's ISO dates.

Ready for human approval.

## Open items (not blocking)

- Acct, Desk and Trader exist only on the left, and Cpty only on the right. They are not reconciled. A human should say whether a counterparty-to-account mapping is expected.
- No row key is declared on either side; rows are identified by position.

## Engine gaps

None found for this pair.
