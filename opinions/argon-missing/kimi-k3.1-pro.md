# Argon.MD - what is missing: kimi-k3.1-pro

- Model ID: `moonshotai/kimi-k3.1-pro` (via OpenRouter)
- Date: 2026-11-04
- Reasoning: effort high
- Prompt: [prompt.md](prompt.md)

---

Yes. **Argon.MD is a strong architectural prompt, but it is not yet a complete implementation specification.** It describes the difficult cases well, while leaving several correctness rules and acceptance criteria undefined. Two different implementations could satisfy the document literally and produce materially different reconciliation results.

The most important addition is a **golden end-to-end example with expected outputs**, followed by precise rules for ambiguity, lifecycle processing, grouping, tolerances, and failures.

## 1. Add an executable input/output specification

The row counts—300 left and 200 right—are not enough to validate an implementation.

Add:

- Complete schemas for `InternalTrade` and `CounterpartyTrade`, including data types, nullability, and stable source-row identifiers.
- A small representative fixture containing at least one example of every listed case.
- The expected result after every stage:
  - Records surviving lifecycle consolidation.
  - Cancellation tombstones, if retained.
  - Multi-leg consolidated records.
  - Match groups and the pass/rule that produced them.
  - Unmatched left and right records.
  - Ambiguous and invalid records.
  - Per-field states and exact deltas.
- Expected totals for the full 300/200 dataset, or a checked-in golden output file.

The fixture should include tricky cases such as duplicate UTIs, an orphan `CANC`, two equally valid allocation subsets, and `S` combined with a negative quantity.

Without this oracle, “working implementation” cannot be assessed objectively.

## 2. Define the reconciliation result model

The tri-state field diff does not describe the result of the overall reconciliation. Specify whether the run exposes distinctions such as:

- `Matched`
- `UnmatchedLeft`
- `UnmatchedRight`
- `Ambiguous`
- `Invalid` or `Quarantined`
- `Cancelled`/`Superseded`, if these remain visible

Also define the metadata retained for each result:

- Original source-row IDs and original values.
- Canonical values.
- Match group ID and cardinality.
- Matching pass, rule, score, and explanation.
- Lifecycle and consolidation provenance.
- Normalization warnings and errors.
- Match confidence, especially for fuzzy matches.

Clarify whether ambiguous candidates are returned for review or silently resolved. For reconciliation, silently choosing an arbitrary candidate should generally not be permitted.

## 3. Specify normalization and lifecycle edge cases

Several examples need normative rules, not just implementation suggestions.

### Normalization

Define:

- The direction/sign conflict matrix. In particular, whether `S` plus `-500,000` is valid and means one sell, rather than being inverted twice.
- Treatment of contradictory values, unknown aliases, malformed identifiers, and malformed 32nds prices.
- Identifier normalization: whitespace, case, padding, check-digit validation, and whether identifier type is inferred or explicitly configured.
- Numeric precision and rounding rules, preferably using `decimal` or scaled integers where appropriate.
- Invariant-culture parsing and date/time-zone semantics.
- Dirty-price conversion inputs: accrued interest, day-count conventions, and what happens when they are unavailable.
- Whether normalization failures abort the run, reject a record, or produce a diagnostic and continue.

### Lifecycle consolidation

Define:

- Ordering by event time, sequence number, ingestion order, or a combination.
- Treatment of out-of-order events, duplicate events, missing references, cycles, and multiple corrections.
- Whether a correction can follow a cancellation and which version survives.
- Whether fully cancelled chains disappear, become tombstones, or appear as unmatched records.
- How `CORR` identifies the version it replaces.
- Which multi-leg fields are summed, weighted-averaged, taken from the parent, required to be equal, or left non-comparable.

All consolidation should preserve source provenance rather than merely discarding collapsed rows.

## 4. Define matching, ambiguity, and determinism rules

The cascading matcher needs a precise consumption and conflict model:

- Later passes operate only on records not consumed by earlier passes.
- No source record can appear in two committed match groups.
- Specify when an exact shared ID is considered unique. Duplicate IDs on either side must produce a defined outcome.
- Define whether natural-key duplicates result in ambiguity, deterministic pairing, or a later matching pass.
- Specify tie-breaking using stable business/source keys, not dictionary enumeration order.
- State whether results must be invariant under input reordering and whether repeated runs must be byte-for-byte deterministic.
- Clarify that cascading greedy matching is not globally optimal unless global optimization is explicitly required.

For the multi-key index, add an important semantic constraint:

> Multiple keys generate candidate pairs; they do not automatically establish transitive identity.

Otherwise, records can become incorrectly chained together through different identifiers. Keys should be namespaced where possible so that, for example, a ticker is not blindly compared with a CUSIP. Candidate pairs also need deduplication even though index buckets store references rather than record copies.

For fuzzy matching, specify:

- Candidate blocking before scoring.
- Configurable features, weights, and thresholds.
- A separate review threshold versus automatic-commit threshold.
- Deterministic scoring and an auditable explanation.
- Whether fuzzy matching is disabled by default.

