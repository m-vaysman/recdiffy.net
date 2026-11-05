# Argon.MD - what is missing: grok-4.7

- Model ID: `x-ai/grok-4.7` (via OpenRouter)
- Date: 2026-11-04
- Reasoning: effort high
- Prompt: [prompt.md](prompt.md)

---

Yes. Argon.MD is a strong problem statement, but it is not yet an architecture spec. It defines the hard cases and then jumps to “write the pipeline and the C#.” Several decisions that will dominate correctness, auditability, and performance are unspecified. Add the following before anyone implements the blueprint.

### 1. Run contract: determinism, exclusivity, and explainability
The doc never says what a successful reconciliation *is*.

Add an explicit run contract:

- Same inputs + same profile + same reference-data snapshot ⇒ identical match groups, break classifications, and deltas. No dependence on dictionary order, thread scheduling, or `GetHashCode`.
- A record is assigned to at most one match group (exclusive assignment), or the engine must emit an `Ambiguous` outcome instead of guessing.
- Every match and every non-match carries a reason: pass id, key used, candidates considered, tie-break rule, normalizer version, reference-data version.
- Tie-break is specified (stable sort on canonical ids, then source ordinal), not “highest score wins” with an undefined score.

Without this, Stage 4 cannot be tested and the result is not usable for break management.

### 2. Outcome model beyond tri-state field diffs
Tri-state (`Exact` / `WithinTolerance` / `Break`) only covers paired fields. The engine also needs row- and group-level outcomes:

- `Matched`, `MatchedWithTolerance`, `Break`
- `LeftOnly`, `RightOnly`
- `Ambiguous` (two or more exclusive candidates above threshold)
- `Quarantined` (normalization or lifecycle failure)
- `UnresolvedPackage` (orphan `CANC`, broken `Parent_Link_ID`, quantity not conserved)
- `PartialGroup` (N-to-1 subset found but residual quantity remains)

Also define break aging inputs: stable `MatchGroupId` derived from canonical keys, so today’s run can be diffed against yesterday’s. That is missing entirely.

### 3. Failure and quarantine policy per stage
“32nds parsing” and cancel/correct collapsing will throw or drop rows in real feeds. Specify:

- Per-stage error policy: fail-fast vs quarantine-and-continue.
- Diagnostic payload: stage, raw value, field, policy rule id, exception kind — not a naked exception.
- Whether a quarantined row is removed from later indexes (it must be) and still appears in the result (it must).
- Invariant checks between stages, especially quantity/notional conservation after cancel-correct collapse and after N-to-1 aggregation. If `400k+500k+600k` becomes `1,450k`, that is a package break, not a silent match.

### 4. Normalization is a ordered, versioned transform pipeline — not a projection
Stage 1 lists transforms but not their algebra.

Add:

- Ordered, named transforms with explicit non-commutativity. Dealer-perspective inversion, sign-from-quantity, and blank-direction inference must run in a documented order. Applying inversion after sign-normalization is a different trade.
- Equivalence maps as **reference data**, not code: `GSCO → LEI`, `GS_NY_PRIME → LEI`, with as-of date and version stamped on the run.
- Parse failures vs semantic blanks: missing, blank, zero, and “encoded in another column” are different.
- Numeric model: `decimal` (or scaled integer) for money; forbid `double` for prices/fees; define rounding mode before tolerance comparison.
- Culture-invariant parsing for `"98-11+"` and for decimal separators.
- Calendar dependency for settle-date breaks: `T+1` vs `T+2` is meaningless without a holiday calendar id. Either include a calendar port or state that date diffs are raw calendar-day diffs only.

### 5. Lifecycle consolidation needs conflict rules, not just “collapse”
Stage 2 names `NEW` / `CANC` / `CORR` and multi-leg packaging. Missing:

