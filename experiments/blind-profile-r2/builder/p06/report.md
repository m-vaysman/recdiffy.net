# Report, pair p06

## Final profile

`profile-pass-6.json`

| Pass | File | Matched (of 1000 per side) | Summary |
|---|---|---|---|
| 1 | profile-pass-1.json | 0% | quarantined=2000 (DateParse, DecimalParse, MissingKey) |
| 2 | profile-pass-2.json | probe, raw Symbols | matched=70 break=70 leftOnly=62 ambiguous=236 quarantined=344 |
| 3 | profile-pass-3.json | 100% | matched=1000 exact=1000, nothing unmatched |
| 4 | profile-pass-4.json | control, natural key only | matched=832 exact=832 ambiguous=75 |
| 5 | profile-pass-5.json | control, ref only | matched=704 exact=704 leftOnly=296 rightOnly=296 |
| 6 | profile-pass-6.json | 100% | matched=1000 exact=1000 at raised scales |

## How the sides differ

- Numbers: left en-US (`1,506,751.29`), right de-DE (`2.492.989,44`). Declared per side.
- Trade date: left `dd MMM yyyy`, right `yyyy.MM.dd`, strict formats.
- Side: left BUY/SELL, right B/S. Closed code map, passthrough false, so an unknown code would quarantine.
- Reference: right Cpty Ref is left Our Ref without the `TR-` prefix. About 30% of pairs have a blank ref on at least one side, so no rowKey is declared.

## Confidence: high

Evidence:
- All 1000 pairs are exact on every compared field (Isin, Side, TradeDate, Quantity, Price, Net) with no tolerance at all, also at scales well above the data's precision (pass 6). No diagnostics, nothing quarantined, ambiguous or unmatched.
- 704 pairs are matched by the reference, which is independent of the compared values; all of them agree exactly on the economics.
- The other 296 pairs are matched on Isin+Side+TradeDate+Quantity; their Price (4 dp) and Net (2 dp), which are not in the key, also agree exactly, so the pairing is not coincidental. The TradeRef veto was active and blocked nothing, and no fallback key was ambiguous.
- The only freedom left is which of two rows with identical economics and blank refs pairs with which; the result shows no ambiguity, and such a swap would change no compared value.

Ready for human approval.

## Engine gaps

None found for this pair.
