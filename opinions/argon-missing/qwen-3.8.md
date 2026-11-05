# Argon.MD - what is missing: qwen-3.8

- Model ID: `qwen/qwen3.8-2.4t-a95b` (via OpenRouter)
- Date: 2026-11-04
- Reasoning: effort high
- Prompt: [prompt.md](prompt.md)

---



Yes. **Argon.MD is currently a very strong problem statement and task prompt, but it is not yet a complete design/specification document.**

If the intent is that this file should become the authoritative design brief for **RecDiffy.NET**, then several important things are missing. If the intent is only to prompt an implementation proposal, it is still missing enough constraints and acceptance criteria that different implementers could produce incompatible but “valid” solutions.

Below is what I would add.

---

## 1. Clarify the document’s purpose

The document says:

> “Your Task … provide a comprehensive architectural revision and C# implementation blueprint”

But it does not say whether **Argon.MD itself** is supposed to contain that blueprint, or whether it is merely asking for one.

### Add one of these explicitly

If this is a **task prompt / RFC**, add:

```markdown
## Document Purpose

This document is a design request / RFC. The expected output is a proposed architecture,
interface set, and implementation blueprint for RecDiffy.NET.
```

If this is meant to be the **actual design document**, then the document is missing the answer sections:

```markdown
## Revised Opinion on External Libraries
...

## Revised 5-Stage Pipeline Architecture
...

## Core C# Blueprint
...
```

Right now it contains the question, not the answer.

---

## 2. Formatting issue: remove the outer Markdown code fence if this is the real `.md` file

The document is wrapped in:

```markdown
```markdown
...
```
```

If `Argon.MD` is intended to be rendered as Markdown in GitHub/GitLab/Azure DevOps, the outer fence should probably be removed. Otherwise the entire document will render as a code block instead of formatted documentation.

Use:

```markdown
# Argon - RecDiffy.NET Design Brief

We are designing and implementing RecDiffy.NET...
```

rather than wrapping the whole file in a Markdown code fence.

---

## 3. Add document metadata

For a repository design document, add front matter or a header section:

```markdown
# Argon: RecDiffy.NET Architecture Brief

Status: Draft / Under Review / Approved
Owner: @username
Last Updated: YYYY-MM-DD
Target Frameworks: .NET 8 / .NET 9+
Language Level: C# 12 / C# 13
Related Documents:
  - Benchmark Plan
  - Test Dataset Specification
  - Public API Proposal
```

This is missing and important for maintainability.

---

## 4. Add scope, non-goals, and design priorities

The document says RecDiffy.NET is “general-purpose,” but it does not define the boundary between core engine and financial-domain adapters strongly enough.

### Add

```markdown
## Scope

RecDiffy.NET is a general-purpose reconciliation and diffing engine.
Financial trade reconciliation is a first-class sample/adapter scenario, not a core assumption.

The core engine should provide:
- normalization/projection hooks
- intra-side consolidation hooks
- multi-index candidate resolution
- cascading match passes
- compiled field diffing
- structured diff reporting
- tolerance classification
- extensibility points for domain-specific policies

## Non-Goals

The core engine should not:
- hardcode OMS, swap, bond, or counterparty semantics
- require financial schemas
- embed reference data such as CUSIP/ISIN/SEDOL/LEI mappings
- provide persistence, workflow, or UI features
- require CompareNETObjects in the core dependency graph
```

Also add explicit priority order:

```markdown
## Design Priorities

1. Correctness and explainability
2. Deterministic behavior
3. Performance and low allocation
4. Extensibility
5. Ease of configuration
```

Without this, implementers may optimize for the wrong thing.

---

## 5. Add a Definition of Done / Acceptance Criteria

This is one of the biggest missing pieces.

### Add

