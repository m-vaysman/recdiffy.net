# Pair p10: builder report

## Final profile

`profile-pass-6.json`

## Match rate after each pass

| Pass | Summary | Note |
|---|---|---|
| 1 | matched=0, leftOnly=1021, rightOnly=380, quarantined=370 | Probe: raw columns as Text, to learn the formats |
| 2 | matched=261 exact=261, leftOnly=369, rightOnly=241, quarantined=639 | Ref pass only; a second right date format and the allocation refs quarantined |
| 3 | matched=630 exact=630, leftOnly=391, rightOnly=120 | Added the natural-key pass; leftovers are allocations against blocks |
| 4 | matched=750 exact=630 break=120 | groupSum on blocks; Price breaks because a group sums member prices |
| 5 | matched=750 exact=750 | Left package by block (Price/Date/ISIN `Same`, Quantity/NetCash `Sum`); MembersDisagree=391 from per-allocation refs |
| 6 | matched=750 exact=750, no diagnostics | Final: debug fields gone, strict patterns, TradeRef only on non-block rows |
| 7 (check) | matched=750 exact=750 | Natural key alone (TradeRef veto on) gives the same result |
| 8 (check) | matched=500 exact=500, leftOnly=250, rightOnly=250 | Without the natural pass: 250 right rows carry no ref |
| 9 (check) | matched=750 exact=750 | Finer scales: rounding hides nothing |

All 1021 left rows and 750 right rows are accounted for: 630 one-to-one pairs and 120 blocks matched to 391 left allocations.

## What the profile says

- Left is en-US, right is de-DE.
- Refs: left `TR-0030004` and right `tr/30004` both become `30004`. Blocks: left `BLK0003` and right `blk-3` both become `3`.
- ISIN is uppercased (right is lowercase). Side: left `B`/`S` and right `BUY`/`SELL`, a closed code map.
- Right `Cash (cents)` is unsigned. It is multiplied by -0.01 when D/C is `D` and by 0.01 when it is `C`, so it matches the left's signed Net Cash. This is checked on every row, because NetCash is compared exactly.
- Right `Settled` holds the same date as left `Trade Date`, written `dd.MM.yyyy` or `yyyyMMdd`. It is compared exactly and agrees on every row, despite the column name.
- Left allocations are merged by block before matching: Price, TradeDate and ISIN must agree across allocations, and Quantity and NetCash are summed.
- Three passes: ref, then ISIN+Side+Quantity+Price, then block+ISIN+Side.

## Confidence: high

Evidence:

- **Everything matches and is compared exactly.** No row is unmatched, ambiguous or quarantined, and no tolerance is used. Every pair agrees on ISIN, Side, Quantity, Price, NetCash and TradeDate. There is no diagnostic.
- **Strict reading.** The ref and block patterns, both side maps and both date parses reject anything outside the shapes found. NetCash is required on the right, so a D/C code other than D or C would quarantine. None fired.
- **The natural key is not a coincidence.** The 250 ref-less pairs are unique on ISIN, Side, Quantity and a 4-decimal Price (ambiguous=0). On top of that, NetCash to the cent and the date agree, and neither is in the key. With the ref pass removed (pass 7), the natural key alone reproduces all 750 matches. The TradeRef veto stops it pairing two rows whose refs differ, so it agrees with every ref.
- **Blocks are checked, not just summed.** The engine rejects a package whose allocations disagree on price, date or ISIN. The merged price, summed quantity and summed cash each equal the block row exactly.
- **No rounding hides a difference.** Pass 9 used scales of 6, 10 and 8 and was still exact.

Ready for human approval.

## Open items (not blocking)

- Left `Entity` and right `Venue` have no counterpart on the other side, so they are not compared. A human who knows an entity-to-venue relation could add it.
- The right `Settled` column holds the trade date in this data. If it ever carries a real settlement date, TradeDate would break and need a `days` tolerance or its own field.

## Engine gaps

- **`match.groupSum` sums every decimal on the many side**, so a per-unit field such as Price is compared as a sum (pass 4). Packages worked around it here, because the allocations carry a block id. Rule that would close it: let a group pass take a per-field aggregate (`Same`, as packages do) for non-conserved fields.
