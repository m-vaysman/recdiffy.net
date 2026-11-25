# Builder report, pair p08

> The builder could not write this file (the agent tool refused report files), so the orchestrator copied it verbatim from the builder's structured result.

Final profile: `profile-pass-4.json`

## Passes

- Pass 1: summary matched=71 exact=0 withinTolerance=0 break=71 leftOnly=18 rightOnly=0 ambiguous=269 searchIncomplete=0 quarantined=0 suppressed=0 (exploratory: ISIN key, raw text)
- Pass 2: summary matched=600 exact=0 withinTolerance=0 break=600 leftOnly=160 rightOnly=0 ambiguous=160 searchIncomplete=0 quarantined=0 suppressed=0 (Ref key, per-side cultures, typed fields; breaks only on temporary probe fields)
- Pass 3: summary matched=760 exact=760 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=360 (left lifecycle added; composition oneToOne=760 lifecycle=160)
- Pass 4: summary matched=760 exact=760 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=360 (Quantity scale 6, Price scale 10: rounding hides nothing)
- Pass 5: summary matched=760 exact=760 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=360 (corrTarget DIRECT sensitivity check: no change)

## Confidence: high

All 760 right rows match one left lifecycle survivor one-to-one and are exact on Isin, Side, Quantity, Price and TradeDate. No field has a tolerance. There are no left-only, right-only, ambiguous or quarantined rows and no diagnostics, and the lifecycle suppressed 360 left events.

The mapping: left is an event log. Its Event Type values NEW, AMEND and CANCEL go through a closed map to NEW, CORR and CANC with passthrough off, Replaces is the target and Party is the scope. Right is one row per final trade. The key is Deal Ref, which equals Trade No once the '-' is removed. Left is read as en-US and right as de-DE. Side codes are B/S on the left and BUY/SELL on the right, both closed maps. Dates are yyyy-MM-dd on the left and yyyyMMdd on the right.

Why the match is not a coincidence:
- Before the collapse (pass 2), AMEND chains disagreed with the right file on quantity, for example 1090 vs 1080. After it they agree, so the right survivor was picked.
- Raising Quantity to scale 6 and Price to scale 10 changes nothing, so rounding hides no difference.
- Any Event Type or side code outside the maps would have quarantined its row, and none did.
- Every Replaces target resolved within its Party.
- Switching corrTarget to DIRECT gives the same result, so no AMEND targets a CANCEL.

The Write tool refused to create report.md because subagents may not write report files. I did not work around it: the report content is in this result, and log.md records the refusal at entry 22.

## Verdict

Ready for human approval.

## Open items

- Party is only in the left file, so it serves as the lifecycle scope and is never compared. A human should confirm that is acceptable.
- report.md could not be written because the Write tool blocked it for a subagent. Its content is in this result, and log.md (entry 22) records the refusal.
- Passes 2 and 4 to 5 each produced a profile file by text replace (pass 2 also used three temporary probe fields that write a constant on the right). No script touched the data.

## Engine gaps

None.
