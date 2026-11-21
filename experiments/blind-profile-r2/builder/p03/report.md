# Builder report, pair p03

> The builder could not write this file (the agent tool refused report files), so the orchestrator copied it verbatim from the builder's structured result.

Final profile: `profile-pass-2.json`

## Passes

- Pass 1: summary matched=0 exact=0 withinTolerance=0 break=0 leftOnly=1000 rightOnly=1000 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (probe: every column copied to Text fields to see canonical values)
- Pass 2: summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (final profile)
- Pass 3: summary matched=1000 exact=1000 withinTolerance=0 break=0 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (control: Amount scale 8, Rate scale 12)
- Pass 4: summary matched=1000 exact=0 withinTolerance=0 break=1000 leftOnly=0 rightOnly=0 ambiguous=0 searchIncomplete=0 quarantined=0 suppressed=0 (control: D/C signs swapped; Amount break=1000 ratio=-1@1.00)

## Confidence: high

Mapping: left Entry ID GL-yyyy-nnnnnn is rewritten by text.regex (passthrough false) to yyyy/nnnnnn, which equals right Bank Ref. That is the key of a one-to-one naturalKey pass. Post Date is yyyy-MM-dd and Value Date is M/d/yyyy, both strict. Left Amount is parsed en-US. Right Minor Units uses decimal.scale with factor 0.01 when D/C=C and -0.01 when D/C=D, plus required.present. Rate % is scaled by 0.01 to equal Rate. Currency/Ccy and Memo/Narrative are compared exactly. No tolerance is set anywhere.

Why I believe the result:
(1) All 1000 rows on each side pair 1:1, with no orphans, ambiguity or quarantine. Five independent fields (Date, Amount, Rate, Currency, Memo) are exact on every pair, so a coincidental pairing is implausible.
(2) Pass 3 raises the scales to 8 and 12 and the result is unchanged, so rounding is not hiding differences.
(3) Pass 4 swaps the sign convention and breaks all 1000 amounts with ratio exactly -1. So the comparison is live, every amount is non-zero, and D=negative is correct.
(4) Zero quarantines under required.present and the passthrough-false regex. So every D/C value is C or D, and every Entry ID has the expected shape.

report.md was not written: the Write tool refused it as a subagent report file, and I did not work around that refusal (log step 20). Its intended content is this result.

## Verdict

Ready for human approval.

## Open items

- Counterparty appears only in the left file, so it cannot be checked against the right file. It is carried as a Text field and not compared. It is unverified, not mismatched.
- report.md is missing from experiments/blind-profile-r2/builder/p03/ because the Write tool refused it. A human or the orchestrator should save it from this result if the audit needs the file.

## Engine gaps

None.