```markdown
## Acceptance Criteria / Definition of Done

A successful implementation must:

1. Compile under .NET 8+ with nullable reference types enabled.
2. Treat warnings as errors in the core project.
3. Contain no financial-specific types in the core engine.
4. Provide a public API that allows consumers to configure:
   - canonical projection
   - normalization
   - lifecycle consolidation
   - key resolution
   - match passes
   - field comparers
   - tolerance rules
   - diff classification
5. Produce deterministic results for a given input dataset and profile.
6. Support:
   - left-only records
   - right-only records
   - exact matches
   - tolerance matches
   - economic breaks
   - ambiguous candidates
   - quarantined/malformed records
7. Include a sample financial adapter demonstrating:
   - 32nds price parsing
   - direction/sign normalization
   - dealer perspective inversion
   - ISIN/CUSIP/SEDOL/Ticker cross-key matching
   - cancel/correct lifecycle collapse
   - multi-leg consolidation
   - N-to-1 allocation matching
8. Include golden tests with expected reconciliation output.
9. Include benchmarks demonstrating memory/CPU characteristics.
10. Document any external dependencies and justify their placement.
```

This turns the document from a narrative into a testable requirement set.

---

## 6. Add explicit result taxonomy

The document describes tri-state field diffing:

- Exact Match
- Matched Within Tolerance
- Economic Break

But reconciliation also needs record-level outcomes.

### Add

```markdown
## Record-Level Reconciliation Outcomes

The engine should classify every input record or logical group into one of:

- MatchedExact
- MatchedWithinTolerance
- MatchedWithBreaks
- LeftOnly
- RightOnly
- Ambiguous
- Suppressed
- Quarantined / Invalid
- LifecycleCollapsed
- PackageConsolidated
```

Also define whether an unmatched record is considered a break.

For example:

```markdown
A LeftOnly or RightOnly record is a record-level break unless explicitly suppressed
by policy. Field-level diffing only applies after record/group pairing.
```

This is currently implied but not specified.

---

## 7. Add a canonical model or at least a canonical contract

The document mentions:

> `TLeft` and `TRight` -> `TCanonical`

But it does not define what the canonical model contains or how normalization affects equality.

### Add

```markdown
## Canonical Record Contract

The canonical representation should include, where applicable:

- SourceRecordId
- Side
- NormalizedSide
- Quantity
- SignedQuantity
- Price
- NormalizedPrice
- Currency
- TradeDate
- SettlementDate
- CounterpartyKey
- InstrumentKeys
- LifecycleStatus
- ParentGroupId
- OriginalRawValues
- NormalizationWarnings
```

Also specify:

```markdown
Canonical normalization must preserve original raw values for auditability.
A canonical exact match may still expose a raw representation difference.
```

This matters for cases like:

```text
Left: 98.359375
Right: "98-11+"
```

After normalization they may be equal, but the diff report may still want to show the original representations.

---

## 8. Add precise normalization rules

The document gives excellent examples, but does not specify the rules.

Add explicit normalization policies for:

### Counterparty aliases

```markdown
Counterparty normalization should resolve internal and external aliases through
a configurable alias map.

Examples:
- GSCO -> GS
- GS_NY_PRIME -> GS
- JPM -> JPM
- JPMS_CLEAR -> JPM
- 20-char LEI -> canonical counterparty key if mapped
```

Also specify what happens when no alias mapping exists.

### Direction and sign

You mention:

- `BUY`, `SELL`
- `B`, `S`
- `BOT`, `SLD`
- lowercase variants
- blank direction with negative quantity
- both `S` and `-500,000`

The document should specify conflict rules.

Example:

```markdown
## Direction / Sign Normalization Rules

1. Direction text is case-insensitive and trimmed.
2. Recognized buy synonyms: BUY, B, BOT.
3. Recognized sell synonyms: SELL, S, SLD.
4. If direction is missing and quantity is negative:
   - canonical side is Sell
   - canonical quantity is absolute value
5. If direction is present and quantity sign conflicts:
   - if direction is Sell and quantity is negative, treat as Sell with positive quantity
   - if direction is Buy and quantity is negative, raise a NormalizationWarning and classify according to policy
6. Double-encoded signs must not cause double inversion.
```

Without this, behavior is ambiguous.

### Dealer perspective inversion

The document says:

> On bilateral OTC feeds, an internal BUY / PAY_FIXED is reported from the dealer's side as SLD / REC.

Add:

```markdown
Dealer perspective inversion must be explicitly configured per feed/source.
The engine should not infer dealer perspective unless the profile supplies a rule.

The normalization result should record whether inversion occurred.
```

