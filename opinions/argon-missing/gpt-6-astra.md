# Argon.MD - what is missing: gpt-6-astra

- Model ID: `openai/gpt-6-astra` (via OpenRouter)
- Date: 2026-11-04
- Reasoning: effort high
- Prompt: [prompt.md](prompt.md)

---

Yes. It is a strong problem statement, but it is not yet a complete **correctness and execution contract**.

The five stages cover the major transformations. What is missing is mostly what happens when the data is ambiguous, incomplete, contradictory, or too expensive to resolve safely. Without those requirements, an implementation could satisfy the document and still silently reconcile the wrong records.

I would add the following.

## 1. Define reconciliation outcomes separately from field-diff outcomes

`Exact`, `WithinTolerance`, and `Break` describe **comparable fields after alignment**. They do not describe all reconciliation results.

The document should distinguish three independent concepts:

| Concept | Example outcomes |
|---|---|
| Alignment | Matched, LeftOnly, RightOnly, Ambiguous, Deferred |
| Input/evaluation status | Valid, Invalid, UnresolvedReference, NotComparable, Failed |
| Field comparison | Exact, WithinTolerance, Break |

For example:

- Two equally plausible candidates are **ambiguous**, not unmatched.
- A failed Treasury-price parse is **invalid input**, not a price break.
- A subset search stopped by a resource limit is **incomplete**, not proof that no match exists.
- A confident shared-ID match can still contain major economic breaks.

Also specify how field results roll up into a record/group result, including ignored fields, optional fields, and unavailable comparisons.

**Suggested addition:**

> Matching confidence, input validity, execution completeness, and field-comparison severity must be represented independently. The engine must not convert uncertainty or processing failures into apparent business mismatches.

## 2. Separate record identity, trade identity, and instrument identity

The Stage 3 wording currently risks treating these as interchangeable:

> `UTI` OR `ISIN` OR `CUSIP` OR `SEDOL` OR `Ticker`

They identify different things:

- A source-row ID identifies an input record or event.
- A UTI generally identifies a transaction.
- ISIN/CUSIP/SEDOL identify instruments.
- A ticker may require market, exchange, share class, or other scope.
- A lifecycle reference identifies an earlier event.
- A package ID identifies a grouping relationship.

Two rows sharing an ISIN are not necessarily the same trade.

Add requirements for:

- **Typed and scoped keys:** kind, namespace/scope, normalized value.
- **Authoritative equivalence mappings:** how aliases become a common entity ID.
- **Conflicting identifiers:** what happens when one identifier agrees and another contradicts it.
- **One-to-many lookup results:** unresolved identifiers must not arbitrarily select an entity.
- **Duplicate postings:** the same record found through several keys must appear once in the candidate set.

Clarify “without duplicating records across buckets”:

> Store each record once under a stable internal handle. Multiple index posting lists may reference that handle; candidate enumeration must deduplicate handles.

That is achievable. Having several keys without several index references generally is not.

## 3. Specify ambiguity, assignment, and consumption semantics

A cascading matcher needs more than pass ordering.

The document should answer:

- What if the shared ID is duplicated on either side?
- What if two natural-key candidates are equally plausible?
- What if several allocation subsets satisfy the target?
- Can a heuristic match consume a row needed by a stronger match?
- Are earlier matches final, provisional, or eligible for reconsideration?
- Is resolution greedy, uniqueness-based, or a component-level assignment problem?

A useful conceptual separation is:

1. Generate candidates.
2. Evaluate constraints and evidence.
3. Propose match groups.
4. Resolve competing proposals.
5. Commit accepted groups atomically.

Otherwise, a parallel or greedy implementation can consume overlapping records or become dependent on input enumeration order.

**Essential invariants:**

> Each active reconciliation item may belong to at most one committed match group. Group acceptance is atomic. Ties must be resolved by an explicit policy or reported as ambiguity; incidental enumeration order is not a resolution policy.

Also define whether shuffling input must preserve results. If ordering is meaningful, require explicit sequence/timestamp fields rather than relying on collection order.

## 4. Make lifecycle consolidation an explicit event-processing contract

`NEW`/`CANC`/`CORR` collapse hides substantial behavior:

