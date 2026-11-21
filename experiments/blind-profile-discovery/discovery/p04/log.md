# p04 discovery log

Both files have 700 rows and the same headers: `TradeId`, `Ticker`, `Quantity`, `Price`, `NetCash`. `TradeId` is unique on each side and uses the same `R-000001` shape. Ticker and quantity have the same value sets. Price does not: left is four decimal places (`8.0000`, `9.0025`, `10.0050`, up to `27.0075`), right is two (`12.01`, `26.01`, up to `27.01`). Net cash spans the same range on both sides. Samples at the same indexes have different trade ids, so the files are ordered differently.

## Pass 1 — keep

**Saw:** peek only.

**Hypothesis:** `TradeId` is a shared reference. Price is the field written differently; keep four decimal places on the first run so the digest shows the raw delta instead of a rounded one. Net cash is money at two decimals.

**Edit:** first profile. `match.sharedRef` on `TradeId`. Ticker as a symbol, quantity at scale 0, price at scale 4, net cash at scale 2. Exact diff. Culture `en-US`. Default midpoint `ToEven`.

**Result:** matched=700 exact=180 withinTolerance=0 break=520 leftOnly=0 rightOnly=0. composition oneToOne=700. Only `Price` broke: nullVsValue=0, sameSign=1.00, delta[min=-0.0025 p50=0.0025 p90=0.005 p99=0.005 max=0.005], ratio=mixed. Ticker, quantity and net cash were exact.

**Decision:** keep. The id join is right. The price deltas are the four quarter-cent endings: `0` matches, `.0025` sits 0.0025 under the cent, `.0075` sits 0.0025 over, `.0050` sits 0.005 under the next cent. There is no delta of `-0.005`, so a half-cent is rounded away from zero, not to even.

## Pass 2 — keep

**Saw:** 520 price breaks, every delta inside half a cent, and no other field involved.

**Hypothesis:** the right price is the left price rounded to cents. Parsing both at scale 2 should clear the non-halfway cases. The default `ToEven` midpoint will still miss the halfway cases whose cent digit is even.

**Edit:** one change from pass 1. `Price` scale `4` → `2`.

**Result:** matched=700 exact=600 break=100. `Price` break=100, sameSign=1.00, delta[min=0.01 p50=0.01 p90=0.01 p99=0.01 max=0.01], ratio=mixed. Everything else still exact.

**Decision:** keep. Exact rows went from 180 to 600. The remaining 100 breaks are a constant `+0.01`: banker's rounding took those halfway prices down, and the right file took them up.

## Pass 3 — keep

**Saw:** 100 price breaks, each exactly one cent high on the right, after rounding to scale 2 with `ToEven`.

**Hypothesis:** the right file rounds half a cent away from zero. Setting that midpoint makes the scale-2 price equal on both sides. This is the representation, not a tolerance.

**Edit:** one change from pass 2. `"rounding": { "midpoint": "AwayFromZero" }`.

**Result:** matched=700 exact=700 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0. composition oneToOne=700. No field breaks.

**Decision:** keep. Full match. Stop.
