# p08 discovery log

The files are not the same grain. Left has 1200 rows and a `FillId` (`F-0001-01`, unique). Right has 300 rows and no fill id. Left `OrderId` has 300 distinct values, the same count as the right file, where `OrderId` is unique. Ticker, side and price have the same value sets. Quantity and net cash do not: left quantity is 5..23, right quantity is 30..49, and right net cash starts at 150 where left starts at 25. One sampled order appears on both sides: `O-0300` is BAC, SELL, price 5.75, with a left fill of quantity 9 and a right order of quantity 49.

## Pass 1 — keep

**Saw:** peek only. 1200 / 300 = 4, so an even handful of fills per order is possible. Right quantities can be sums of the left fill quantities. Price is shared, so it is not a sum.

**Hypothesis:** each right row is an order and the left rows are its fills. Block on `OrderId`, `Ticker`, `Side` and `Price`. Conserve `Quantity` and `NetCash`. The many side is left. A group of one would not match, so a leftover one-fill order would show up as unmatched.

**Edit:** first profile. `match.groupSum` as above. Quantity scale 0, price and net cash scale 2. Exact diff. Culture `en-US`. Left row key is `FillId`. Right row key is `OrderId`. `FillId` is not a field, because the right file does not have it.

**Result:** matched=300 exact=300 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0. composition manyToOne=300. No field breaks.

**Decision:** keep. Full match. Every order grouped, so none had a single fill, and none needed more than the default group size of 8. Stop.