- Out-of-order events.
- Duplicate deliveries.
- Cancellation arriving before the original.
- Correction referencing a missing event.
- Repeated corrections.
- Conflicting revisions.
- Reference cycles.
- An original event outside the reconciliation window.
- Late events after a prior result was produced.

There is also a pipeline issue: **a cancellation may contain only a reference and no complete economic payload**. Stage 1 must not require every event to normalize into a fully populated trade before Stage 2 can process it.

Add an envelope that conceptually separates:

- Stable source identity.
- Event identity and references.
- Event type/version/order.
- Optional normalized payload.
- Provenance and diagnostics.

Lifecycle processing should produce active items plus explicit dispositions such as superseded, cancelled, unresolved, or rejected.

Specify whether the first implementation is:

- Finite-batch only, with complete history assumed.
- Finite-batch with external lifecycle state.
- Stateful incremental processing with late-event handling.

It is fine to support only the first option initially—but that assumption must be visible.

## 5. Define aggregation semantics and bound combinatorial matching

“N-to-1 subset/group matcher” is not a complete specification.

### Aggregation is field-specific

A group may require:

- Quantity: sum.
- Price: quantity-weighted average.
- Currency: must agree.
- Settlement date: must agree, or remain a multivalued attribute.
- Identifiers: retain all source values.
- Legs: preserve separately rather than net away.

Multi-leg packaging and allocation aggregation are not necessarily the same operation. Netting two swap legs into one scalar record can destroy economically important information.

Require aggregation policies to produce both:

1. A comparison representation.
2. A membership/lineage structure retaining its constituents.

### Search must have explicit limits

Subset-sum search is potentially exponential. Require configurable limits for:

- Candidate bucket size.
- Maximum subset cardinality.
- Search work budget.
- Accepted quantity tolerance.
- Number of competing solutions retained.
- Behavior when a limit is reached.

Prefer deterministic work limits for reproducibility; wall-clock limits can remain an operational safeguard.

Also distinguish:

- Whole-group aggregation.
- Selection of complete rows into a subset.
- Partial consumption of a row.
- General N:M transportation/flow matching.

These are different problems. The document should explicitly identify which are supported.

### Do not use the discrepancy as an absolute matching prerequisite

If exact quantity equality is mandatory for alignment, the engine cannot align an 80% partial fill and then report the intended quantity break.

> Separate identity evidence and candidate constraints from economic fields being tested. State which discrepancies prevent a match and which remain reportable after matching.

## 6. Specify normalization failures, conflicts, and auditability

The examples identify transformations, but not their conflict rules.

For direction/sign normalization, define a truth table covering:

- Explicit sell plus negative quantity.
- Explicit buy plus negative quantity.
- Missing direction plus negative quantity.
- Zero quantity with missing direction.
- Dealer-perspective inversion.
- Already-normalized input.

A rule such as “negative quantity means sell” is insufficient when an explicit direction or perspective rule also applies.

Also require:

- Culture-invariant parsing.
- Explicit date/time and timezone handling.
- Missing versus null versus blank semantics.
- Precision and rounding rules.
- Units and currencies.
- Versioned alias/reference-data snapshots.
- Policy for unknown aliases and contradictory evidence.

Preserve raw input or a retrievable source reference alongside canonical values. Otherwise “Exact” can hide normalization-induced equality.

For reproducibility, record the profile version, reference-data version, and relevant as-of context with each run.

## 7. Turn tolerance support into a precise comparison contract

“Within tolerance” needs an actual definition.

For a numeric field, the profile should specify:

- Absolute tolerance.
- Relative tolerance, including its denominator/basis.
- How absolute and relative tolerances combine.
- Inclusive versus exclusive boundaries.
- Whether rounding occurs before comparison.
- Null/missing behavior.
- Overflow behavior.
- Floating-point `NaN` and infinity handling, where applicable.
- Signed delta convention, such as `right − left`.

For example, one possible policy is:

\[
|r-l| \le \max\left(a,\;q\cdot\max(|l|,|r|)\right)
\]

But that should be a configured policy, not a universal rule.

Also clarify that:

