# p06 discovery log

Both files have 900 rows. Shared headers: `PositionId`, `Account`, `Ticker`, `Quantity`, `MarketValue`. The right file also has `BookView` with values `COUNTERPARTY` and `INTERNAL`. `PositionId` is unique on each side, shape `P-000001`. Account and ticker value sets match. Quantity and market value are signed, two decimals, same ranges (`-119..119` and `-2915.5..2915.5`) and the same distinct counts.

## Pass 1 — keep

**Saw:** peek only. The extra `BookView` column is the only structural difference. Signs exist on both sides, so this is not an absolute-value rewrite.

**Hypothesis:** `PositionId` joins the rows. Compare the shared columns exactly and leave `BookView` out until a break says it matters.

**Edit:** first profile. `match.sharedRef` on `PositionId`. Account and ticker as symbols. Quantity and market value at scale 2. Exact diff. Culture `en-US`.

**Result:** matched=900 exact=540 break=360 leftOnly=0 rightOnly=0. composition oneToOne=900.

- `Quantity` break=360, nullVsValue=0, sameSign=0.00, ratio=-1@1.00, delta[min=-232 p50=-202 p90=230 p99=232 max=232]
- `MarketValue` break=360, nullVsValue=0, sameSign=0.00, ratio=-1@1.00, delta[min=-5336 p50=-4141 p90=5175 p99=5336 max=5336]

Account and ticker did not break.

**Decision:** keep. The id join is right. On exactly 360 rows both economic fields are the negation of the left value (`sameSign` 0 and ratio −1 on every break). That is a perspective flip, not a tolerance.

## Pass 2 — keep

**Saw:** 360 rows with quantity and market value both negated. `BookView` is the only column that can say which view a right-hand row is in. `cash.toPerspective` negates one decimal field when a named column equals `invertWhen`.

**Hypothesis:** `COUNTERPARTY` is the external view, so those quantities should be negated back to the internal sign. `INTERNAL` rows already matched and should be left alone. One field per pass.

**Edit:** one change from pass 1. On the right, derive `cash.toPerspective` for `Quantity` with `perspective` `BookView` and `invertWhen` `COUNTERPARTY`.

**Result:** matched=900 exact=540 break=360. `Quantity` dropped out of the field list. `MarketValue` is unchanged: break=360, ratio=-1@1.00, same deltas.

**Decision:** keep. The summary counts did not move, because those 360 rows still break on market value. The field digest did: quantity is now exact, and the rows that already matched stayed matched, so `COUNTERPARTY` was the right flag. Reverting would put the quantity breaks back.

## Pass 3 — keep

**Saw:** market value is still negated on the same 360 rows. Quantity is clean.

**Hypothesis:** the counterparty view flips both economic amounts. The same rule on `MarketValue` should clear the remaining breaks.

**Edit:** one change from pass 2. A second right-hand `cash.toPerspective` on `MarketValue`, same perspective and `invertWhen`.

**Result:** matched=900 exact=900 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0. composition oneToOne=900. No field breaks.

**Decision:** keep. Full match. Stop.