Also define how to avoid double inversion when both direction and sign are already encoded.

### 32nds price parsing

You mention `"98-11+"`, but the meaning of `+` should be specified.

Add:

```markdown
## 32nds Price Parsing

The financial adapter must support Treasury-style fractional pricing.

Examples:
- "98-11" => 98 + 11/32
- "98-11+" => 98 + 11/32 + 1/64, if plus denotes a half-32nd
- "98-112" may mean 98 + 11.25/32 if using plus-one-eighth notation

The exact convention must be configurable.
Invalid 32nds strings should produce a normalization issue and be routed according to error policy.
```

Right now the example is domain-realistic but underspecified.

### Dirty price / accrued interest

Add:

```markdown
Dirty price handling requires:
- clean price
- accrued interest
- day count convention
- currency precision
- rounding mode

The adapter should expose a pluggable AccruedInterestCalculator.
```

Otherwise “Dirty Price” is too vague.

---

## 9. Add explicit Stage 0: input validation and quarantine

The proposed pipeline starts with normalization, but real reconciliation needs input validation first.

### Add

```markdown
## Stage 0: Validation, Source Identity, and Quarantine

Before normalization, each input record should be validated for:

- missing required fields
- invalid dates
- invalid numeric values
- malformed identifiers
- duplicate source record ids
- missing lifecycle references
- unknown side/direction values
- unsupported currency codes

Records may be:
- accepted
- accepted with warnings
- quarantined
- rejected
```

This is missing and critical.

---

## 10. Add explicit lifecycle consolidation rules

The document mentions:

> Cancel & Correct Chains (`NEW`, `CANC`, `CORR`)

But does not define the state machine.

### Add

```markdown
## Lifecycle Consolidation Rules

For each logical trade/event chain:

1. Group events by source trade/event id.
2. Order events by sequence number, timestamp, or feed-provided ordering.
3. `NEW` followed by `CANC` referencing that trade should collapse to a cancelled/no-effective-record result.
4. `CORR` should replace the prior effective version unless correction semantics are additive.
5. A `CANC` or `CORR` referencing a missing prior record should be quarantined or treated as an orphan lifecycle event.
6. The effective record should preserve the full event chain for audit.
7. Cancelled records may be suppressed from matching unless policy requires break reporting.
```

Also add:

```markdown
The output of Stage 2 should be:
- effective standalone records
- consolidated packages/groups
- cancelled/suppressed records
- lifecycle exceptions
```

---

## 11. Add explicit multi-leg consolidation rules

The document says:

> Left books swaps/forwards as 2 linked rows sharing a `Parent_Link_ID`, while Right sends 1 combined net row.

Add exact behavior.

### Add

```markdown
## Multi-Leg Consolidation Rules

When multiple records share a Parent_Link_ID:

1. Treat them as one logical package for matching.
2. Preserve individual legs for audit and diffing.
3. Produce a package-level canonical projection.
4. Package-level fields may include:
   - net quantity or net notional
   - combined fees
   - primary instrument
   - primary currency
   - earliest/latest trade date
   - leg count
5. Missing legs should raise a warning or quarantine the package.
6. A package may match a single right-side record, but diff output should include both package-level and leg-level explanations.
```

Also define whether package matching is mandatory or optional.

---

## 12. Add explicit multi-index / cross-key resolver semantics

The document asks:

> how to index a record when an entity can be identified by UTI OR ISIN OR CUSIP OR SEDOL OR Ticker without duplicating records across buckets

This is a hard problem and needs explicit requirements.

### Add

```markdown
## Cross-Key Index Requirements

The resolver should:

1. Assign each canonical record or package a stable surrogate key.
2. Build an inverted index from normalized identity facts to surrogate keys.
3. Support multiple identity facts per record without duplicating the record payload.
4. Support aliasing/union-find where different identifiers resolve to the same logical entity.
5. Support key precedence ordering, for example:
   - UTI / Exec ID
   - ISIN
   - CUSIP
   - SEDOL
   - Ticker + Exchange
6. Record identity facts should be normalized before indexing.
7. Candidate generation should return distinct surrogate keys, not duplicated records.
8. Ambiguous identifier matches should be retained as candidate edges, not immediately collapsed.
```

