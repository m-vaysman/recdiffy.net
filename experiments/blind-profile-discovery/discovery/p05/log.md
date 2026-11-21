# p05 discovery log

Both files have 1200 rows. The headers differ and the right file is in a different column order.

| Left | Right | Shared value set |
|---|---|---|
| `TradeId` | `ref` | `T-000001` shape, 1200 distinct |
| `Book` | `desk` | `ACC-01` … `ACC-12` |
| `Ticker` | `sym` | the same 15 tickers |
| `TradeDate` | `td` | the same 12 dates, `yyyy-MM-dd` |
| `Side` | `bs` | `BUY`, `SELL` |
| `Quantity` | `qty` | integers 5..19 |
| `Price` | `px` | the same 9 prices, 12.00..14.00 |
| `NetCash` | `cash` | the same range 60..266, 45 distinct |

## Pass 1 — keep

**Saw:** peek only. Same domains under different names. Samples at the same indexes are different trades, so row order also differs.

**Hypothesis:** this is a rename. Joining `TradeId` to `ref` and comparing the renamed columns exactly should match every row.

**Edit:** first profile. Each side's `from` column writes the same canonical field. `match.sharedRef` on `TradeId`. Quantity scale 0, price and net cash scale 2, date format `yyyy-MM-dd`. Exact diff. Culture `en-US`.

**Result:** matched=1200 exact=1200 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0. composition oneToOne=1200. No field breaks.

**Decision:** keep. Full match. Stop.
