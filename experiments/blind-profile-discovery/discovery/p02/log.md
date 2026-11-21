# p02 discovery log

Both files have 800 rows and the same headers: `EntryId`, `Account`, `ValueDate`, `Currency`, `Amount`, `Balance`. `EntryId` is unique on each side (800 distinct, length 8, shape `E-000001`). Every other column has the same value set on both sides. Seeded samples at the same indexes show different entry ids, so the files are not in the same order.

## Pass 1 — keep

**Saw:** peek only. Signed amounts at two decimals, balances at two decimals, dates `yyyy-MM-dd`, currencies CHF/EUR/GBP/JPY/USD.

**Hypothesis:** `EntryId` is a shared reference. The other columns are the same record written in a different row order and should compare exactly.

**Edit:** first profile. `rowKey` is `EntryId`. Identifier-map it under scheme `entry`, symbol-map account and currency, parse the date and both decimals at scale 2, `match.sharedRef` on `EntryId`, exact diff. Culture `en-US`.

**Result:** matched=800 exact=800 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0. composition oneToOne=800. No field breaks.

**Decision:** keep. Full match. Stop.
