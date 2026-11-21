# p07 discovery log

Both files have 1500 rows and the same headers: `LotId`, `Account`, `Ticker`, `Quantity`, `Price`, `MarketValue`. `LotId` is unique on each side, shape `L-000001`. Account, ticker and price have the same value sets. Price is the 15 values `10.00`, `10.25`, … `13.50`. Quantity and market value do not: left quantity is 40 integers from 100 to 4000, right quantity is 40 decimals from 0.1 to 4. Left market value is 48 integers from 1000 to 49000. Right market value is 48 decimals from 1 to 49. Samples show left `Quantity * Price = MarketValue`, and the same identity on the right in the smaller units.

## Pass 1 — keep

**Saw:** peek only.

**Hypothesis:** `LotId` joins the rows. Price should already agree. Quantity and market value look like the same numbers divided by 1000 on the right. Compare them as decimals first so the digest can show the ratio. No registered rule multiplies or divides by a constant, so this pass only measures.

**Edit:** first profile. `match.sharedRef` on `LotId`. Quantity scale 1, price scale 2, market value scale 3. Exact diff. Culture `en-US`.

**Result:** matched=1500 exact=0 break=1500. composition oneToOne=1500. Price did not break.

- `Quantity` break=1500, nullVsValue=0, sameSign=1.00, ratio=0.001@1.00, delta[min=-3996 p50=-1598.4 p90=-299.7 p99=-99.9 max=-99.9]
- `MarketValue` break=1500, nullVsValue=0, sameSign=1.00, ratio=0.001@1.00, delta[min=-48951 p50=-18531.45 p90=-3896.1 p99=-999 max=-999]

**Decision:** keep. Every joined row is exactly one thousandth on both economic fields. Account, ticker and price are already the same value.

## Pass 2 — keep

**Saw:** a constant ratio of 0.001 on every quantity and every market value. The core and trading rules can round, negate, take an absolute value, or parse 32nds. None of them scales a number by a constant. A tolerance wide enough to cover a 1000× gap would hide the break.

**Hypothesis:** the quantity domain is closed and fully determined by the peek stats: 40 values, 100 through 4000, step 100, and the right spelling is that number over 1000 with trailing zeros removed (`3700` and `3.7`, `1000` and `1`, `4000` and `4`). A `symbol.map` on each side can send both spellings to the share count. One field per pass.

**Edit:** one change from pass 1. `Quantity` becomes a `Symbol` with role `Key`. The left map is `100`…`4000`. The right map is `0.1`…`4`. `passthrough` is false, so an unexpected spelling would quarantine.

**Result:** matched=1500 exact=0 break=1500 quarantined=0. `Quantity` dropped out of the field list. `MarketValue` is unchanged: break=1500, ratio=0.001@1.00.

**Decision:** keep. The summary exact count stayed 0 because market value still breaks on every row. The field digest improved, nothing quarantined, and the right-hand spellings were exactly the stripped thousandths. The quantity table is the right representation.

## Pass 3 — keep

**Saw:** market value is the same thousandths factor, and quantity now matches. Price's 15 values were listed in full. Every left market value in the samples is `Quantity * Price`, and that product is an integer because quantity is a multiple of 100.

**Hypothesis:** the market-value domain is the cross product of those 40 quantities and 15 prices. Mapping each integer product and its thousandth, with trailing zeros removed, should clear the remaining breaks. This is still a spelling table, not a scale rule. The table is larger than the 48 values that actually occur. Unused keys do not change a row.

**Edit:** one change from pass 2. `MarketValue` becomes a `Symbol`. Both sides map the 514 distinct products onto the integer product. `passthrough` stays true, so an unlisted spelling would remain a break instead of a quarantine.

**Result:** matched=1500 exact=1500 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0. composition oneToOne=1500. No field breaks.

**Decision:** keep. Full match. Stop.

The match is real: no tolerance was widened. It is also a workaround. Quantity and market value are compared as symbols, not as decimals, because the engine has no unit-factor rule. The tables were possible only because peek had already bounded both domains.
