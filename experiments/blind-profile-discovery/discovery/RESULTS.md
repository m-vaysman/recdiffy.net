# Blind profile discovery results

Eight pairs, harness only (`peek`, `validate`, `run`). No answer key was used. A pass is kept when the field digest improves, even if the summary exact count stays put because another field on the same rows is still broken. Nothing was reverted: every edit improved the digest. No tolerance was widened.

`matched %` is `exact / matched` after the last kept pass. For p08 the matched count is order groups, not fills.

| Pair | Passes | Final matched % | Stopped because |
|---|---:|---:|---|
| p01 | 1 | 100% (480/480 exact) | Full match. Composite key `Account` + `Ticker` + `AsOf`. |
| p02 | 1 | 100% (800/800 exact) | Full match. Shared `EntryId`. |
| p03 | 1 | 100% (640/640 exact) | Full match, after canonicalizing `TRD-000001` and `trd1` with `symbol.map`. |
| p04 | 3 | 100% (700/700 exact) | Full match. Price compared at scale 2 with midpoint `AwayFromZero`. |
| p05 | 1 | 100% (1200/1200 exact) | Full match. Right columns are the left columns renamed and reordered. |
| p06 | 3 | 100% (900/900 exact) | Full match. `BookView=COUNTERPARTY` negates quantity and market value. |
| p07 | 3 | 100% (1500/1500 exact) | Full match only as symbols. Right quantity and market value are the left numbers divided by 1000. No rule scales a decimal. |
| p08 | 1 | 100% (300/300 groups exact) | Full match. 1200 fills sum to 300 orders (`manyToOne=300`, nothing left over). |

## How the profiles converged

The first profile came from the peek, before any run.

- Same headers, a unique id, and the same value sets meant a shared-reference join and an exact diff (p02, and the join step of p04, p06, p07).
- Same headers and no unique column, with `12 × 10 × 4 = 480`, meant a composite natural key and row-ordinal identity (p01).
- Different headers with the same domains meant a rename. The `from` column changes, the canonical field does not (p05).
- An id that is unique on both sides but spelled differently (`TRD-000001` vs `trd67`) cannot be the raw match key. The economic columns were also not unique, so the id had to be canonicalized (p03).
- 1200 rows against 300, with `FillId` carrying the order id and the right quantity sitting well above the left range, meant `match.groupSum` with the fills on the `many` side (p08).

Later passes were one digest fact each.

- p04 pass 1 kept four decimal places on purpose. Price deltas were only `-0.0025`, `+0.0025` and `+0.005`, with no `-0.005`, so a half cent was being rounded away from zero. Scale 2 cleared 420 of the 520 breaks. The last 100 were a flat `+0.01`, which is `ToEven` versus `AwayFromZero`. The midpoint change cleared them. Net cash had already matched at scale 2, so it was never the price.
- p06 pass 1 showed ratio `-1` at 100% on both quantity and market value, same sign rate 0, 360 of 900 rows. `BookView` was the only extra column. Negating quantity when it said `COUNTERPARTY` removed quantity from the field list and left the other 540 rows exact, so the flag was right. The summary counts did not move. The same rule on market value then went to exact.
- p07 pass 1 showed ratio `0.001` at 100% on quantity and market value, and price did not break. The quantity domain was closed (40 values, 100 through 4000, step 100), so pass 2 mapped both spellings. Market value was the cross product of that quantity domain and the 15 listed prices. Pass 3 mapped those products. Both fields had to become symbols.

## What the harness made easy or hard

Easy:

- Full-file value sets, ranges, and distinct counts were enough to see a shuffle, a rename, a closed id pattern, and a unit change without lining rows up by eye.
- A constant `ratio=` on the field line was unambiguous for a sign flip and for a factor of 1000. Delta quantiles were enough to separate half-up rounding from banker's rounding.
- `composition manyToOne=300` with `leftOnly=0` confirmed that every fill was consumed.
- Header check failed closed before a bad run.

Hard:

- The summary exact count stays flat when one of two broken fields is fixed. The field list is the score. p06 pass 2 and p07 pass 2 would have looked like no improvement from the summary alone.
- Distinct values are listed only when there are 16 or fewer. A 40-value quantity had to be inferred from the range, the count, and a few examples. A wrong step would have quarantined the file, which is a loud failure, but the harness does not show the unexpected spelling, only a quarantine count and three examples.
- Samples are the same row indexes, not the same keys, so one record cannot be read across the two files unless that key happens to land in both samples.
- Break lines do not include an example key or the two raw values. Once every row matches, a field break has quantiles and a ratio and nothing you can point at.
- `BookView` did not come with frequencies, so `COUNTERPARTY` versus `INTERNAL` was a guess. The field digest was the only way to tell it had been the right guess.

## Engine gaps

No pair stopped short of an exact summary. Two of those summaries are lookup tables standing in for a missing rule. A domain that peek could not close would have stopped there.

- **No unit factor.** Nothing in the core rules or the trading pack multiplies or divides a decimal by a constant. p07 is `right = left / 1000` on quantity and market value, on every row, with price already equal. A relative tolerance wide enough to accept that ratio would be about `0.999`, which hides the break rather than describing it. The exact summary treats both fields as symbols. They are no longer rounded, summed, or tolerated as decimals.
- **No string reshape.** `symbol.map` and `identifier.map` copy or look up a whole value. They do not strip a prefix, change case, or drop zeros. p03 matched only because `TRD-000001`…`TRD-000640` and `trd1`…`trd640` are a closed pattern peek had already bounded. An open id scheme would have stayed unmatched.
- **`cash.toPerspective` is one field per call.** It did express the p06 sign flip, including a perspective column that exists on only one side. It cannot scale, and a second amount needs a second call. That is a profile inconvenience, not a gap that blocked a pair.
- **`match.groupSum` ignores a group of one.** p08 did not hit this: all 300 orders grouped, so every order had between 2 and 8 fills. A mix of single fills and multi-fill orders would need a one-to-one pass beside the group pass. The engine already has that pass.

`price.dirtyToClean`, `fees.inferFromNet`, and the trading attributions were not used. They explain a price or a fee from other trade fields. They do not divide by 1000, and using one of them to force p07 equal would have been hiding the break.
