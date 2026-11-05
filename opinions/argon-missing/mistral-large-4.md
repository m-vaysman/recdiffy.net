# Argon.MD - what is missing: mistral-large-4

- Model ID: `mistralai/mistral-large-4-0` (via OpenRouter)
- Date: 2026-11-04
- Reasoning: effort high
- Prompt: [prompt.md](prompt.md)

---

Short verdict: **Argon.MD is a strong problem statement but an incomplete specification.** It vividly motivates *why* a naive pipeline fails, and it sketches a sensible 5‑stage shape — but it reads like a requirements narrative, not a buildable design doc. It has no acceptance criteria, no non‑functional targets, no semantics for the hard edge cases it itself names (null vs blank vs sentinel, ambiguity, NP‑hard subset‑sum, AOT vs expression trees), and no operational story (audit, resilience, observability). Below is what’s missing and what I’d add.

---

## 1. Blocking gaps (these can sink the project if unresolved)

**A. Reference data / entity resolution layer is absent.** The doc jumps from "aliases" (`GSCO` vs `GS_NY_PRIME`, 20‑char LEI) straight to matching, but alias resolution *across records* requires a **security master** (ticker↔ISIN↔CUSIP↔SEDOL↔LEI↔internal ID) and an **entity/counterparty master** (plus fuzzy entity matching). Same for price conventions: comparing clean vs dirty price (`98.359375` vs `"98-11+"` vs Clean+Accrued) is impossible without **instrument static data** (coupon, maturity, day‑count, coupon frequency) and an **FX rate source** for cross‑currency amounts, plus **business‑day calendars** for T+1/T+2 validity. Add a Stage 0/1.5: *Reference Data & Entity Resolution*, with a clear contract for where that data comes from (in‑memory cache, DB, adapter) and its freshness/as‑of semantics.

**B. Subset‑sum is NP‑hard.** Pass 3 ("N‑to‑1 Subset‑Sum") as stated will not scale. 3→1 is fine, but 50→1 explodes. The doc must constrain the algorithm: bucket by business key first, cap group size, use exact DP with a bounded sum (quantized to cents) or largest‑first greedy with a verification step, and define a **fallback to Pass 4** when no exact combination is found within budget. State the complexity budget explicitly.

**C. Field‑level data semantics are undefined.** The doc defines tri‑state *diff* outcomes (Exact/WithinTolerance/Break) but not tri‑state *input* semantics: is a blank direction `null`, `""`, `"N/A"`, or a sentinel? How are `0` vs missing quantity handled? What about whitespace, case, culture (`,` vs `.` decimal separator), and Unicode normalization in string IDs? Money must be `decimal` end‑to‑end (never `double`) with a stated rounding mode (banker’s vs half‑up) for tolerance comparisons. Define a **Data Semantics Charter** per field: nullability, sentinel values, parse grammar, normalization, and the exact math for `"98-11+"` → decimal (define the grammar: integer‑`32nds`‑optional `+`/`-` suffix = 1/64, with carry into the integer part).

**D. Matching conflict & ambiguity policy is missing.** What happens when one Left record matches two Right records in Pass 2? Greedy first‑come? Optimal assignment (Hungarian)? What wins when Pass 1 (UTI) and Pass 2 (natural key) disagree? There needs to be: a precedence order, a **conflict/assignment resolution strategy**, tie‑breaking rules, a minimum confidence threshold for Pass 4, dedupe so a pair matched via multiple keys counts once, and **match provenance** (which pass, which key, what score) attached to every pair — non‑negotiable for audit.

**E. "Compiled expression tree" conflicts with modern .NET deployment.** `Expression.Compile` is not NativeAOT/trimming‑friendly. The doc should state the strategy: **source generators** emitting strongly‑typed, allocation‑free comparers/differs for the hot path, with `Expression.Compile` (or an interpreter) as a fallback, plus AOT/trimming CI tests. Also define the two‑tier result model: `readonly record struct`s and pooled buffers for the hot path, immutable DTOs for serialization/UI; clarify what "zero‑boxing" actually means (zero‑allocation? no boxing?) and the ownership/lifetime of results.

**F. No performance/acceptance numbers.** It’s billed as "high‑performance," and references a 300 vs 200 benchmark, but gives no targets (rows/sec, p99 latency, memory, GC gen0 budget), no scale test (10⁶+ rows), no streaming story (`IAsyncEnumerable`, `CancellationToken`, `IProgress<T>`, `ArrayPool`), no parallelism strategy, and no golden dataset with **expected outputs** (how many pairs, how many breaks of each type). Without expected counts for the 300/200 fixture, you can’t validate correctness — only that it runs.

**G. Temporal/lifecycle model is under‑specified.** Cancel/correct chains imply event time, sequence numbers, late‑arriving rows, out‑of‑order delivery, and at‑least‑once feeds. You need: event time vs ingestion time, watermarking, **as‑of snapshots** ("reconcile as of trade date D"), idempotent replay (same input → same output), duplicate‑delivery handling, and retention of raw provenance (SourceRowId, event type, `Cancels_Prior_Report_ID`) through canonicalization — Stage 1 must not erase the fields Stage 2 needs.

---

## 2. Important additions (should‑haves)

