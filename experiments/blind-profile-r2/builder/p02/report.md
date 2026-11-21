# Builder report, pair p02

> The builder could not write this file (the agent tool refused report files), so the orchestrator copied it verbatim from the builder's structured result.

Final profile: `profile-pass-3.json`

## Passes

- Pass 1: summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=1000 rightOnly=1000 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (probe: raw values as symbols, keyed on raw Isin)
- Pass 2: summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (ref digits key, ISIN cleanup, side code map, x1000/x0.01/x1000 factors, two date formats)
- Pass 3: summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (scales raised to Qty 6/Px 10/Net 8; Allocated restricted to Y with passthrough false)
- Pass 4: summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (cross-check: keyed on Isin+Side+Qty+TradeDate, RefNo compared as Key)

## Confidence: high

All 1000 rows on each side pair one to one. Seven fields are compared on every pair (Isin, Side, Qty, Px, Net, TradeDate, Ccy), all exact, with no tolerance anywhere. Pass 3 raised every decimal scale far beyond the data's precision and the result stayed all-exact, so rounding to scale hides nothing. Pass 4 found the same pairing without the ref (Isin+Side+Qty+TradeDate) and compared the ref as a Key field: all 1000 exact. Two independent keys give the same pairing, so the ref transform is not coincidental. Side and Allocated use passthrough false, so any unexpected code would have quarantined; none did. The transforms: ref = digits after the prefix with leading zeros dropped (TRD-0001007 / trd/3459); ISIN strip non-alphanumerics + upper case; Side B/S and BOT/SLD to BUY/SELL (closed code map); right Size (000s) x1000, Px (minor) x0.01, Proceeds (000s) x1000; dates yyyyMMdd vs d-MMM-yyyy (en-US); Ccy upper case. Note: the agent tool refused report.md (subagents may not write report files), so the report is in this result, and the log (experiments/blind-profile-r2/builder/p02/log.md) records the refusal.

## Verdict

Ready for human approval.

## Open items

- Left Book (ACC-nnnn) has no right counterpart; it is carried as uncompared Text. Right Allocated is a different field.
- Right Allocated is Y on every row. The final profile quarantines any other value because its meaning is unknown; a reviewer should decide whether N should be allowed.
- report.md was not written: the agent tool blocks subagent report files. Its content is in this structured result.

## Engine gaps

None.
