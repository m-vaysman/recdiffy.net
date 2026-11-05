# Argon.MD - what is missing: deepseek-v4-pro

- Model ID: `deepseek/deepseek-v4-pro-0813` (via OpenRouter)
- Date: 2026-11-04
- Reasoning: effort high
- Prompt: [prompt.md](prompt.md)

---

Yes. The document is an excellent problem statement, but as a full architectural blueprint it is incomplete. Below are the missing areas and specific items that should be added.

---

## 1. Missing End-to-End Data Flow and Source/Sink Abstractions

The document mentions `TLeft` and `TRight`, but never defines how records enter or leave the pipeline.

**Missing:**
- Input abstraction for left/right streams (`IAsyncEnumerable<T>`, batching, backpressure, cancellation).
- Output/result model for matches, breaks, unmatches, and summaries.
- Whether the engine is synchronous, asynchronous, or both.
- How source data is transformed into canonical records without losing original row references (important for audit/break reporting).

**Add:**
```csharp
public interface IReconciliationSource<T>
{
    IAsyncEnumerable<T> ReadAsync(CancellationToken ct = default);
}
```
and a result model:
- `ReconciliationResult<TLeft, TRight, TCanonical>`
- `MatchResult`, `UnmatchedRecord`, `BreakResult`, `ToleranceDelta`

---

## 2. Canonical Normalization Details

Stage 1 is described conceptually, but many implementation-critical rules are missing.

**Missing:**
- Exact canonical schema for financial instruments (e.g., `CanonicalTrade` with normalized fields).
- Date/time handling: timezone, calendar, `DateOnly` vs `DateTimeOffset`.
- Numeric normalization: decimals, rounding mode, sign conventions, unit scales.
- Enum/domain value mapping tables (BUY/B/BOT vs normalized `Buy`).
- Null vs missing vs default semantics.
- Custom converter registration and validation failure policy.
- How to preserve original values alongside canonical values for diff output.

**Add:**
- `ITypeNormalizer<TInput, TOutput>` interface.
- `ICanonicalProjector<TLeft, TRight, TCanonical>` interface.
- Policies for culture, timezone, rounding, and `null` propagation.

---

## 3. Stage 2 Lifecycle & Package Consolidation Semantics

“Cancel & Correct Chains” and “Multi-Leg Packaging” are mentioned, but the exact state machine and rules are not defined.

**Missing:**
- Event ordering: by timestamp? sequence number? arrival order?
- Versioning: how to identify the latest active state.
- Handling of duplicate IDs, missing prior reports, out-of-order arrivals, and partial cancellations.
- What fields survive from `NEW` vs `CORR` (e.g., does a correction inherit all fields or only changed ones?).
- Multi-leg package identity: what is the package key? How to pair legs? What if one leg is missing or canceled?
- Netting rules for combining legs into a single canonical record.
- Whether consolidation is idempotent and deterministic.

**Add:**
- `ILifecycleConsolidator<T>` with explicit ordering and version resolution.
- State machine for `NEW`, `CANC`, `CORR`, `CANCEL`, `REBOOK`.
- Package consolidation policy: leg role mapping, sum/aggregate function, missing leg handling.

---

## 4. Multi-Key Index & Resolver Details

The document says records can match on `UTI`, `ISIN`, `CUSIP`, `SEDOL`, or `Ticker`, but not how to build or query that index efficiently without duplicating records.

**Missing:**
- How to normalize and hash composite keys.
- Index structure: multiple dictionaries vs single multi-column key.
- Handling of partial keys and missing identifiers.
- Key collision resolution.
- Memory bounds for very large datasets.
- Query strategy for “any of these four columns matches” — exactly the multi-index problem.
- Candidate pruning before downstream passes.

**Add:**
- `IMultiKeyIndex<T>` with `TryLookup(KeySet keys)` or vectorized key generation.
- A “key matrix” defining which fields participate in which pass.
- Flagging records with duplicate keys to avoid false matches.

---

## 5. Cascading Match Pass Orchestration

The four passes are listed, but the mechanics controlling pass ordering, lock/unlock of records, conflict resolution, and residual flow are missing.

**Missing:**
- How a record is marked “matched” and removed from subsequent passes.
- How leftover records flow from Pass 1 to Pass 2, etc.
- What happens when a record could match multiple records in the same pass (ambiguity).
- Tie-breaking rules (e.g., highest score, most recent, deterministic ordering).
- Maximum candidate set size for fuzzy matches.
- How matches are persisted/created after each pass and whether later passes can override earlier matches.

**Add:**
- `IMatchPass<T>` with clear input/output and matched/unmatched sets.
- `IMatchOrchestrator` that applies passes in order and maintains bookkeeping.
- Explicit ambiguity policy: fail, use best score, or queue for manual review.

---

## 6. N-to-1 / Subset-Sum Semantics

Stage 4 Pass 3 says “N-to-1 Subset-Sum / Group Aggregation for allocation splits” but lacks crucial operational details.

