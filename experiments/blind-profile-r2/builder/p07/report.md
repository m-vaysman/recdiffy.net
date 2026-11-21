# Builder report, pair p07

> The builder could not write this file (the agent tool refused report files), so the orchestrator copied it verbatim from the builder's structured result.

Final profile: `profile-pass-5.json`

## Passes

- Pass 1: probe: matched=0 exact=0 withinTolerance=0 break=0 leftOnly=560 rightOnly=780 ambiguous=0 searchIncomplete=0 quarantined=440 suppressed=0 (DuplicateConflict=440 on left rowKey Deal)
- Pass 2: probe, positional left key: matched=0 exact=0 withinTolerance=0 break=0 leftOnly=1000 rightOnly=780 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0
- Pass 3: matched=780 exact=560 withinTolerance=0 break=220 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (NetCash = Sum of legs breaks all 220 packages)
- Pass 4: matched=780 exact=560 withinTolerance=0 break=220 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (NetCash = DifferenceByRole(R,P), ratio -1 on all 220)
- Pass 5: matched=780 exact=780 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (NetCash = DifferenceByRole(P,R))
- Pass 6: check, pass 5 with Notional and NetCash at scale 6: matched=780 exact=780 break=0, nothing unmatched or quarantined

## Confidence: high

The profile uses all 1000 left rows and all 780 right rows: 560 match one to one and 220 are two-leg packages (Pkg Link, legs R and P). Nothing is an orphan, ambiguous, quarantined or flagged by a diagnostic. Every compared field is exact with zero tolerance: TradeRef (Deal vs Trade Ref, upper-cased), Product, Notional, NetCash and Maturity (left en-US with yyyy-MM-dd, right de-DE with dd.MM.yyyy, strict). The fields are still exact at scale 6, so rounding hides nothing. The netting rule was found step by step, not fitted to noise: Sum broke all 220 packages, R-P gave a ratio of -1 on all 220, and P-R made all 220 exact. A wrong pairing could not be exact on Notional, Product, Maturity and NetCash at once. Every package has both an R and a P leg (no IncompletePackage). The legs agree on every field that has no aggregator (no MembersDisagree). There are no value lists: the only mapping is upper-casing the reference. Report content (report.md could not be written because the agent tool refuses report files from subagents; this is noted in log.md): final profile profile-pass-5.json; match rate per pass 0%, 0%, 72% exact (100% matched), 72% exact, 100% exact, 100% exact at scale 6; confidence high; Ready for human approval; no engine gaps.

## Verdict

Ready for human approval.

## Open items

- Party exists only on the left, so it can't be reconciled. It is carried as an Identity field and not compared.
- report.md was not written: the agent tool refused it as a subagent report file. Its content is in this structured result, and log.md item 23 records the refusal.

## Engine gaps

None.
