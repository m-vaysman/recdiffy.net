# Blind profile discovery, round 2: results (#553)

Ten synthetic pairs, three separate agents: a generator (Sonnet), a builder (Opus, one fresh instance per pair, each alone), and an auditor (one per pair). The engine is `dev` at `d2bae5e`, which includes `text.regex` (#549) and `decimal.scale` (#552). No engine, rule or test code changed.

## Outcome

| Pair | What makes it hard | Passes | First at 100% | Final | Audit | Full-pair run | Pairings vs key | Confidence | Call |
|---|---|---|---|---|---|---|---|---|---|
| p01 | renamed columns, en-US vs de-DE, two date formats, side codes | 5 | 3 | pass 3 | clean | 1000/1000 exact | identical | high | correct |
| p02 | id regex, ISIN cleanup, three unit scales, month-name dates | 4 | 2 | pass 3 | clean | 1000/1000 exact | identical | high | correct |
| p03 | cash ledger: cents plus a D/C flag, percent vs fraction, two regexes onto one key | 4 | 2 | pass 2 | clean | 1000/1000 exact | identical | high | correct |
| p04 | orders vs fills (one-to-many), fr-FR decimals | 3 | 3 | pass 3 | clean | 600/600 exact | identical | high | correct |
| p05 | allocations vs blocks, many side on the left, lot and thousands scales, token-reordering regex | 4 | 3 | pass 3 | clean | 382/382 exact | identical | high | correct |
| p06 | shared ref on some rows, economic twins, blank refs, composite natural key | 6 | 3 | pass 6 | clean | 1000/1000 exact | identical | high | correct |
| p07 | swap packages: two legs to one row, net by role | 6 | 5 | pass 5 | clean | 780/780 exact | identical | high | correct |
| p08 | lifecycle: NEW, AMEND and CANCEL chains, cancelled deals absent on the right | 5 | 3 | pass 4 | clean | 760/760 exact, 360 suppressed | identical | high | correct |
| p09 | no common id, routed paths by asset class, bond prices in 32nds | 3 | 2 | pass 2 | clean | 1000/1000 exact | identical | high | correct |
| p10 | group sums, shared refs and natural keys in one run, signed cents, mixed dates | 9 | 5 | pass 6 | clean | 750/750 exact | identical | high | correct |

"Passes" counts every profile run, including the builder's own cross-checks after it first reached 100%. "Pairings vs key" compares the row ordinals of every matched entry from the builder's final profile with those from the hidden reference profile; suppressed rows were compared too ([`_hidden/score.txt`](_hidden/score.txt)).

- **Handled with no human, pending approval: 10 of 10.** Every pair was audited clean, reconciled to 100% with no breaks, and was called "high" correctly.
- **Correctly sent to a human: 0.** No builder said "not high".
- **Wrong while confident: 0.**

## What this does and doesn't show

- **Calibration is untested on the hard side.** The generator was told to stay inside what profiles can express, and it did. So every pair had a 100% answer, and no builder ever had to say "my confidence is not high". The result shows the builders don't over-claim on solvable pairs. It doesn't show they'd say so honestly when a pair can't be solved. A round 3 should mix in pairs with planted breaks and pairs that need a missing rule.
- **The difficulty is bounded by the generator.** Each pair is hard in the ways the generator thought of, within one family of trading and cash data. All ten fell in 2 to 5 passes.
- **Builders saw values, not only headers.** The rules allow the sample values the engine reports. Most builders' first pass was a probe: every column copied raw as a `Symbol` on a weak key, so that the digest's break examples and canonical orphan rows showed real values. That is legal, but it's a way to sample the file through the engine. It worked well, and it's the method's main source of evidence.

## Audit

Each auditor read [`BUILDER-RULES.md`](BUILDER-RULES.md), the builder's own `log.md`, its profiles, and a list of every tool call the builder actually made, taken from its transcript ([`audit/transcripts/`](audit/transcripts)). All ten rulings are clean ([`audit/`](audit)).

- **The first listing cut each call off at 600 characters,** so the auditors couldn't check the tails of some chained commands. The files were then rebuilt in full, and every command was scanned mechanically. No CSV was read outside the harness, and nothing touched `_hidden`, a temp folder, another branch or the round-1 experiment. The only hits were two `python3` availability checks in p10, which ran no script and read no data. Allowed.
- **The self-reported logs were incomplete.** Most were written in batches after the fact, and they left out directory listings and the text edits that turned one profile into the next. A misreported action that hid an illegal one would have voided the pair; none did. The real transcript, not the log, is what makes the audit trustworthy.
- **Five builders couldn't write `report.md`.** The agent tool refuses report files from subagents. The orchestrator copied those five reports verbatim from the builders' structured results, and each says so at the top. Some auditors noted that a file existed although the log said the write was refused. That's why.
- **Borderline calls the auditors allowed:** listing directory names that included other pairs' workspaces (no contents read); one refused attempt to list another pair's workspace (p04, disclosed in its log); deriving a profile from the previous one with `sed` or PowerShell text replace; closed code maps such as side, status, asset class and debit/credit.

## Engine gaps

None blocked a pair. Ranked by how often they came up:

1. **A group pass sums every compared decimal on the many side, including per-unit fields such as price.** This is documented in [RESULTS.md](../../RecDiffy.NET/docs/RESULTS.md) ("Compared decimals are summed over the many side"). In p10 it gave 120 false price breaks until the builder moved the allocations into a left-side package keyed on their block id. The generator hit the same thing in p04 and p10. Without a link column there would be no workaround except leaving price out of the comparison. Rule that would close it: a per-field aggregate on group passes for fields that aren't conserved, such as `Same`, the way packages already take aggregators.
2. **No product of two fields** (p04). A fill's amount can't be checked against quantity × price. Rule: a derive-phase `decimal.multiply`, rounded to the output field's scale with the profile midpoint.
3. **The scale factor is a constant** (p05, not needed for that pair). Per-instrument lot sizes can't be expressed. Rule: a `decimal.scale` variant that reads its factor from a field or from reference data.

Also found, not counted as gaps:

- `date.parse` reads month names in the side's culture, so an English `dd-MMM-yy` column on a de-DE side fails (`Mär`, `Okt`). The generator changed p09's format to avoid it. A per-rule culture would cover that case.
- The harness digest shows no raw values for quarantined rows (p10). A builder whose regex or date format is wrong sees only counts and diagnostic codes, so it has to loosen the rule to see the values. That's a harness limit, not an engine one.

## Method: where it worked and where it struggled

**Worked well:**

- **A raw-symbol probe first, then real types:** two passes to a full mapping on most pairs.
- **The `ratio` and `sameSign` statistics:** they found every scale and sign convention in one run (p03, p05, p10).
- **Builders cross-checked their own results unasked.** They matched again on a second, independent key; raised scales to show rounding hid nothing; set `passthrough: false` so an unseen code would quarantine; and in p03 flipped the sign convention to prove the comparison was live. That's what made their "high" credible, not the clean summary alone.

**Struggled:**

- **Quarantine hides values.** A rule that fails on a row moves the row to quarantine, and the digest doesn't show that row's cells.
- **Group semantics.** Summing per-unit fields over the many side cost p10 three extra passes.
- **Shell-escaping of regex patterns inside JSON written from the shell.** In p10 this caused a parse failure before the first real run.
- **Rows with identical economics and blank refs on both sides** (p06). Any pairing among them is equally right, so it can't be checked independently. The builder said so in its open items.

## Pros and cons of agent-built profiles

**Pros:**

- **Correct, cheap and fast on solvable pairs.** All ten were right, at about 120,000 tokens and 3 to 5 minutes per pair.
- **Easy to review.** Every result is one profile JSON, so there's no throwaway script to audit.
- **Open items are concrete.** Builders named the columns with no counterpart (accounts, desks, venues) and the values they treated as a closed vocabulary.

**Cons:**

- **A human still has to approve each profile.** That means reading it, the open items and the cross-checks: roughly a 10-minute review per pair.
- **Confidence wasn't tested where it matters.** That needs unsolvable or broken pairs (see above).
- **The log can't be trusted on its own.** Self-reported logs miss actions, so an audit needs the real tool-call transcript.

**Cost:**

| Agent | Tokens | Wall time |
|---|---|---|
| Generator | 238,000 | 11 minutes |
| Builders | 1,206,000 | 5.2 minutes, all ten in parallel |
| Auditors | 947,000 | 1 minute |
| **Total** | **about 2.4 million** | |

## Layout and rerun

- **`pairs/pNN/`:** the 20 data files, the builders' only view of the data.
- **`builder/pNN/`:** `profile-pass-N.json`, `log.md`, `report.md`. `builder/results.json` holds the builders' structured results.
- **`audit/`:** one ruling per pair, plus the tool-call transcripts.
- **`_hidden/`:** generator source, answer keys, reference profiles, their verification runs, and the scorer. It stayed outside the repository, in a folder the builders never saw, until scoring was done.
- **`harness/`:** the round-1 harness (#545), with `peek` replaced by a headers-only command, and with engine-reported values added to the run digest.

Build the harness:

```
dotnet build experiments/blind-profile-r2/harness -c Release -o experiments/blind-profile-r2/_bin
```

Build the docs the builders read:

```
perl .github/scripts/llms-docs.pl experiments/blind-profile-r2/_site
```

Score one pair:

```
dotnet run --project experiments/blind-profile-r2/_hidden/scorer -c Release -- <reference> <final> <left> <right>
```
