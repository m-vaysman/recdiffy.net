# p03 discovery log

Both files have 640 rows and the same headers: `TradeId`, `Book`, `Ticker`, `TradeDate`, `Side`, `Quantity`, `Price`. Book, ticker, date, side, quantity and price have the same value sets. `TradeId` does not: left is `TRD-000001` (length 10, zero-padded), right is `trd119` (length 4–6, no padding, lower case). Seeded samples at the same indexes are different trades.

The same book, ticker, date, side, quantity and price show up on several right-hand sample rows with different trade ids (`trd67`, `trd167`, `trd627`), so the economic columns are not a unique key.

## Pass 1 — keep

**Saw:** peek only. 640 distinct ids on each side. Left shape is `TRD-` plus six digits. Right shape is `trd` plus the integer. Other columns look directly comparable (`yyyy-MM-dd`, `BUY`/`SELL`, integer quantity, price at two decimals).

**Hypothesis:** the trade id is the same integer written two ways. Mapping both spellings onto that integer should pair the rows, and the other fields should then compare exactly.

**Edit:** first profile. `symbol.map` on the left sends `TRD-000001`…`TRD-000640` to `1`…`640`. The right map sends `trd1`…`trd640` to the same tokens. `passthrough` is false, so an id outside that range would quarantine. `match.sharedRef` on the canonical id. Book, ticker, side, date, quantity (scale 0) and price (scale 2) are compared exactly. Culture `en-US`. `rowKey` stays the raw `TradeId`.

**Result:** matched=640 exact=640 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0. composition oneToOne=640. No field breaks. Nothing quarantined, so every id was inside the inferred 1..640 range.

**Decision:** keep. Full match. Stop.