- **Tolerance/materiality model, properly specified.** Per‑field, per‑asset‑class tolerances; absolute vs relative vs ULP; unit‑aware (price vs quantity vs amount vs FX rate); possibly asymmetric; and a rule against **cumulative drift** (ten +$1.00 diffs each "within tolerance" must not pass silently) — i.e., a materiality budget/aggregation at the record level.
- **Resilience & error handling.** Parse failures (malformed 32nds), schema drift on the Right feed, partial loads, dead‑letter queue for unparseable rows, fail‑fast policy validation at profile build time, and a defined behavior for "no match found" (left‑only/right‑only orphan reports with reasons).
- **Auditability, explainability, workflow.** Every pair/break should carry evidence: raw values, normalized values, rule IDs, tolerance applied, exact delta. Breaks need a workflow lifecycle (NEW → INVESTIGATING → RESOLVED/APPROVED, with comments/owner), suppression/whitelisting of known breaks, aging, and exportable reports (JSON/CSV) for ops. Diff AST should be versioned for downstream consumers.
- **Observability.** Structured logging, OpenTelemetry metrics (match rate by pass, break rate by field, latency histograms, subset‑sum budget exhaustion), correlation ID per run, deterministic output ordering for reproducible reports.
- **Configuration as data.** Profiles should be serializable (JSON/YAML) so tolerances/aliases change without recompiling, schema‑versioned, validated at load, hot‑reloadable, and DI‑friendly (`IOptions<>`), with plugin contracts (`IFieldCodec`, `IKeyResolver`, `IMatchScorer`, `IDiffRule`, `IReferenceDataProvider`) so adapters are truly optional.
- **Testing strategy.** Golden‑master tests on the benchmark fixture, property‑based tests (generate adversarial inputs), parser fuzzing, contract tests per adapter, mutation testing, BenchmarkDotNet in CI with regression gates, synthetic data generator (real trade data is confidential), and chaos tests (dropped/duplicated/out‑of‑order rows).
- **Generality proof.** Since the core claims to be general‑purpose, include a second, non‑financial example (e.g., customer/address dedupe and merge) to prove the engine isn’t overfit to finance — and keep all finance specifics (32nds, UTI, cancel/correct, dealer views) in a `RecDiffy.Finance` adapter profile.
- **Packaging & legal.** NuGet layout (Core / Adapters.* / SourceGenerators), target frameworks, a licenses/IP section covering the external libs under consideration and what (if anything) is vendored vs referenced, and a migration/compat story from `CompareNETObjects`.
- **Security/compliance note** (even if out of core scope): PII masking in logs, retention/erasure for recon archives, access control on break data.

---

## 3. Ambiguities inside the current text worth tightening

- **"Without duplicating records across buckets"** (Stage 3): clarify this means an *inverted index* `(scheme, value) → record ref` where a multi‑ID record naturally appears under several keys, plus dedupe at match time — not copies of the record.
- **Dealer perspective inversion** is a *relation between sides* and must be configurable **per feed/view**, with perspective metadata carried on each record; a global flip would silently corrupt non‑bilateral feeds.
- **Stage ordering invariant:** Stage 2 needs raw event IDs, so state explicitly that canonical records retain provenance (source row ID, event type, original raw values alongside normalized ones — ops need side‑by‑side raw vs normalized).
- **"Preserving the exact numeric delta":** specify `decimal`, scale, and rounding; "exact" is impossible with `double`.
- **Terminology:** "zero‑boxing" → define as zero‑allocation/no‑boxing hot path; add a glossary (bust, rebook, UTI, LEI, break, tolerance) since the doc assumes a finance‑literate reader despite claiming to be general‑purpose.
- **Scope/non‑goals:** state explicitly what v1 will *not* do (e.g., real‑time streaming, ML‑based matching, UI) so the 5 stages don’t balloon.

---

## 4. Suggested outline for Argon.MD v2 (sections to add)

1. Scope, non‑goals, definitions/glossary
2. Non‑functional requirements (throughput/latency/memory/GC, scale targets, streaming, cancellation, determinism)
3. Data Semantics Charter (nullability/sentinels, culture‑invariant parsing, decimal rules, per‑type parse grammars incl. 32nds)
4. Reference Data & Entity Resolution (security master, entity master, FX, calendars; as‑of semantics)
5. Temporal & Lifecycle Model (event vs ingestion time, watermarks, idempotency, as‑of snapshots, provenance invariants)
6. The 5‑stage pipeline (keep, but add per‑stage contracts, invariants, error behavior, and the multi‑key inverted index + constrained N‑to‑1 algorithm with complexity budget)
7. Matching & Conflict Policy (pass precedence, assignment strategy, thresholds, provenance, orphan handling)
8. Tolerance & Materiality Model (per‑field/unit/absolute/relative, drift aggregation, exact‑delta capture)
9. Diff AST & Result Model (states, strongly‑typed deltas, struct‑hot/DTO‑cold tiers, versioning)
10. Engine implementation strategy (source generators vs `Expression.Compile`, AOT/trimming, pooling, caching, thread safety)
11. Extensibility & Config (plugin interfaces, serializable versioned profiles, validation)
12. Resilience (parse failures, schema drift, DLQ, partial loads)
13. Observability & Audit (metrics, logging, lineage, break workflow, reports)
14. Packaging, licensing, migration path
15. Test & Benchmark Plan (golden fixture with expected counts, generators, fuzzing, BDN gates, chaos)
16. Security & compliance considerations
17. Open questions & ADRs

If you add items 2–9 and 15 as concrete, numbered requirements, the doc stops being "here’s why it’s hard" and becomes "here is exactly what we will build and how we’ll prove it works." Want me to draft any of those sections (e.g., the Matching & Conflict Policy or the Data Semantics Charter) in full next?