Also state the supported cardinality scope. If `1:N` and general `N:N` matching are out of scope for version one, say so explicitly rather than implying unrestricted generality.

## 5. Constrain the N-to-1 subset/group matcher

Subset-sum is potentially exponential, so “implement subset-sum” is not a sufficient operational specification.

Add:

- Mandatory blocking/grouping keys so subsets are not searched across the entire unmatched population.
- The quantity type and sign rules.
- Exact versus tolerance-based sum comparison.
- Per-field aggregation rules for price, quantity, fees, dates, and other canonical values.
- Maximum group size, candidate-count cap, time/cancellation budget, and behavior when a cap is exceeded.
- Handling of negative allocations, zero quantities, overflow, and over/under-filled groups.
- Behavior when multiple subsets sum to the target: mark ambiguous or apply an explicitly configured secondary score.
- Whether group matching is exact-only or may incorporate tolerance.
- A brute-force reference implementation for small randomized tests.

The document should acknowledge the worst-case complexity and require a bounded, grouped algorithm rather than presenting subset-sum as an ordinary linear matching pass.

## 6. Complete the field-diff and tolerance contract

Define:

- Delta direction, for example `Left - Right`.
- Whether deltas use canonical values and whether original values are also retained.
- Absolute, relative, or combined tolerance formulas.
- Whether tolerance is inclusive: `abs(delta) <= tolerance`.
- Rounding behavior and scale.
- Null versus missing versus not-applicable semantics.
- String comparison culture, case, and whitespace rules.
- Date and date/time tolerance rules.
- `double.NaN`, infinities, and decimal overflow behavior.
- Which types may produce `WithinTolerance`; nonnumeric mismatches should not accidentally receive that state.
- How field results roll up into record and group results.
- How an N-to-1 group is diffed—against an aggregated canonical projection, individual constituents, or both.

There is also a specification tension between **“zero-boxing,” “strongly typed deltas,” and a heterogeneous diff AST**. Define the guarantee narrowly. For example, zero boxing may apply to the compiled comparison hot path, while materialized result nodes may allocate or use type erasure. Do not imply zero allocation unless benchmarks demonstrate it.

## 7. Clarify the general-purpose/core boundary

State explicitly that the core engine contains policies and extension points for:

- Normalization.
- Lifecycle consolidation.
- Key extraction and candidate generation.
- Match passes.
- Group aggregation.
- Field comparison and tolerances.

UTI, ISIN, 32nds, dealer inversion, OMS event codes, and allocation rules should live in a financial sample or optional adapter package.

Also specify:

- Whether profiles are immutable and reusable after `Build()`.
- Profile validation and diagnostics for incomplete or conflicting configuration.
- Thread-safety expectations.
- Sync/async input support, cancellation, and materialization requirements. Lifecycle processing and indexing inherently require buffering, so the engine should not be described as fully streaming without qualification.
- Extensibility versus version-one out-of-scope features.

## 8. Add operational and audit requirements

For a reconciliation library, diagnostics are part of correctness. Require:

- Per-stage counts and dropped/quarantined-record counts.
- Match-rate and pass-level metrics.
- Explanations for unmatched and ambiguous records.
- Warnings for normalization coercion or conflicting source values.
- Profile/rule version information for reproducibility.
- Configurable exception versus record-error behavior.
- Care around logging sensitive financial data.

## 9. Add measurable performance and delivery criteria

“High-performance” and “zero-boxing” need acceptance tests. Specify:

- Target framework, preferably the minimum supported version such as `net8.0`.
- Whether submitted code must compile without placeholders or omitted helper methods.
- Benchmark sizes well beyond 300/200, including duplicate-key and worst-case group workloads.
- Latency, throughput, and memory/allocation budgets.
- BenchmarkDotNet allocation measurements and comparison against a defined baseline.
- Complexity expectations for indexing and normal matching paths.
- AOT/trimming requirements, if any, for expression compilation.
- Required unit, golden, property-based, and randomized tests.
- Package boundaries, dependency versions, and license constraints.

A particularly useful test suite would verify:

- No record is consumed twice.
- Input shuffling does not change valid committed matches.
- Small subset-sum results agree with brute force.
- Tolerance boundaries and delta signs are correct.
- Cancellation/correction graphs handle orphans and cycles.
- Ambiguous matches are never silently committed.
- All originally requested examples have end-to-end tests.

## Suggested acceptance paragraph to append

> The implementation must compile for `net8.0` and include a runnable end-to-end example and tests, not pseudocode or unimplemented helpers. Matching must be deterministic, must never consume a source record twice, and must surface duplicate-key and ambiguous-group cases rather than resolving them arbitrarily. Financial behavior must be configured outside the core engine. The submission must define its error, null, tolerance, aggregation, and unmatched-record semantics; preserve source provenance; and bound pathological subset/group searches. “Zero-boxing” applies only to the explicitly identified compiled comparison path and must be supported by allocation benchmarks. Correctness is validated against the supplied golden fixture and expected stage/result counts.

In short, the document already explains **what makes the problem difficult**. What it needs next is a precise definition of **what constitutes a correct, deterministic, and acceptable solution**.