Also specify identifier normalization:

```markdown
Identifier normalization:
- ISIN: uppercase, validate length/check digit if configured
- CUSIP: uppercase, validate 9-character format if configured
- SEDOL: uppercase, validate check digit if configured
- LEI: uppercase, 20-character format
- Ticker: configurable exchange suffix normalization
```

---

## 13. Add explicit cascading match-pass contracts

The document lists four passes:

1. Exact Shared ID
2. Natural Business Key 1:1
3. N-to-1 Subset-Sum / Group Aggregation
4. Fuzzy/Heuristic scoring

But it does not define the contract for each pass.

### Add

```markdown
## Match Pass Contract

Each match pass receives:

- remaining unmatched left records/groups
- remaining unmatched right records/groups
- candidate index
- reconciliation profile
- prior pass results

Each match pass returns:

- proposed matches
- rejected candidates
- ambiguous candidates
- diagnostics/reasons
- records consumed by the pass

A record/group consumed by a pass is unavailable to later passes unless the profile allows revisiting.
```

Also define pass ordering and determinism:

```markdown
Match passes must execute in a deterministic order.
If parallel candidate generation is used, final match selection must be deterministic.
Tie-breaking rules must be explicit.
```

---

## 14. Add exact rules for N-to-1 subset-sum/group matching

The document mentions allocation splits:

> Left books 3 sub-fund allocations (`400k + 500k + 600k`) that sum up to 1 single Prime Broker block trade (`1,500k`)

But subset-sum matching can explode combinatorially. The document should constrain it.

### Add

```markdown
## N-to-1 Group Matching Rules

Group matching should be constrained by:

1. Same or compatible:
   - instrument
   - side
   - currency
   - counterparty
   - trade date window
   - settlement date window
2. Summation field must be explicitly configured:
   - quantity
   - notional
   - principal
   - allocated amount
3. Tolerance for group sum must be explicitly configured.
4. Maximum group size must be configurable.
5. Search timeout or candidate budget must be configurable.
6. Exact-sum matches should be preferred over tolerance matches.
7. If multiple subsets match, choose by explicit tie-break:
   - smallest cardinality
   - highest score
   - earliest source sequence
   - deterministic ordering
8. Unmatched residual allocations should be reported.
9. Group match output should include:
   - parent/right block record
   - child/left allocation records
   - sum delta
   - tolerance state
   - per-child diff results
```

Also specify whether M-to-N matching is in scope.

```markdown
M-to-N group matching is out of scope for the first version unless explicitly required.
```

Or, if it is in scope, say so.

---

## 15. Add explicit fuzzy matching rules

The document says:

> Pass 4: Fuzzy/Heuristic candidate scoring for broken trades

But does not define scoring, thresholds, or ambiguity.

### Add

```markdown
## Fuzzy Matching Requirements

Fuzzy matching must define:

1. Candidate generation rules.
2. Hard constraints that disqualify candidates.
3. Soft scoring features, such as:
   - counterparty similarity
   - instrument identifier overlap
   - side compatibility
   - trade date proximity
   - settlement date proximity
   - quantity proximity
   - price proximity
   - fee proximity
   - text similarity
4. Score normalization.
5. Minimum score threshold.
6. Maximum score delta allowed between top candidates before marking ambiguous.
7. One-to-one enforcement policy.
8. Tie-breaking policy.
9. Whether fuzzy matches require confirmation or are automatically accepted.
10. Audit trail explaining why a fuzzy match was chosen.
```

Also add:

```markdown
Fuzzy matching should never silently choose between equally plausible candidates.
Ambiguous matches should be surfaced as Ambiguous, not forced into Matched.
```

---

## 16. Add explicit handling for unmatched and ambiguous records

The document focuses on matched pairs but does not fully define what happens to records that do not match.

### Add

```markdown
## Unmatched and Ambiguous Handling

The engine must support:

- LeftOnly
- RightOnly
- Ambiguous
- SuppressedByPolicy
- Quarantined

For each unmatched or ambiguous record, the result should include:

- original record references
- canonical projection if available
- reason codes
- candidate list, if any
- scoring details, if fuzzy matching was involved
- recommended action, if configured
```

