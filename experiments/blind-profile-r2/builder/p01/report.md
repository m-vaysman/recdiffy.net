# Report, pair p01

**Final profile:** `profile-pass-3.json`

| Pass | File | Match rate | Summary |
|---|---|---|---|
| 1 | profile-pass-1.json | 34 of 1000 left (3.4%), 255 ambiguous | Raw probe keyed on ISIN only |
| 2 | profile-pass-2.json | 1000/1000 matched, 0 exact (only probe fields broke) | Cultures, dates, side map, ref match |
| 3 | profile-pass-3.json | 1000/1000 exact | Final |
| 4 | profile-pass-4.json | 1000/1000 exact | Cross-check: natural key, no ref |
| 5 | profile-pass-5.json | 1000/1000 exact | Cross-check: ref compared on natural-key pairs |

## Mapping

- Trade ID = Confirm Ref (row key and `TradeRef` match key).
- Left en-US, right de-DE (`1.205.611,55`); dates `yyyy-MM-dd` vs `dd.MM.yyyy`, strict.
- Buy/Sell `B/S` = Dir `BUY/SELL` (closed code map, passthrough false).
- ISIN, Quantity/Qty, Price/Px, Currency/CCY, Net Amount/Total compared exactly.
- Not compared: Account, Desk, Comment (left only); Venue (right only). Desk and Venue are different concepts (EQ-LDN vs XLON). Status is CONFIRMED on every row, enforced by a closed map that would quarantine anything else.

## Confidence: high

- 1000 of 1000 one-to-one, every compared field exact, no tolerances anywhere, no quarantines, no ambiguity.
- Scales are set far past the data (Qty 6, Price 10, Net 6), so rounding cannot hide a difference.
- `required.present` on every compared field, so no blank cell is silently matched.
- The ref pairing is confirmed independently: matching on ISIN+Side+Date+Qty+Price without the ref gives the same 1000 pairs, with the ref equal on each (pass 5). The match is not a coincidence of one key.

Ready for human approval.

## Engine gaps

None found for this pair.
