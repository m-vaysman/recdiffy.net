# Argon.MD - what is missing: claude-opus-5.5

- Model ID: `anthropic/claude-opus-5.5` (via OpenRouter)
- Date: 2026-11-04
- Reasoning: effort high
- Prompt: [prompt.md](prompt.md)

---

# Gaps in Argon.MD and What to Add

Argon.MD explains the problem well. The data-shape section is concrete and makes a convincing case that the naive pipeline fails. But the document asks for architecture and working code without specifying several things that would determine whether the result is correct, fast, or usable. The gaps are grouped below by how much they would affect the design.

---

## 1. Critical gaps (these change the architecture)

### a. The input schemas and benchmark data aren't included
The document refers to `InternalTrade`, `CounterpartyTrade`, and a 300 vs. 200 row dataset, but it never shows:
- the actual class definitions (property names, types, nullability)
- a sample of rows, especially the edge cases
- **the expected reconciliation outcome**: which rows should pair, and in which pass

Without these, any implementation has to invent field names, and nobody can verify whether it's correct.

**Add:** the C# type definitions (or CSV headers with types) for both sides, 10–20 representative rows, and a ground-truth answer key, e.g. "L-042 ↔ R-017, Pass 2, Break on SettleDate."

### b. No scale or performance targets
"High-performance" and "zero-boxing" are stated, but the only dataset is 500 rows. A design for 500 rows differs from one for 5M rows: in-memory vs. streaming, indexing strategy, parallelism, and how far subset-sum can go.

**Add:** target volumes (typical and peak rows per side), a latency budget, a memory ceiling, allocation goals, and whether input arrives as `IEnumerable`, `IAsyncEnumerable`, or file streams.

### c. No complexity bounds on N-to-1 subset-sum
Subset-sum is NP-hard. The document asks for an implementation but sets no limits, so the result could be either useless or explosively slow.

**Add:**
- maximum group size (e.g. ≤ 8 allocations)
- partitioning constraints that must hold before summing (same counterparty, instrument, side, trade date)
- tolerance on the aggregate sum
- a per-bucket time or iteration budget, and what happens when it's exceeded (flag as "unresolved," don't silently skip)

### d. Match cardinality is incomplete
Only N-to-1 (Left many → Right one) is specified. The following are missing:
- **1-to-N**: one internal block vs. multiple street executions or partial fills. The "80% partial fill" example may actually be this case, not a break.
- **N-to-M**
- **duplicates within one side** (the same UTI appearing twice)

**Add:** a statement of which cardinalities are in scope, and how duplicates and ambiguous candidates are surfaced.

### e. No determinism, tie-breaking, or claim semantics
When several candidates qualify, it's unclear which one wins. Several related questions are open:
- Is matching greedy per pass, or globally optimal (e.g. Hungarian assignment in the fuzzy pass)?
- Once a record is claimed in Pass 1, can a later pass reconsider it?
- Must results be reproducible run-to-run, regardless of input order?

For an audit-facing reconciliation tool, determinism is usually mandatory and should be stated explicitly.

### f. The top-level result model is undefined
Stage 5 specifies a field-level diff AST, but the overall reconciliation output isn't specified. It should probably include:
- matched pairs or groups, with the pass that matched them, the key used, and a confidence score
- unmatched Left and unmatched Right records
- ambiguous or multi-candidate records
- records rejected during normalization (see next item)
- lifecycle-collapsed records, with their lineage (which NEW/CANC/CORR rows were absorbed)
- **explainability**: why each match was made

### g. No failure or quarantine semantics
The document doesn't say what happens when:
- a 32nds string is malformed (`"98-1X"`)
- direction is `S` **and** quantity is `-500,000`. Is that a double negative meaning BUY, or a redundant SELL? The document lists the case but gives no rule.
- a `CANC` references a nonexistent prior report
- a `CORR` chain has cycles, multiple corrections, or arrives out of order
- an alias has no mapping

**Add:** an error policy (fail fast, quarantine, or pass through flagged) and the resolution rule for conflicting sign and direction.

### h. Platform constraints: AOT and trimming
The task requires a "Compiled Expression Tree Field Differ." On NativeAOT and trimmed deployments, `Expression.Compile()` falls back to the interpreter or causes trim warnings. That undermines the performance goal.

**Add:** whether NativeAOT and trimming are supported targets. If they are, the document should invite or require a **source generator** alternative to expression trees, or a dual strategy.

---

## 2. Important gaps (these change the configuration model)

### Reference data dependencies
Several normalizations can't be done from the row alone:
- **Dirty to clean price** requires accrued interest, which needs the coupon, day-count convention, and settlement date.
- **Alias to entity** (`GS_NY_PRIME` → `GSCO`, LEI lookups) requires a mapping source.
- **Security ID cross-lookup** may need a security master if Left lacks some IDs.
- **T+1 vs. T+2 settle date** checks need holiday calendars.