This is essential for reconciliation systems.

---

## 17. Add explicit diff semantics

The document says:

> Exact Match, Matched Within Tolerance, Economic Break

But it does not define how those decisions are made.

### Add

```markdown
## Field Diff Classification

For each compared field:

1. Exact:
   - canonical values are equal according to the field comparer
2. WithinTolerance:
   - values are not equal but fall within a configured tolerance
   - the exact numeric or temporal delta must be preserved
3. Break:
   - values are not equal and fall outside tolerance
   - or a required field is missing on one side
   - or a policy-specific rule classifies the difference as an economic break

Field diff results should include:
- field path
- left value
- right value
- canonical left value
- canonical right value
- typed delta
- tolerance applied
- classification
- reason
```

Also define record-level aggregation:

```markdown
A record pair is:
- Exact if all compared fields are Exact
- WithinTolerance if no field is Break and at least one field is WithinTolerance
- Break if any field is Break
```

---

## 18. Add tolerance specification

The document gives examples:

- `± $1.50` accrued interest rounding
- 4th-decimal FX rate

But tolerance needs a formal model.

### Add

```markdown
## Tolerance Model

Tolerances may be:

- absolute
- relative/percentage
- tick-based
- decimal-place based
- date/time-based
- currency-specific
- field-specific
- culture/precision-specific

Example:

- AccruedInterest: absolute tolerance 1.50 currency units
- FxRate: absolute tolerance 0.0001
- Quantity: relative tolerance 0.0001 or absolute tolerance 1 unit
- SettlementDate: no tolerance by default; T+1 vs T+2 is a break unless policy allows calendar-aware tolerance
```

Also specify rounding:

```markdown
Tolerance comparisons must specify:
- numeric type: decimal, double, long ticks, etc.
- rounding mode
- midpoint behavior
- whether tolerance is inclusive
```

---

## 19. Add explicit “zero-boxing” and allocation constraints

The document asks for:

> Zero-Boxing Compiled Diff Engine

But also asks for a structured diff AST. Those two goals can conflict.

### Add

```markdown
## Allocation Policy

Define where allocations are allowed.

For example:

- Hot field comparison path: no boxing, no per-field allocations.
- Compiled delegates: cached and reused.
- Diff AST construction: allocations allowed, but should be bounded and intentional.
- Result summaries: allocations allowed.
- String formatting/export: allocations allowed.

The benchmark should report:
- allocations per matched record pair
- allocations per reconciliation run
- Gen0/Gen1/Gen2 collections
- total time
```

Without this, “zero-boxing” is ambiguous.

---

## 20. Add AOT / trimming / reflection constraints

The document asks for compiled expression trees. That has implications for NativeAOT and trimming.

### Add

```markdown
## Reflection, Expression Trees, and AOT Policy

Specify whether the library must support:

- reflection-based expression compilation
- source-generated comparers
- reflection-free fallbacks
- NativeAOT
- trimming

If NativeAOT is required, Expression.Compile may be restricted.
In that case, provide a source-generator or delegate-based fallback.
```

This is missing and important for modern .NET.

---

## 21. Add performance and scale targets

The document says:

> high-performance

But does not define what that means.

### Add

```markdown
## Performance Targets

The implementation should be validated against targets such as:

- 300 x 200 benchmark dataset: < X ms, < Y KB allocated
- 10,000 x 10,000 dataset: < X seconds, < Y MB allocated
- Index construction: O(n) expected for typical key counts
- Match passes: avoid O(n*m) full cross joins except where explicitly configured
- Compiled field differ: compile once per profile, not once per record
- No reflection per field comparison in the hot path
```

You do not need exact numbers immediately, but the document should require them.

---

## 22. Add determinism requirements

Reconciliation results must often be reproducible.

### Add

```markdown
## Determinism Requirements

Given:
- the same input records
- the same profile configuration
- the same reference data
- the same library version

the engine must produce:
- the same match decisions
- the same diff classifications
- the same ordering if ordered output is requested

If parallelism is used, final output ordering and match selection must remain deterministic.
```