- Graph model: cancel edges (`Cancels_Prior_Report_ID`), correction supersedes, cycles, duplicates, cancels-of-cancels.
- Orphan and forward-reference policy.
- Which row is the survivor, and which fields are taken from `CORR` vs original `NEW`.
- Both sides can have lifecycles. Collapse **intra-side first**, then match. Do not cross-match intermediate busts.
- 2-to-1 packaging: required legs, optional legs, what “net row” means (sum notionals? pay/receive pair?), and what happens if one leg is missing.
- Output of the stage is a `Package` (1..n source rows → 1 canonical entity) with lineage back to source ids. Later diffs must be able to point at the survivor *and* the collapsed members.

### 6. Identity index: equivalence classes, not “index by any key”
“Index by UTI OR ISIN OR CUSIP without duplicating records” is the right constraint and the wrong data structure if left as a slogan.

Specify:

- A record has a set of **key claims**. Keys live in typed namespaces (`Uti`, `Isin`, `Cusip`, …) so a 9-character string cannot collide across CUSIP and something else.
- Alias resolution happens before indexing, into a canonical key, or the index stores equivalence-class ids.
- Intra-side duplicate keys are a first-class outcome (two live rows, same UTI), not an index overwrite.
- Cross-key does not mean the record is findable under every key of the *other* side unless a claim or alias says so. Left `Ticker=IBM` must not join Right `Primary_Security_ID` unless that value is classified as a ticker (or an explicit any-of extractor says so).
- The multi-key resolver returns **candidate sets**, not a single hit. Disambiguation belongs to Stage 4.

### 7. Matcher algorithm constraints (this is the real gap in Stage 4)
Cascading passes are listed. The hard parts are not.

Add:

- **Blocking before scoring.** Fuzzy pass must not be O(n·m). Specify block keys (counterparty + trade date bucket + instrument class).
- **Exclusive assignment** after each pass: matched rows leave the pool. Later passes see only residue. Document whether a tolerance match in pass 2 blocks a perfect natural-key match that would have been found if order differed — pass order is semantic, not an optimization.
- **N-to-1 is not “subset-sum.”** Unbounded subset-sum is NP-complete and, with tolerances, ill-posed. Require: max group size, same block key, quantity (and optionally notional) conservation within tolerance, deterministic DP or meet-in-the-middle with a timeout, and a rule when **multiple subsets** sum to the block. “First subset that fits” is not acceptable unless the tie-break is specified.
- **Many-to-many** exists (partial allocations on both sides). Either support it under the same bounds or explicitly defer it.
- **Pass 4 fuzzy** needs a score vector, a minimum margin over the runner-up (else `Ambiguous`), and a prohibition on auto-matching economic breaks that the business must confirm. Configurable: auto-link vs suggest-only.
- Greedy matching is allowed only with the documented tie-break. If you need optimal assignment, say min-cost matching and its complexity bound.
- Cancellation: `CancellationToken` on each pass; partial results are not a successful run unless the contract says so.

### 8. Diff engine: what is actually compared, and AOT
“Zero-boxing compiled expression tree” conflicts with the stated target (.NET 8/9, and likely Native AOT / trimming). Expression trees are a poor default there.

Add a decision:

- Prefer a source generator or precompiled accessor delegates emitted at profile-build time; keep expression trees as a JIT-only adapter.
- Diff **canonical fields**, not raw left/right shapes, plus an optional raw-evidence attachment.
- Tolerance is per field and typed: absolute, relative, ticks, business-days — not a single `± decimal`.
- Nested and multi-value fields (fee schedules, legs that remain after packaging) need a stated strategy: positional, keyed, or multiset.
- Delta must preserve exact numeric difference, unit, and which side was basis. Tolerance match still stores the delta (the doc says this; the type system should force it).
- Null policy: null vs 0 vs missing-on-one-side is configurable per field (`TreatNullAsZero` is dangerous for prices).
- No dynamic compilation of policy loaded from JSON unless you also specify a sandbox. Prefer a closed DSL that only binds known accessors.

