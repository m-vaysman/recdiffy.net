# Report, pair p05

**Final profile:** `profile-pass-3.json`

| Pass | Result |
|---|---|
| 1 (discovery, raw text) | matched 0 of 382 blocks; leftOnly 1002, rightOnly 382 |
| 2 | matched 382/382 (100%), break 382 (Quantity only, ratio 0.01) |
| 3 (final) | matched 382/382, exact 382, break 0, no unmatched, no quarantine, no diagnostics |
| 4 (control) | matched 382/382 exact on economics-only key; BlockKey exact everywhere |

## What the profile says

- Left is 1002 allocations, right is 382 blocks. Left rows are packaged by block (Sum quantity and amount; instrument, side and trade date must be the same across allocations), then matched one-to-one to the right block.
- Block ref: `BLK-YYYY-NNNN` on the left equals `N/YYYY` (no zero padding) on the right.
- Side: `B/S` and `buy/sell` (closed code sets, unknown codes rejected).
- Right quantity is lots x 100; right value is in thousands (x 1000), compared to the cent.
- Left trade date is written two ways (`dd/MM/yyyy`, `yyyy-MM-dd`); right is `yyyyMMdd`.
- No tolerances anywhere. Fund exists only on the left, so it is not compared.

## Confidence: high

Evidence:
- Every field is compared exactly, with no tolerance, so nothing can be absorbed.
- Amounts sum to the cent across up to many allocations per block on all 382 blocks; that is not coincidental.
- The control run (pass 4) matched on instrument, side, date, quantity and amount alone and paired the same rows, with the normalized block key equal on all 382. Two independent keys agree.
- `passthrough: false` and `required.present` mean any unexpected code, block format or blank would quarantine; none did. Mixed left date formats could hide a day/month swap, but a misread date would break against the right date or the package `Same` check; none did.

Ready for human approval.

## Engine gaps

None blocking. Minor: a lot-size conversion is expressible only as a fixed `decimal.scale` factor; per-instrument lot sizes would need a reference-data rule (closing rule: a `decimal.scaleBy` rule that reads the factor from a field or reference data). Not needed for this pair.