- Exact canonical equality is checked before tolerance.
- Raw and canonical equality are distinct.
- Dates may have duration or business-calendar policies.
- Strings, enums, and collections do not necessarily have numeric deltas.
- Tolerance equality is generally non-transitive and must not be used as dictionary equality.

Define whether nested objects and collections are in scope. If they are, specify ordered/unordered collection semantics, element identity, recursion limits, and cycle handling.

## 8. Clarify what “zero-boxing” and “compiled” guarantee

This requirement is currently easy to overclaim.

A `readonly record struct` boxes when stored through `object` or an interface. A heterogeneous diff AST can therefore reintroduce boxing even if comparison delegates are typed.

Specify the boundary:

> Field evaluation and delta calculation must avoid boxing on the hot path. The result representation must explicitly document whether it also avoids boxing and how heterogeneous typed deltas are stored.

Possible representations include typed field-result buffers, typed visitors, or generated result shapes. The design should choose one rather than relying on structs alone.

Also address deployment compatibility:

- Is runtime expression compilation required?
- Is Native AOT supported?
- Is trimming supported?
- Is there a generated-delegate or interpreted fallback?
- How are compiled plans cached without retaining profiles indefinitely?

Zero boxing is not the same as zero allocation, and those should have separate performance claims.

## 9. Add execution, ownership, and operational requirements

The document asks for “high performance” without defining the operating model.

Specify:

- Finite batch versus streaming/incremental operation.
- Expected input sizes and identifier fan-out.
- Memory limits and whether spill-to-disk is supported.
- Immutable compiled profiles versus per-run mutable state.
- Thread-safety and parallelism guarantees.
- Cancellation behavior.
- Fail-fast versus per-record quarantine.
- Whether cancelled/failed runs expose partial results, and how those are marked.
- Ownership and lifetime of pooled buffers and returned results.

For observability, request counts and timings for each stage, including:

- Candidate counts and largest buckets.
- Ambiguous groups.
- Lifecycle exclusions.
- Reference-resolution failures.
- Search-budget exhaustion.
- Match counts by pass.
- Field breaks and tolerated differences.

Diagnostic logging should support redaction rather than requiring raw business records in logs.

## 10. Require fixtures, invariants, and executable acceptance criteria

The 300-versus-200 example is a useful correctness scenario, but it is neither a supplied test fixture nor a meaningful performance specification by itself.

The document should include—or require the implementation to provide:

### A deterministic correctness fixture

Include concrete schemas, example policies, and expected results for:

- Every normalization case.
- Cancel/correct chains.
- Package consolidation.
- Duplicate and conflicting identifiers.
- Unique and ambiguous allocation subsets.
- Unmatched rows.
- Matched rows with each intended economic break.

### Property-based tests

Important properties include:

- No double consumption.
- Complete source-row accounting.
- Stable results under irrelevant input reordering.
- Repeated-run determinism.
- Correct tolerance boundaries.
- Conservation of relevant measures during aggregation.
- No mutation of caller-owned inputs.

### Representative benchmarks

Measure normalization, index construction, candidate search, matching, and diff materialization separately. Include:

- Larger datasets.
- Skewed key distributions.
- Duplicate-heavy buckets.
- Ambiguous cases.
- Cold-plan versus warm-plan execution.
- Allocations and peak memory, not just elapsed time.

### A clear implementation deliverable

> Provide a buildable solution with explicit target frameworks and language versions, a runnable sample, unit tests, and benchmarks. Clearly distinguish fully implemented components from illustrative interfaces or deferred features.

That prevents “working implementation” from becoming disconnected code fragments.

## Highest-priority additions

If you add only a short section, make it **“Correctness Contracts and Non-Goals”**, covering:

1. Identifier namespaces and entity-resolution rules.
2. Ambiguity handling and atomic record consumption.
3. Lifecycle ordering, incomplete events, and provenance.
4. Aggregation semantics and bounded-search outcomes.
5. Separate alignment, evaluation, and field-diff states.
6. Explicit batch/streaming, N:M, nested-diff, and AOT scope.

The central missing principle is:

> **The engine must be able to explain not only why it matched two groups, but also why it declined to match, what evidence was unavailable, and whether the search was complete.**

That is what turns the proposed pipeline from a capable matching algorithm into a dependable general-purpose reconciliation library.