### 9. Performance model the 300×200 benchmark does not imply
The benchmark is toy-sized. State targets and non-goals:

- Expected scale (10⁵, 10⁶, 10⁷ rows) and memory ceiling.
- Allocation budget: profiles compiled once; per-row work on `Span` / pooled buffers; no per-field boxing; no `string` keys if integer interning or `Utf8String` is the plan.
- Parallelism: which stages are parallel (normalize, block) and which must stay single-threaded for determinism (assignment).
- Incremental vs full recompute: late corrections should not require an unspecified second product. Either define a watermark/incremental mode or declare batch-only.

### 10. Profile as a versioned artifact
The fluent `ReconciliationProfile<TLeft,TRight,TCanonical>` is necessary but not sufficient.

Add:

- Profile id, semantic version, and hash stored on every result.
- Compile step that validates: every match pass has key extractors; tolerance fields exist on `TCanonical`; lifecycle ids are configured if that stage is enabled; no pass references a field that normalization does not produce.
- Separation the doc asks for in question 1, made concrete: core DLL has pipeline, packages, index, assignment, diff AST, and the profile compiler. Optional adapters: SuperLinq/MoreLINQ join helpers (not in core — they cannot express exclusive multi-pass residue or N-to-1), CompareNETObjects (debug/adapter only — it will not give tri-state tolerance AST or zero-boxing), reference-data providers, fractional-price parsers, holiday calendars.
- What does **not** survive in core: any API that materializes a full outer join before field compare, and any reflection-based deep compare on the hot path.

### 11. Test and diagnostic requirements (acceptance, not afterthought)
The doc should require, as part of the blueprint:

- Golden cases for each failure mode already listed: alias, sign-and-blank direction, dealer inversion, 32nds vs decimal, dirty vs clean, cross-id column, cancel/correct chain, orphan cancel, 2-leg vs 1 net, 3-to-1 allocation, multiple valid subsets, ambiguous fuzzy pair, tolerance vs economic break.
- Property tests: exclusivity, conservation, determinism under shuffled input.
- A structured trace (pass → candidates → decision) that can be asserted without scraping logs.
- Metrics: residue counts per pass, quarantine rate, ambiguous rate, subset-sum timeouts.

### 12. Small but load-bearing omissions inside the existing five stages
Worth a short subsection so they are not rediscovered in code review:

- Direction vocabularies and the case where **both** `S` and `-500000` are present and disagree.
- Dirty price: compare clean-to-clean only, or clean+accrued with accrued tolerance separate from price tolerance. Mixing them creates false economic breaks.
- Identifier check digits (CUSIP/ISIN/SEDOL) — normalize before compare or you will fuzzy-match typos.
- Fee sign and currency: a $450 break in a different currency is not a $450 break.
- Unsigned quantity + side vs signed quantity: after normalization there is exactly one canonical side enum and one non-negative quantity (or the opposite convention — pick one).
- Source lineage and ordinal stability for inputs that are not already keyed.

### What I would add to the document itself
A short “Decisions” section in front of the task, so the C# blueprint has invariants to satisfy:

1. Batch, deterministic, exclusive assignment; ambiguous rather than guess.
2. Five stages with quarantine, lineage, and conservation checks between them.
3. Reference data and calendars are versioned inputs to the run.
4. N-to-1 only inside a block, bounded, tolerance-aware, deterministic on multi-solution.
5. Diff is over canonical fields via generated accessors; expression trees are optional and not AOT-core.
6. Core stays free of SuperLinq, MoreLINQ, and CompareNETObjects.
7. Result AST includes group outcome, field tri-state, exact deltas, and the decision trace.

Until those are written down, a fluent DSL and a subset-sum matcher will look complete and still be wrong on the dataset the doc already describes. I can draft that decisions section and the public result/profile interfaces against these constraints if you want that next.