**Add:** a reference-data provider abstraction (`IAliasResolver`, `ISecurityMaster`, `IHolidayCalendar`, `IAccruedInterestCalculator`). State that these are consumer-supplied, and specify whether they are sync or async and whether they are cached.

### Tolerance semantics
The document should specify:
- absolute vs. relative tolerance, and whether it can vary by currency or instrument
- date tolerances in business days vs. calendar days
- how tolerance applies to **aggregated** values in N-to-1 matches
- numeric type policy (`decimal` vs. `double`) and rounding mode
- whether a tolerance can depend on other fields (e.g. tolerance scaled by notional)

### Multi-leg consolidation rules
"Group `Parent_Link_ID` rows into 1 net row" leaves open how each field is aggregated. Notional might be summed or taken from one leg; rates might come from the fixed leg. Per-field aggregation strategies need to be configurable.

### Lifecycle on the Left side
Stage 2 describes NEW/CANC/CORR only for the Right feed. Internal systems also amend trades. State whether lifecycle collapsing applies to both sides.

### Fuzzy pass definition
Pass 4 needs a defined scoring function (weighted fields? string similarity on IDs? date proximity?), plus thresholds for auto-match vs. suggest-for-review vs. reject. It should also state whether fuzzy matches are ever auto-accepted.

### Parsing and culture
`-500,000` implies culture-sensitive number parsing. Specify the expected culture and invariant-culture handling, along with time zone handling for `TradeDate`. For example, a trade executed at 23:30 New York time is the next day in UTC.

---

## 3. Secondary gaps (scope and quality)

- **External library evaluation criteria.** Task item 1 asks for a revised opinion but gives no criteria. The document should list licenses (MoreLINQ Apache-2.0, SuperLinq MIT, Compare-NET-Objects MS-PL), maintenance status, AOT compatibility, and allocation behavior.
- **Concurrency and API conventions.** Cover thread safety of compiled profiles (are they reusable singletons?), `CancellationToken` support, progress reporting, and `IAsyncEnumerable` support.
- **Stateful or incremental reconciliation.** Real back offices carry breaks forward day over day. State whether this is in scope or a non-goal.
- **The "TCanonical or adapter views" decision.** The document leaves it open. Either request a recommendation with trade-offs, or specify the preference.
- **Generality proof.** The library is described as general-purpose, but every example is financial. Add one non-financial use case (e.g. inventory vs. warehouse records, or customer master data deduplication) so the design isn't accidentally trade-shaped.
- **Testing expectations.** Specify unit tests, property-based tests (e.g. normalization is idempotent, matching is order-independent), and benchmarks with BenchmarkDotNet.
- **Deliverable scope.** The task asks for "working C# implementation" of several complex components. Specify the expected depth (compilable skeleton vs. production-grade), any length constraints, and whether stubs are acceptable for some stages.
- **Formatting.** The whole document is wrapped in a ```` ```markdown ```` fence, so many renderers will show it as raw code rather than formatted Markdown. The `$\pm \$1.50$` LaTeX may also not render in a typical README viewer.

---

## 4. Suggested sections to append

```markdown
### Inputs & Ground Truth
- Schema definitions for InternalTrade and CounterpartyTrade (attached / inline)
- Sample rows covering each edge case
- Expected results file: pair IDs, matching pass, expected field-level states

### Non-Functional Requirements
- Volumes: typical N rows/side, peak M rows/side
- Latency budget, memory ceiling, allocation targets
- Target frameworks; NativeAOT/trimming: required | optional | out of scope
- Determinism: results must be identical regardless of input order

### Matching Semantics
- Supported cardinalities: 1:1, N:1, 1:N, N:M (in/out of scope)
- Claim rules across passes; greedy vs. optimal assignment
- Subset-sum bounds: max group size, partition keys, sum tolerance, budget
- Ambiguity handling: never auto-pick; surface as Ambiguous

### Normalization & Error Policy
- Rule for conflicting direction + sign
- Lifecycle edge cases: orphan CANC, multiple CORR, out-of-order events
- Malformed values: quarantine with reason codes

### External Dependencies (consumer-supplied)
- Alias/LEI resolver, security master, holiday calendar, accrued interest calculator

### Output Contract
- Matched (with pass, key, score, explanation), UnmatchedLeft, UnmatchedRight,
  Ambiguous, Rejected, lifecycle lineage, field-level diff AST

### Non-Goals
- e.g. persistence, UI, day-over-day break aging (unless in scope)
```

---

**Summary:** The document clearly describes *why* the naive approach fails. What it lacks is the information needed to judge *whether a proposed solution succeeds*: actual schemas, a ground-truth answer key, scale targets, matching semantics (cardinality, tie-breaking, subset-sum bounds), failure policy, reference-data dependencies, and AOT constraints. Adding these would make the requested blueprint verifiable rather than speculative.
