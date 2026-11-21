# p01 discovery log

Headers and stats were identical on both sides: 480 rows, columns `Account`, `Ticker`, `AsOf`, `Quantity`, `MarketValue`. No column is unique (Account has 12 values, Ticker 10, AsOf 4, and 12×10×4 = 480), so the row key was left as the row ordinal. The seeded samples at the same indexes differed, which looks like a row shuffle of the same grid.

## Pass 1 — keep

**Saw:** peek only. Same value sets and ranges on every column. Dates are `yyyy-MM-dd`. Decimals use a dot and two places on `MarketValue`. Quantities are whole numbers.

**Hypothesis:** each holding is one `(Account, Ticker, AsOf)` and the two files are the same grid in a different order. Quantity and market value should compare exactly.

**Edit:** first profile. Symbol-map `Account` and `Ticker`, parse `AsOf` as `yyyy-MM-dd`, parse `Quantity` at scale 0 and `MarketValue` at scale 2, `match.naturalKey` on `Account` + `Ticker` + `AsOf`, exact diff. Culture `en-US`.

**Result:** matched=480 exact=480 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0. composition oneToOne=480. No field breaks.

**Decision:** keep. Full match. Stop.