**Missing:**
- Exact vs tolerance-based subset matching.
- Sign handling for buys/sells (negative quantities).
- Group key definition for performing subset-sum within a group.
- Maximum N considered (performance blowup is exponential).
- What to do with non-matching remainders.
- Aggregation function beyond simple sum (e.g., weighted average price for multiple allocations).

**Add:**
- `IAllocationGroupMatcher<T>` with configurable tolerance and max group size.
- Algorithm constraints: brute force only for small N, otherwise heuristic/ILP.
- Output schema: matched groups, leftover partials, and aggregation method.

---

## 7. Diff Engine and Tolerance Model

The document says “tri-state field diffing”, but the actual AST shape and tolerance rules are not specified.

**Missing:**
- Exact diff AST node types and structure.
- Property path representation.
- Old value, new value, normalized delta.
- Tolerance definition:
  - Absolute (`±$1.50`)
  - Relative / percentage
  - Date-based (`T+1` vs `T+2`)
  - Per-field, per-type, or global
- Break reason codes and categories.
- How to handle nested objects/collections within a record.
- Custom comparer registration.
- Serialization of diff results for reporting/export.

**Add:**
- `DiffNode` with `Exact`, `WithinTolerance`, `Break` variants.
- `ToleranceRule` abstraction (`AbsoluteTolerance`, `RelativeTolerance`, `DateTolerance`, `None`).
- `IDiffEngine<T>` that compiles expression trees per profile.

---

## 8. Error Handling, Logging, and Diagnostics

The document focuses on happy-path behavior but omits what happens when data is malformed or pipeline stages fail.

**Missing:**
- Invalid data during normalization (bad 32nds string, unknown enum).
- Partial pipeline failure: should it abort or collect all errors?
- Exception vs result object policy.
- Logging / tracing / telemetry for debugging reconciliation runs.
- Idempotency and reproducibility of a run.
- Cancellation and progress reporting.

**Add:**
- `ReconciliationException` or error collection in `ReconciliationResult`.
- `ILogger` integration and activity source.
- Policy for invalid records: skip, fail, or route to an error bucket.

---

## 9. Performance and Scale Constraints

The document mentions “zero-boxing” and a benchmark dataset, but no measurable acceptance criteria.

**Missing:**
- Target throughput (records/sec) for benchmark and production.
- Memory allocation budget (KB per record, GC pressure).
- Maximum dataset sizes for each stage.
- Parallelization strategy (stages, batches, partitions).
- Benchmarks to validate the design.

**Add:**
- Performance goals in the spec (e.g., 300k records under 200 MB, 50k records/sec).
- Allocations per record budget.
- Async/parallel pipeline design notes.

---

## 10. Configuration, Extensibility, and DSL Completeness

The document asks for a `ReconciliationProfile<TLeft, TRight, TCanonical>` DSL, but doesn’t specify how profiles are stored, versioned, or extended.

**Missing:**
- Serialization format for profiles (JSON, YAML, code-only?).
- Profile validation at build time.
- Versioning/migration for profiles.
- DI integration.
- Custom extension points:
  - Custom normalizers
  - Custom match passes
  - Custom diff comparers
  - Custom fuzzy scorers
- How to express conditional policies (e.g., different tolerance for FX vs equities).

**Add:**
- `IReconciliationProfile` with `Validate()` and `Build()`.
- Fluent DSL examples including custom comparers and match passes.
- Documentation on how to add new stages without forking the core engine.

---

## 11. External Library Opinion Missing

The document explicitly asks for a “Revised Opinion on External Libraries vs Custom Core,” but the text only mentions that the naive pipeline fails. It never states what actually survives.

**Missing conclusion:**  
- `SuperLinq` / `MoreLINQ` are useful for data preparation, testing utilities, or optional adapters, but not for the core matching/diffing engine due to allocation patterns and lack of custom match semantics.
- `CompareNETObjects` is not suitable for performance-critical field diffing. It may remain as a fallback/diagnostic adapter only.
- Core engine should use custom compiled expression trees, custom hash indexes, and deterministic streaming operations.

---

## Summary of What Should Be Added

| Area | What to add |
|------|-------------|
| Source/sink | Input abstraction, result model, async support |
| Canonicalization | Normalizer interfaces, date/number/culture rules, error policy |
| Lifecycle consolidation | State machine, version resolution, idempotency, package key |
| Multi-key index | Index structure, collision handling, candidate pruning |
| Match orchestration | Pass ordering, lock mechanism, ambiguity policy |
| N-to-1 matching | Tolerance, group key, max N, remainder handling |
| Diff engine | AST schema, tolerance rules, break codes, custom comparers |
| Error handling | Exception/error collection, logging, idempotency |
| Performance | Throughput, allocation budget, parallelization |
| Extensibility | DSL completeness, serialization, validation, custom extensions |
| External libraries | Explicit conclusion on SuperLinq/MoreLINQ/CompareNETObjects |

If these items are added, the document will become a truly implementation-ready architectural specification for **RecDiffy.NET**.