This is especially important for fuzzy matching and subset-sum search.

---

## 23. Add error handling and failure policy

The document does not say what happens when data is malformed.

### Add

```markdown
## Error Handling Policy

The engine should distinguish:

- configuration errors
- invalid profile errors
- input validation errors
- normalization errors
- lifecycle reference errors
- matching ambiguity
- diff classification warnings
- fatal engine errors

Policies should allow:
- fail fast
- collect and continue
- quarantine record
- suppress warning
- escalate warning to break
```

Also add:

```markdown
Configuration validation should occur before reconciliation starts.
Invalid profiles should throw descriptive exceptions rather than failing mid-run.
```

---

## 24. Add observability, audit, and explainability

Reconciliation is not only about producing matches; it is about explaining why.

### Add

```markdown
## Explainability and Audit Requirements

Every match and diff result should include:

- pass that produced the match
- keys used
- candidate score, if applicable
- normalization decisions
- lifecycle events applied
- package/group membership
- field-level reasons
- original raw values

Optional outputs:
- structured logs
- metrics
- OpenTelemetry spans
- reconciliation summary counts
```

Also mention redaction:

```markdown
Sensitive trade data should not be logged unless explicitly enabled.
Provide redaction hooks for logs and telemetry.
```

---

## 25. Add testing strategy

The document mentions a benchmark dataset but does not define testing requirements.

### Add

```markdown
## Testing Strategy

The implementation should include:

1. Unit tests for:
   - normalization
   - 32nds parsing
   - side/sign normalization
   - identifier normalization
   - lifecycle consolidation
   - multi-leg grouping
   - tolerance comparison
   - compiled differ

2. Golden-file tests for:
   - sample 300 x 200 dataset
   - cancel/correct chains
   - allocation splits
   - multi-leg packages
   - fuzzy matches
   - ambiguous matches
   - unmatched records

3. Property-based tests for:
   - determinism
   - symmetry where appropriate
   - tolerance boundary behavior
   - subset-sum invariants

4. Negative tests for:
   - invalid 32nds
   - missing lifecycle references
   - conflicting side/sign
   - duplicate identifiers
   - malformed dates/numbers

5. Benchmark tests for:
   - throughput
   - allocation
   - cache effectiveness
   - expression compile cost
```

---

## 26. Add golden sample data and expected outputs

The document mentions:

> 300 internal OMS trades vs. 200 external street/counterparty rows

But it does not provide expected results.

### Add

```markdown
## Golden Dataset Requirements

Provide a reproducible test dataset with known expected output.

The dataset should include:
- exact shared-ID matches
- natural-key matches
- alias-matched counterparties
- cross-identifier instrument matches
- 32nds vs decimal price matches
- dealer-perspective inversions
- cancel/correct chains
- multi-leg packages
- allocation splits
- tolerance matches
- economic breaks
- left-only records
- right-only records
- ambiguous records
- malformed/quarantined records

Expected output should include:
- match counts by category
- break counts by field
- tolerance counts
- unmatched counts
- quarantined counts
```

Without expected output, benchmarks and tests are not verifiable.

---

## 27. Add external dependency policy

The document asks for a revised opinion on:

- `SuperLinq`
- `MoreLINQ`
- `CompareNETObjects`

But it does not state dependency constraints.

### Add

```markdown
## Dependency Policy

The core library should minimize external dependencies.

Expected guidance:
- CompareNETObjects should not be a core dependency because it is reflection-oriented and does not provide reconciliation-specific tolerance, normalization, or match-pass semantics.
- SuperLinq/MoreLINQ may be used in samples, tests, or optional adapters if they simplify non-core code.
- Any dependency used in the core engine must be justified by performance, licensing, trimming, and maintenance considerations.
```

Or, if you want a stronger statement:

```markdown
The core DLL should depend only on the BCL.
Optional adapters may depend on external libraries.
```

---

## 28. Add explicit project/package layout

The document asks for a blueprint but does not specify where code should live.

### Add

```markdown
## Proposed Repository Layout

The implementation should include or propose:

- `RecDiffy.Core`
  - engine interfaces
  - match pipeline
  - diff engine
 
