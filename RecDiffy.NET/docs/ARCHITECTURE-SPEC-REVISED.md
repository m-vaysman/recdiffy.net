# RecDiffy.NET architectural specification (revised)

**Status:** Specification for implementation review. No implementation.
**Date:** 2026-11-04
**Runtime:** C#, `net10.0` (current LTS; .NET 8 and 9 leave support in November 2026).
**Authority:** Michael's wiki pages are requirements. `Argon.MD` is the baseline pipeline. The workbooks and oracles under `test-data/` are the acceptance data. This document supersedes `docs/ARCHITECTURE-SPEC.md` and `docs/ARCHITECTURE-SPEC-claude.md` where they disagree (§14).

RecDiffy.NET reconciles two populations of records by **cascading matching**: passes run from the strongest evidence to the weakest, each on what earlier passes left, and each commit is final. Trade, bond and cash reconciliation are profiles on that engine, not features of it.

Section 12.1 separates what was verified against repository data from what is proposed and what is unverified.

## 0. Conventions

| Term | Meaning |
|---|---|
| Left / right | The two inputs. In samples, left is `Internal_Trades`, right is `Counterparty_Trades`. The engine is symmetric. |
| Row | One input row, identified by its source and `RowKey` (`Internal_Trade_ID`, `Cpty_Report_ID`). |
| Item | What matching consumes: one row, or several rows merged by Stage 2 (a lifecycle survivor, a package, identical duplicates). |
| Group | A committed set of items: 1:1, N:1, or 1:N (the mirror of N:1). |
| Delta | Right − left, on canonical values after rounding. |
| Profile | The runtime definition of one reconciliation: mappings, rules, passes, tolerances. |

Money, prices, rates and quantities are `decimal`. Dates are `DateOnly`.

---

## 1. Scope and non-goals

### 1.1 Requirements

| Wiki requirement | How it is met |
|---|---|
| General purpose; no domain in the core | The core knows fields, rules, keys and passes. Trading rules live in a separate rule pack (§1.4). |
| Matching defined at runtime | Profiles are JSON bound to a closed rule registry (§9). |
| Two unbalanced sides; anything unmatched is a break | Side sizes are independent; `LeftOnly` and `RightOnly` are record-level breaks (§6). |
| Trade matching and cash-type matching are peer paths | Both are **paths** in a profile, chosen by a route rule. Items on different paths never match. Neither path is a core type. |
| NuGet ships the DLL only; REST API and MCP server deploy separately | The engine is one DLL with BCL dependencies only. Hosts consume profile JSON and result JSON. |
| Excel side-by-side reconciliation | `SideBySideView`, a two-sided pivot over results (§6.6). |

### 1.2 Decisions on scope

| Topic | v1 |
|---|---|
| Batch vs streaming | **Batch.** Sources may be `IEnumerable<T>`, `IAsyncEnumerable<T>` or `IDataReader`, but both sides are materialized before Stage 2: lifecycle collapse and subset search need whole sides. |
| Cardinality | 1:1 and N:1. The group pass takes a `many` side, so 1:N is the same pass mirrored; acceptance profiles use `many: left`. |
| N:M | Only through known links: Stage 2 merges linked rows into one item before matching. No discovered N:M, no assignment solver. |
| Partial consumption | No. A row is wholly in one group or in none. |
| Nested diff | **No.** Canonical records are flat. Package members are kept as lineage, not compared recursively. |
| Scored or fuzzy matching | Not in v1. No oracle needs it, and the fixture rules forbid fuzzy ticker or description matching. A caller can register a custom pass (§9). |
| Cross-currency comparison, holiday calendars, persistence, break aging, UI, `.xlsx` output | Not in v1. Group IDs are stable so aging can be added later. |

### 1.3 Paths

```json
{ "paths": [
  { "id": "trade", "route": { "rule": "route.byField", "field": "RecordClass", "equals": "TRADE" } },
  { "id": "cash",  "route": { "rule": "route.byField", "field": "RecordClass", "equals": "CASH" } } ] }
```

A single-path profile uses `route.all`. The repository has no cash data, so the cash path has no acceptance test yet (Q2).

### 1.4 Packaging and dependencies

| Artifact | Contents |
|---|---|
| `RecDiffy.dll` | Engine, schema, index, passes, diff, results, profile compiler, generic rules (parse, map, trim, route). No domain types. |
| `RecDiffy.RulePacks.Trading.dll` | 32nds, dirty-to-clean, side resolution, dealer inversion, `NEW`/`CANC`/`CORR`, leg aggregators, fee inference, trade-reference classification. |

Core dependencies are BCL only: `System.Text.Json`, `System.Data`, `System.Diagnostics.DiagnosticSource`, `System.Security.Cryptography`. SuperLinq, MoreLINQ and CompareNETObjects are not used: a join operator cannot defer, consume atomically or search subsets, and nested compare is out of scope.

---

## 2. Canonical record model

### 2.1 Why typed columns

The wiki requires matching to be defined at runtime. A profile must therefore be able to declare canonical fields that no compiled C# type has, such as a derived `Fees` or a resolved `Instrument`. The two common alternatives both fail a requirement:

- a caller-supplied `TCanonical` class needs a compiled type per profile, so a JSON-only profile cannot add a field;
- a dictionary of `object` values boxes every `decimal` and `DateOnly` on the compare path (§8).

So Stage 1 writes into a **run-scoped `RecordTable`**: one typed array per declared field, plus a null bitmap. That is the whole data structure. It is append-only during Stage 1 and Stage 2, read-only afterwards, kept by the result for value access (§11) and discarded with it, and has no query language, indexes, persistence or cross-run cache. Keys live in the Stage 3 index, not in the table.

| Field type | Storage | Example |
|---|---|---|
| `Decimal` (with scale) | `decimal[]` | `Quantity` (2), `Price` (6), `NetCash` (2) |
| `Date` | `DateOnly[]` | `TradeDate`, `SettleDate` |
| `Symbol` | `int[]` of interned values | `Side`, `Currency`, `AssetClass` |
| `Identifier` | `int[]` of interned `(scheme, scope, value)` | `Counterparty`, `Instrument`, `TradeRef` |
| `Text` | `string[]` | `Security_Text`; descriptive only, never keyed or diffed |

### 2.2 Trade schema

| Canonical field | Role | Left column | Right column |
|---|---|---|---|
| `Counterparty` | Key | `Counterparty_Code` | `Counterparty_LEI` → code (reference data) |
| `Instrument` | Key | `ISIN`, else `Ticker` | `Primary_Security_ID` under `Security_ID_Source` |
| `AssetClass` | Key | `Asset_Class` | `Product_Type`, mapped |
| `TradeDate` | Key | `Trade_Date` | `Trade_Date` |
| `Side` | Key | `Side` | `Dir_Ind` + quantity sign + perspective (§3.3) |
| `Quantity` | Economic | `Quantity_Notional` | `abs(Reported_Qty_Notional)` |
| `Price` | Economic | `Clean_Price_Rate` | `Exec_Price_Rate` by `Price_Quote_Convention` |
| `SettleDate`, `Currency`, `Accrued`, `Coupon`, `Maturity` | Economic | own columns | `Value_Settle_Date`, `Trade_Ccy`, `Reported_Accrued_Int`, `Coupon`, `Maturity` |
| `Fees` | Economic | `Commission_Fees` | derived from net cash (§3.3) |
| `NetCash` | Economic | `Net_Settlement_Amount` | `Net_Cash_Amount` |
| `TradeRef` | Identity | `UTI_Exec_ID` | `Shared_Market_Exec_Ref` |
| `EventId`, `EventAction`, `EventTarget` | Lifecycle | none | `Cpty_Report_ID`, `Trans_Lifecycle_Action`, `Cancels_Prior_Report_ID` |
| `PackageRef`, `LegRole` | Package | `Parent_Link_ID`, `Leg_Structure` | none |

`Key` fields may appear in pass keys. `Key` and `Economic` fields are diffed: a P1 match can still disagree on side or instrument (fixture v1 reports `Direction` and `InstrumentID` breaks). `Identity` fields are evidence and are never diffed: the fixture oracle states that non-shared references are not economic differences.

### 2.3 Identifiers

An identifier is `(scheme, scope, value)`, interned once. Rules:

1. Values compare only within one scheme and scope. A right value `91282CT68` under source `CUSIP` is looked up as a CUSIP only.
2. `instrument.resolve` maps every instrument identifier to one canonical `Instrument` in Stage 1, using a security master from reference data or, if the profile says so, built from the left side's `ISIN`/`CUSIP`/`SEDOL`/`Ticker` columns. An identifier that maps to two instruments gets `UnresolvedInstrument` and cannot satisfy instrument keys. (Sonnet: 0 conflicts; 3 right rows unresolved, for example `CG-20542470`, AT&T, not in our book.)
3. Shared keys never imply transitive identity. Sharing a `TradeRef` with A and an `Instrument` with B does not join A and B.
4. A trade reference's scheme comes from a profile format rule, never from whether it happens to match: a **shared** scheme (a reference both parties use) or a **broker** scheme scoped to the counterparty. §9.2 lists the rule per profile. A broker reference with no counterpart finds no candidate and is never a break.

### 2.4 Provenance

Every row has a `SourceRef(source, rowKey, ordinal)`. Every canonical field that a rule transformed has a trace entry `(ruleId, field, raw, canonical)`, always kept. Raw text of the other mapped columns follows `RawRetention`: `OnDifference` (the default) keeps it for fields that end `WithinTolerance` or `Break`; `Always` and `Never` do what they say. The result exposes canonical values, raw text and traces (§11). Example: right `BA-18777889` has `Exec_Price_Rate = "96-02+"` traced by `price.from32nds` to `96.078125`, so an exact match to left `OMS4152729` still shows that normalization produced the equality.

---

## 3. Pipeline

### 3.1 Stages

| Stage | Output | Failure handling |
|---|---|---|
| 0 Intake | Rows with `SourceRef`; duplicate handling (§3.2) | Unreadable source or missing mapped column fails the run. |
| 1 Normalize | `RecordTable` rows, traces, diagnostics | `Invalid` diagnostics quarantine the row (§3.4). |
| 2 Consolidate | Items: lifecycle survivors, packages | Broken chains and incomplete packages are quarantined (§5). |
| 3 Index | Posting lists per pass key | None on data. An item with no key for a pass is `NoKey` for that pass. |
| 4 Match | Groups, contests, residue | Search limits give `SearchIncomplete` (§4.7), never a silent miss. |
| 5 Diff | Field outcomes and deltas | Type mismatches are profile errors caught at compile time. |
| 6 Classify | `ReconciliationResult` | Asserts row accounting (§6.2); a violation is an engine bug and fails the run. |

Ordering: Stage 1 finishes on both sides before Stage 2. Lifecycle runs before packages. Passes run in profile order (§4.2). Only committed groups are diffed. The core reads no file paths; callers pass sources and a profile.

### 3.2 Duplicates

One rule covers duplicate rows and duplicate lifecycle events. Two rows are duplicates when they share a source and `RowKey`, or, for events, a scope and `EventId`. They are **identical** when every mapped raw value is equal (text ordinally, numbers as `decimal`).

| Case | Handling | Accounting |
|---|---|---|
| Identical duplicates | Merged into one item. Warning `DuplicateRowCollapsed`. | Every copy is a member of the item and takes the item's final disposition. |
| Conflicting duplicates | Every copy quarantined: `DuplicateConflict`. | Every copy is `Quarantined`. |
| A conflicting copy is a lifecycle event | The whole chain is quarantined (§5.1). | Every chain row is `Quarantined`. |

Merging needs no representative copy and no tie-break, so the result does not depend on which copy came first.

### 3.3 Stage 1: normalize

Rules run in four phases, **Identify → Parse → Derive → Validate**, and inside a phase in profile order. Each rule is one class in one file; rules that apply to some rows only carry a `when` condition in the profile, so there are no if-chains on asset class.

| Phase | Reads | Trade rules, in order |
|---|---|---|
| Identify | Raw text and reference data only | `party.resolveLei`, `party.resolveAlias`, `instrument.resolve`, `tradeRef.classify`, `symbol.map` (`Product_Type` → `AssetClass`) |
| Parse | Raw text and Identify outputs | `decimal.parse`, `date.parse` (format chosen by the resolved `Counterparty`), `price.from32nds` |
| Derive | Earlier canonical fields | `side.resolve` (needs `AssetClass`), `quantity.absolute`, `price.dirtyToClean`, `fees.inferFromNet` (needs `Side`, `Quantity`, `Price`, `Accrued`, `NetCash`), `cash.toPerspective` |
| Validate | Finished row | `required.present`, `side.determined` |

Every rule declares the canonical fields it reads and writes. `Reconciler.Compile` rejects a profile in which a rule reads a canonical field that no earlier rule writes (`FieldReadBeforeWrite`), or in which two rules write one field under conditions that can both hold, unless the later rule also reads it as a transform, as `price.dirtyToClean` does with the parsed price (`AmbiguousWrite`). Raw columns may be read by any rule.

Rows that carry only a lifecycle event (a `CANC`) skip the trade rules. A cancellation does not have to be a valid trade.

**Side.** Map the code, reconcile it with the quantity sign, then invert **once** for dealer perspective.

| Code after mapping | Quantity | Result | Real row |
|---|---|---|---|
| `BUY` (`B`, `BOT`, `buy`) | + | `BUY` | `GS-30895386`: `B`, +42,500 |
| `SELL` (`S`, `SLD`, `sell`) | + or − | `SELL`; a negative sign is redundant, never a second inversion | `UB-96731052`: `S`, −4,750,000 |
| `BUY` or `REC_FIXED` | − | `Invalid`, `SideSignConflict` (Q3) | none in the data |
| blank | − | `SELL`, or `PAY_FIXED` for a swap | `CS-36252460`: −9,750,000; `JP-32003590`: swap, −13,000,000 |
| blank | + | `BUY`, or `REC_FIXED` for a swap | `JP-31861549`: +4,750,000 |
| blank | 0 | `Invalid`, `UndeterminedSide` | none in the data |

`PAY_FIXED` (`P`, `PAY`, `pay`) follows the `SELL` rows and `REC_FIXED` (`R`, `REC`, `rec`) the `BUY` rows.

Then, for `DEALER_BILATERAL_VIEW`, swap `BUY`↔`SELL` and `PAY_FIXED`↔`REC_FIXED`. `BC-51721586` (BARC) reads `S` → `SELL` → `BUY`, matching internal `OMS3008502`. `UB-12158471` reads `P` with −76,000,000 → `PAY_FIXED` → `REC_FIXED`, matching `OMS6232584`.

**Dates.** The format comes from reference data, keyed by counterparty; formats are never guessed. Sonnet's BARC row `BC-51721586` has `Value_Settle_Date = 11/09/2025`, which is 2025-09-11 under `dd/MM/yyyy`; JPM's `MM/dd/yyyy` would read 2025-11-09.

**Prices.**

| Convention | Rule | Real row | Raw → canonical |
|---|---|---|---|
| `32NDS_UST` | `W-TT[+]` = W + TT/32 (+ 1/64); TT ∈ 00..31 | `BA-18777889` | `96-02+` → 96.078125 |
| `DIRTY_PCT_PAR` | dirty − accrued / quantity × 100, scale 6 | `CG-51174451` | 100.213333, accrued 54,687.50, qty 3,750,000 → 98.755000 |

Malformed text such as `98-1X` is `Invalid` (`PriceParse`), not a price break.

**Fees.** The street feed has no commission column. `fees.inferFromNet` derives it where the profile defines principal (bond: quantity × price / 100; equity: quantity × price; FX forward: quantity × rate): for `BUY`/`PAY_FIXED`, fees = net − principal − accrued; for `SELL`/`REC_FIXED`, fees = principal + accrued − net. Principal is rounded with the profile's midpoint rule (§7.3), and the value is flagged `Derived`. Sonnet's single-row swaps have no principal formula (principal is not a fixed function of notional and rate), so their street fee is left null; §7.4 covers them.

### 3.4 Diagnostics

| Code | Stage | Severity |
|---|---|---|
| `SourceSchemaError` | 0 | Fatal: fail the run |
| `DuplicateRowCollapsed` | 0, 2 | Warning |
| `DuplicateConflict` | 0, 2 | Invalid |
| `DecimalParse`, `DateParse`, `PriceParse`, `SideSignConflict`, `UndeterminedSide`, `UnknownParty` | 1 | Invalid |
| `UnresolvedInstrument` | 1 | Warning |
| `MissingLifecycleTarget`, `LifecycleCycle`, `LifecycleConflict`, `ChainMemberQuarantined` | 2 | Invalid |
| `IncompletePackage`, `PackageInconsistent` | 2 | Invalid |
| `SearchIncomplete` (`BlockTooLarge`, `BudgetExhausted`) | 4 | Disposition, not a validity problem (§6.2) |

`Invalid` always means `Quarantined`. A profile may raise a warning to `Invalid` but cannot lower `Invalid`. Quarantined rows never enter an index and always appear in the result.

---

## 4. Cascading matcher

### 4.1 Pass contract

A pass sees only unconsumed items. Its index maps each interned key to the items that hold it, is built from unconsumed items, and never scans the other side; an item reached through several keys counts once. The pass builds candidates from that index, evaluates constraints, proposes 1:1 pairs or N:1 groups, and hands them to the orchestrator, which commits a proposal atomically or not at all.

```csharp
public interface IMatchPass
{
    string PassId { get; }
    PassCardinality Cardinality { get; }                 // OneToOne, ManyToOne, OneToMany
    void Propose(in PassContext context, ProposalSink sink);
}
```

Each pass leaves a **verdict** on every item it examined: `Committed`, `NoKey`, `NoCandidate`, `Vetoed`, `Contest` or `SearchIncomplete`. Verdicts are history for the explanation (§6.4). They are not dispositions; §6.2 decides those once, at the end.

### 4.2 Pass precedence

The pass keys are **not nested**. P1 keys on a shared reference that no later key contains; P3's block adds `Price`, which P2's key lacks; P4's key is a subset of P2's key and of P3's block, but that relation is not what orders the passes. Precedence therefore rests on the evidence each commit uses, by two rules:

1. **Shared identifiers first.** A shared reference is asserted by both parties for one trade, so P1 commits even when the economics disagree; the disagreement becomes a field break. Fixture v1 pairs `L-B9A59A` with `R-F39622` on `EX-8421175503` although one says `Buy` and the other `Sell`; the oracle expects a matched pair with a `Direction` break.
2. **Among business-key passes, a pass that tolerates disagreement on a field runs after every pass that requires agreement on it.** P1 is exempt: it keys on no business field and tolerates every economic disagreement, because its evidence is the shared identifier (rule 1). P4 accepts a quantity difference, so it runs after P2 (equal quantity) and P3 (conserved sum). Run earlier, it could take an allocation row that P3 needs, or pair a partial fill with a row that an exact pair needs.

Because keys are not nested, a contest under one key says nothing about the residue under another. Ambiguity is decided on the final residue (§4.5), never from history.

### 4.3 Identifier veto

In P2, P3 and P4, a pair is not a candidate when both items carry a trade reference in the profile's **shared** scheme (§2.3) and the references differ: two different shared references are two different trades.

Fixture 2.0, internal orphan `OMS-46A660444328E3B2` and alleged street row `ST-A4351DFEFAD06334`: both GSCO, `US9900000020`, `SELL`, 2026-11-02, but quantity 27,000,000 vs 27,900,000 and references `EX-1A904B08BDA71E36` vs `EX-9637B58A037EBDFE`. A reduced key pairs them as a quantity break; the oracle says they are an orphan and an alleged trade. The veto keeps them apart. On the Sonnet data it changes nothing.

### 4.4 Cascades

| Pass | Key (exact canonical values) | Commits when |
|---|---|---|
| **P1** `match.sharedRef` | `(Counterparty, TradeRef)`, shared scheme only | the key holds exactly one item on each side |
| **P2** `match.naturalKey` | `(Counterparty, Instrument, AssetClass, TradeDate, Side, Quantity)` | unique on both sides, not vetoed |
| **P3** `match.groupSum` | block `(Counterparty, Instrument, TradeDate, Side, Price)`; target = one item on the one side | a proven-unique admissible subset conserves `Quantity` and `NetCash` (§4.7) |
| **P4** `match.reducedKey` | `(Counterparty, Instrument, TradeDate, Side)` | unique on both sides, not vetoed |

Profiles choose which passes they run (§9.2). Results of the analysis script (§12.1):

| | Sonnet (P1–P4) | Fixture 2.0 (P1, P2) | Fixture v1 (P1, P2) |
|---|---|---|---|
| P1 | 86 | 123 | 142 |
| P2 | 81 | 60 | 28 |
| P3 | 12 | | |
| P4 | 4 | | |
| Residue | 85 left-only, 7 right-only | 85 left-only, 7 right-only | 110 left-only, 20 right-only, 30 rows in 10 ambiguous contests |
| Against the oracle | 275/275 groups, 0 wrong pairs | 275/275 cases, 0 wrong pairs | 310/310 cases, 0 wrong pairs |

**P4 is per profile.** Sonnet has partial fills with no shared reference (`G0144` and three others), which only a reduced key can align. Fixture 2.0 and v1 put quantity in their fallback identity and keep a shared reference on every break pair. Adding P4 to the v1 profile would put 90 single-sided rows (76 internal-only, 14 counterparty-only) into reduced-key contests and report them `Ambiguous`, against the oracle. A profile includes a reduced-key pass only when its data contract allows quantity disagreement without a shared reference.

Worked Sonnet samples:

| Group | Rows | P1 | P2 | P3 | P4 |
|---|---|---|---|---|---|
| `G0011` (32nds) | `OMS4152729` ↔ `BA-18777889` | `NoKey`: `BAX8101307164` is a broker reference | **commit** `(BAML, US91282CT687, Bond, 2025-06-10, SELL, 3000000.00)` | | |
| `G0159` (3:1) | `OMS4044414`, `OMS7849940`, `OMS4003839` ↔ `JP-48051575` | `Contest`: 3 left, 1 right on UTI `…7CB88FD742`; deferred | `NoCandidate`: 9,000 vs 2,200 / 5,400 / 1,400 | **commit** (below) | |
| `G0144` (80% fill) | `OMS3512650` ↔ `CG-43060951` | `NoKey`: broker reference `CGX1302998639` | `NoCandidate`: 1,000,000 vs 800,000 | `NoCandidate`: block has one left item | **commit** `(CITI, UST77KS39V09, 2025-06-12, SELL)` |
| `G0171` (legs) | `OMS9369884` + `OMS9929303` ↔ `JP-32003590` | **commit**: Stage 2 already made the two legs one item | | | |

P3 for `G0159` (block `JPM, US00Q8EY5J09, 2025-06-11, BUY, 114.000000`):

| Subset | Σ Quantity | Σ NetCash | Conserves 9,000 / 1,026,045.00 |
|---|---|---|---|
| 2,200 + 5,400 | 7,600 | 866,438.00 | no |
| 2,200 + 1,400 | 3,600 | 410,418.00 | no |
| 5,400 + 1,400 | 6,800 | 775,234.00 | no |
| 2,200 + 5,400 + 1,400 | 9,000 | 1,026,045.00 | **yes; search complete, the only solution** |

### 4.5 Deferral and re-evaluation

- A 1:1 pass that finds items on both sides of a key and more than one on either side **defers** the key. Nothing is consumed.
- After any pass commits, every earlier pass re-evaluates, on the current residue, the keys and blocks whose membership changed, in pass order, until nothing more commits. Then the next pass runs. A deferred key that has become 1:1 is committed by the pass that deferred it.
- After the last pass, contests are recomputed **on the residue only** (§6.2). A row that was once in a multiplicity whose rivals were later consumed is not `Ambiguous`.

Constructed example (not in the data; checked with the analysis script): add left row A, JPM `BUY` 2,200 DIS (`US00Q8EY5J09`) on 2025-06-11 at 113.95, and right row S with the same terms. P2's key `(JPM, US00Q8EY5J09, Equity, 2025-06-11, BUY, 2200)` holds `OMS4044414` and A against S, so P2 defers. P3 commits `G0159`, consuming `OMS4044414`; A's price keeps it out of that block. Re-evaluation finds the key 1:1, and P2 commits A ↔ S.

### 4.6 Contests and ties

A **contest** is either a key with eligible (not vetoed) items on both sides and more than one on some side, or a group component whose search ends in contest (§4.7). Contests are never broken by row order, `RowKey`, ordinal or hash order. A profile may declare an evidence-based preference (for example equal `SettleDate`), which is then recorded in the explanation; the acceptance profiles declare none.

Fixture v1 `T-298F87`: `L-FAFDE5` and `L-257F61` against `R-74AAC1`, identical on every field (Cedar Securities Ltd, EUR/USD, 2026-11-02, Buy, 2,780,000 at 1.093). The contest survives every pass, and the three rows are reported as one `Ambiguous` entry, which is the oracle's answer.

Commit is atomic: claims are one bitset per side, and a proposal commits only if all its items are unclaimed. Proposals are grouped into components (items linked by shared candidates). Components are disjoint, so they may be resolved in parallel; commits happen in a fixed order that affects output order only.

### 4.7 Bounded subset search

**Terms.** A *block* is the many-side items and the one-side targets that share P3's block key. An item is *eligible* for a target when no constraint excludes the pair: the identifier veto (§4.3) and any constraint the profile adds. An *admissible* subset for a target has 2 to `MaxGroupSize` items, all eligible for it; `MaxGroupSize` defines what counts as a group and is not a budget. A subset *conserves* when every conserved field (`Quantity`, `NetCash` and any other the profile lists) sums to the target's value exactly at field scale, or within a profile sum tolerance (§7.6).

**Per-target search.** Each target is searched on its own: over its own eligible items, with its own node budget. Within that space, items with equal values in **every** conserved field form one class. Because only eligible items are in the space, the members of a class are interchangeable for this target, and the search chooses how many to take from each class (*k* of *n*), never which. Two items with equal cash and quantity but different eligibility are never in one class: an item vetoed for the target is not in its space at all. Classes are ordered by conserved values (`Quantity` descending, then the other conserved fields in profile order). Pruning is limited to sound cuts on the non-negative `Quantity`; other conserved fields may be signed and are checked only on complete subsets. Budgets count nodes, not time; time limits belong to the caller's `CancellationToken`.

Each target ends in one state:

| State | Meaning |
|---|---|
| `None` | Search complete; no admissible subset conserves. |
| `Unique` | Search complete; exactly one choice of class counts conserves, and it takes each class wholly or not at all. |
| `Contest` | Two conserving choices found, or one that takes part of a class (the other members conserve equally). |
| `Incomplete` | The node budget ran out before the search completed or found a second solution, **including after one solution was found**. |

**Coupled targets.** Two targets in a block are coupled when some item is eligible for both; coupling is transitive and forms components. Each component is decided as a whole, so an unfinished search for one target can never be overtaken by a commit for another that might need the same items:

| Component | Outcome |
|---|---|
| Every target `None` or `Unique`, and the `Unique` solutions pairwise disjoint | Each `Unique` target commits its subset; `None` targets record `NoCandidate`. |
| Any target `Contest`, or two `Unique` solutions share an item | Nothing commits. The targets and every item of a class that a found solution draws from are a contest; other eligible items of an `Incomplete` target in the component are `SearchIncomplete`. |
| Otherwise: some target `Incomplete`, no contest | Nothing commits. The targets and all their eligible items are `SearchIncomplete` (`BudgetExhausted`); found solutions are reported as candidates. |
| More than `MaxBlockItems` eligible items or `MaxTargetsPerComponent` targets | No search; `SearchIncomplete` (`BlockTooLarge`). |

Targets that share no eligible item do not block each other.

**Determinism.** A target's state depends only on the values of its eligible items. A component's outcome depends only on its targets' states and solution item sets. Budgets are per target and never shared, so one target cannot exhaust another's. The order in which targets and components are processed (by conserved values, for output) therefore never changes a result, and searches may run in parallel.

| Limit | Default |
|---|---|
| `MaxGroupSize` | 8 |
| `MaxBlockItems` | 24 eligible items per component |
| `MaxTargetsPerComponent` | 8 |
| `MaxNodesPerTarget` | 250,000 search nodes |
| `StopAfterSolutions` | 2 per target |

All 12 Sonnet blocks that P3 searches have one target and at most 3 items, so multi-target components are covered by synthetic fixtures (§12.4).

Constructed examples (checked with the analysis script):

- Add an internal `BUY` of 2,200 DIS at 114 with net 250,811.00 and no shared reference: a twin of `OMS4044414`, which carries the target's UTI. Neither is vetoed, so both sit in one class for target `JP-48051575`, and the only conserving choice takes one of the two: a contest. The `G0159` rows, the twin and the street block end `Ambiguous`; P3 commits the other 11 groups.
- Give the same twin a different shared-scheme UTI instead. The veto removes it from the target's space, the class holds only `OMS4044414`, and `G0159` commits; the twin ends `LeftOnly`.

---

## 5. Lifecycle and packages

### 5.1 Lifecycle

`lifecycle.newCancCorr` builds a reference graph per side from each event's target, scoped by counterparty. Row order is irrelevant. For each chain:

1. Every target must exist, otherwise the chain is quarantined (`MissingLifecycleTarget`); a cycle gives `LifecycleCycle`.
2. A `CANC` cancels its target version.
3. A `CORR` supersedes its predecessor; if it points at a `CANC`, the predecessor is the version that `CANC` cancelled.
4. One surviving version becomes an item. None means every row is `Suppressed` (`FullyCancelled`). More than one is ordered by a profile `Sequence` field if there is one, otherwise the chain is quarantined (`LifecycleConflict`).
5. If any chain row is quarantined (for example by `DuplicateConflict`), every row of the chain is quarantined (`ChainMemberQuarantined`).

Non-surviving rows are `Suppressed`: `Superseded` (an older version) or `CancelEvent` (a `CANC` row). Sonnet `G0179`:

| Report | Action | Target | Price | Net cash | Disposition |
|---|---|---|---|---|---|
| `CS-39633954` | `NEW` | | 102.604 | 5,485,752.71 | `Suppressed` (`Superseded`) |
| `CS-74239776` | `CANC` | `CS-39633954` | 102.604 | 5,485,752.71 | `Suppressed` (`CancelEvent`) |
| `CS-36468090` | `CORR` | `CS-74239776` | 105.149 | 5,619,365.21 | Survivor; P1 matches it to `OMS9063540` |

The link convention and the `CANC` payload differ between datasets; the profile sets both (§9.2). Lifecycle runs on either side; the internal tabs here have no event columns.

### 5.2 Packages: known membership

A package rule groups rows on a link field, checks required roles, and builds one item whose fields come from per-field aggregators. Each aggregator is one type: `Same` (members must agree, else `PackageInconsistent`), `Sum`, `FromRole(role)`, `DifferenceByRole(a, b)`, `AbsOf(inner)`. A missing required role quarantines the package (`IncompletePackage`). Member rows stay as lineage.

Sonnet `G0171` (`Parent_Link_ID = PLK-158918`):

| Field | `OMS9369884` `LEG_1_PAY` | `OMS9929303` `LEG_2_RECEIVE` | Aggregator | Item | Street `JP-32003590` |
|---|---|---|---|---|---|
| Side | `PAY_FIXED` | `REC_FIXED` | `FromRole(LEG_1_PAY)` | `PAY_FIXED` | blank, −13,000,000 → `PAY_FIXED` |
| Quantity | 13,000,000 | 13,000,000 | `Same` | 13,000,000 | 13,000,000 |
| Price | 4.268 | 3.9645 | `FromRole(LEG_1_PAY)` | 4.268 | 4.268 |
| NetCash, Accrued | 278,961.22 | 259,124.12 | `DifferenceByRole(LEG_1_PAY, LEG_2_RECEIVE)` | 19,837.10 | 19,837.10 |

Aggregators by profile are in §9.2.

### 5.3 Known versus discovered membership

| | Package (Stage 2) | Group pass P3 (Stage 4) |
|---|---|---|
| Membership from | A link field | A proven-unique conserving subset |
| Sonnet allocations | Not possible: no link, and 6 of the 12 groups (`G0160`, `G0163`, `G0164`, `G0166`, `G0167`, `G0169`) have no UTI on any internal row | All 12 found |
| Fixture 2.0 allocations | All 12, linked by `BLOCK-…` with `Leg_Structure = ALLOCATION` | Not needed |

Both routes produce the same result shape (`ManyToOne`). A block key that relies only on a shared reference or a link would miss the six unlinked Sonnet groups, so P3 blocks on business fields.

---

## 6. Result model

### 6.1 Four independent axes

| Axis | Applies to | Values |
|---|---|---|
| `Alignment` | Result entry | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly` |
| `Composition` | Matched entry | `OneToOne`, `ManyToOne` or `OneToMany`, with flags `Package`, `LifecycleCollapsed` |
| `InputValidity` | Row | `Valid`, `ValidWithWarnings`, `Invalid` (`Invalid` ⇔ quarantined) |
| `FieldOutcome` | Field of a matched entry | `NotCompared`, `Exact`, `WithinTolerance`, `Break` |

No axis stands in for another. A failed 32nds parse is `Invalid`, not a price `Break`. A search stopped by its budget is `SearchIncomplete`, not evidence that no group exists. A shared-reference match with a busted price is `Matched` with outcome `Break` (`G0139`: 96.572 vs 99.966).

### 6.2 Row dispositions and precedence

Every source row receives exactly one final **disposition**, assigned once in Stage 6. Pass verdicts (§4.1) are history and never decide it. When more than one rule could apply, the first in this list wins:

| Order | Disposition | Assigned when |
|---|---|---|
| 1 | `Quarantined` | The row is `Invalid` (Stages 0–2), including every row of a quarantined chain or package. |
| 2 | `Suppressed` (`Superseded`, `CancelEvent`, `FullyCancelled`) | Stage 2 removed it from matching. |
| 3 | `Matched` | Its item is in a committed group. |
| 4 | `Ambiguous` | Its item is in a contest **recomputed on the final residue** (§4.5, §4.6). |
| 5 | `SearchIncomplete` | Its item is in no residue contest, but a group component containing it ended `SearchIncomplete` in its last evaluation on the residue (§4.5, §4.7). |
| 6 | `LeftOnly` / `RightOnly` | Otherwise. |

Rows merged into one item (identical duplicates, package members, a lifecycle survivor) take that item's disposition. `Ambiguous` ranks above `SearchIncomplete` because a contest is proven by candidates found, while an incomplete search is only missing evidence. Both mean "needs review"; when both apply, the entry carries both diagnostics.

Stage 6 asserts that the dispositions partition the input rows. Sonnet (analysis script, §12.1):

| Disposition | Left | Right |
|---|---|---|
| `Matched` (183 groups) | 215 = 163 single + 16 in 8 packages + 36 in 12 allocation groups | 183 |
| `LeftOnly` / `RightOnly` | 85 | 7 |
| `Suppressed` | | 10 (5 `Superseded`, 5 `CancelEvent`) |
| Total | 300 | 200 |

### 6.3 Roll-up

| Level | Rule |
|---|---|
| Field | Its own outcome (§7). A derived field whose delta is fully explained by root fields takes the roots' worst outcome and lists them in `AttributedTo` (§7.4). |
| Entry | Worst compared field: `Break` > `WithinTolerance` > `Exact`. `NotCompared` fields do not count. Entries that are not `Matched` are `NotCompared`. |
| Run | Counts by `(Alignment, Outcome)`. Breaks are counted by **root** field, so one quantity break is not also counted under accrued and net cash. |

Sonnet with the §9.2 profile: 145 `Exact` (120 single + 5 chains + 12 allocations + 8 packages), 18 `WithinTolerance`, 20 `Break` (5 each rooted in `Price`, `Quantity`, `SettleDate`, `Fees`), 85 `LeftOnly`, 7 `RightOnly`.

### 6.4 Explanations

```csharp
public sealed record Explanation(string? CommittedBy, IReadOnlyList<PassAttempt> Trail, IReadOnlyList<Hint> Hints);
public sealed record PassAttempt(string PassId, PassVerdict Verdict, string? Key, IReadOnlyList<SourceRef> Candidates, string? Detail);
```

`G0144` (80% fill), abridged:

```json
{
  "alignment": "Matched", "composition": "OneToOne", "outcome": "Break",
  "left": ["OMS3512650"], "right": ["CG-43060951"],
  "diffs": [
    { "field": "Quantity", "left": "1000000.00", "right": "800000.00", "delta": "-200000.00", "outcome": "Break" },
    { "field": "Accrued",  "left": "4784.72", "right": "3827.78", "delta": "-956.94", "outcome": "Break", "attributedTo": ["Quantity"] },
    { "field": "Fees",     "left": "50.00", "right": "40.00", "delta": "-10.00", "derived": true, "outcome": "Break", "attributedTo": ["Quantity"] },
    { "field": "NetCash",  "left": "1028884.72", "right": "823107.78", "delta": "-205776.94", "outcome": "Break", "attributedTo": ["Quantity", "Accrued", "Fees"] }
  ],
  "explanation": { "committedBy": "P4", "trail": [
    { "pass": "P1", "verdict": "NoKey" }, { "pass": "P2", "verdict": "NoCandidate" },
    { "pass": "P3", "verdict": "NoCandidate" }, { "pass": "P4", "verdict": "Committed", "key": "CITI|UST77KS39V09|2025-06-12|SELL" } ] }
}
```

Unmatched entries explain themselves the same way. Orphan `OMS7586315` (CITI, `SELL`, `US4T8CATZT44`, 2025-06-10): P1 `NoKey` (blank `UTI_Exec_ID`), P2–P4 `NoCandidate`; hint `NearestCandidate`: `MS-58715991` shares the instrument but its counterparty is MSCO.

### 6.5 Oracle outcomes

Each oracle file gets a test adapter (§12.2) that maps its vocabulary onto the axes:

| Oracle outcomes (all files) | Alignment | Composition | Outcome |
|---|---|---|---|
| `EXACT_MATCH_AFTER_NORMALIZATION`, `MATCH_EXACT_AFTER_NORMALIZATION`, `MATCHED_EXACT`, `EXACT`, `NORMALIZED` | `Matched` | `OneToOne` | `Exact` |
| `MATCH_WITHIN_TOLERANCE`, `MATCHED_WITHIN_TOLERANCE` | `Matched` | `OneToOne` | `WithinTolerance` |
| `BREAK_1_TO_1`, `MATCH_WITH_FIELD_BREAKS`, `MATCHED_WITH_ECONOMIC_BREAK`, `BREAK`, `FIELD_BREAK` | `Matched` | `OneToOne` | `Break`, named field as root |
| `MANY_TO_ONE_MATCH`, `MATCHED_N_TO_1`, `NETTED_ALLOCATION_MATCH`, `MATCH_AGGREGATED` (allocations) | `Matched` | `ManyToOne`, plus `Package` when linked (fixture 2.0) | `Exact` |
| `TWO_TO_ONE_NET_MATCH`, `MATCHED_MULTI_LEG`, `MULTI_LEG_PACKAGE_MATCH`, `MATCH_AGGREGATED` (swaps) | `Matched` | `ManyToOne` + `Package` | `Exact` |
| `CHAIN_COLLAPSES_TO_EXACT_MATCH`, `MATCH_AFTER_LIFECYCLE_COLLAPSE`, `MATCHED_AFTER_LIFECYCLE_COLLAPSE`, `EXACT_MATCH_AFTER_LIFECYCLE_COLLAPSE` | `Matched` | `OneToOne` + `LifecycleCollapsed` | `Exact`; other chain rows `Suppressed` |
| `UNMATCHED_INTERNAL`, `LEFT_ONLY_UNMATCHED`, `INTERNAL_ONLY` | `LeftOnly` | | `NotCompared` |
| `UNMATCHED_COUNTERPARTY`, `RIGHT_ONLY_ALLEGED`, `COUNTERPARTY_ONLY` | `RightOnly` | | `NotCompared` |
| `AMBIGUOUS` (fixture v1 only, 10 cases; 2 left, 1 right each) | `Ambiguous` | | `NotCompared` |

No oracle covers quarantine, lifecycle failures, incomplete packages, subset contests or search limits; §12.4 adds synthetic fixtures for them.

### 6.6 Side-by-side view

The wiki's Excel pattern, where each cell holds our value and theirs with totals, is a **two-sided pivot**: a cross-tab of (left, right, delta) cells summed along chosen dimensions. It is summary-level, control-total reconciliation. The library builds it from a finished result, so each cell's delta also splits into its causes.

```csharp
public sealed record SideBySideSpec(IReadOnlyList<string> Rows, IReadOnlyList<string> Columns, IReadOnlyList<MeasureSpec> Measures, FilterSpec? Filter);
```

USD bond face by counterparty, Sonnet, after lifecycle collapse (analysis script):

| Counterparty | Rows (ours \| theirs) | Face (ours \| theirs) | Delta | = left-only | + right-only | + matched deltas |
|---|---|---|---|---|---|---|
| CITI | 13 \| 8 | 67,750,000 \| 52,800,000 | −14,950,000 | −20,000,000 | +5,250,000 | −200,000 (`G0144`) |
| MSCO | 9 \| 4 | 38,500,000 \| 8,500,000 | −30,000,000 | −30,500,000 | +500,000 | 0 |
| All 8 | 109 \| 69 | 550,250,000 \| 393,700,000 | −156,550,000 | | | |

Uses: a one-page report of a run; triage by largest cell delta; and, run on raw feeds before matching, a completeness check (a cell off by a whole file points at a missing feed). Only additive measures sum; prices and dates do not. `Suppressed` rows are excluded. Rendering to `.xlsx` is outside the DLL.

---

## 7. Tolerance contract

### 7.1 Evaluation order for one field

1. **Applicability.** If the field does not apply (§7.5), `NotCompared`.
2. **Nulls** (§7.5).
3. **Round** both values to the field's scale with the profile's midpoint rule (§7.3).
4. Equal → `Exact`. Tolerance is never consulted for equal values.
5. **Delta** = right − left on the rounded values.
6. Inside the field's tolerance → `WithinTolerance`, keeping the exact delta.
7. A field with an attribution formula that fully explains its delta → the roots' worst outcome (§7.4).
8. Otherwise `Break`.

### 7.2 Tolerance kinds

| Kind | Passes when | Use |
|---|---|---|
| `Exact` | delta = 0 | Default for every field |
| `Absolute(a)` | \|d\| ≤ a | `Accrued`, FX `Price`, `Fees` |
| `Relative(r)` | \|d\| ≤ r × max(\|left\|, \|right\|); fails if that maximum is 0 | large notionals |
| `AbsoluteOrRelative(a, r)` | either holds | |
| `Days(n)` | \|calendar-day difference\| ≤ n | off in all profiles |
| `Attributed(formula)` | the delta equals the formula's delta exactly | `NetCash`; `Accrued` and `Fees` pro rata to `Quantity` |

Bounds are **inclusive**. Sonnet `G0127` moves an FX rate from 1.2705 to 1.2706, a delta of exactly +0.0001, and the oracle calls it within a 0.0001 tolerance. `Symbol`, `Identifier` and `Text` fields are only `Exact` or `Break`. Tolerances are symmetric. Tolerance equality is not transitive (1.2705 ≈ 1.2706 ≈ 1.2707, but 1.2705 and 1.2707 are 0.0002 apart), so tolerances never enter keys or hashes; they are used only in Stage 5 and in P3's optional sum tolerance.

### 7.3 Rounding

Every `Decimal` field has a scale; every profile names a midpoint rule; values are rounded before comparison. The rule is per profile because the datasets differ, and getting it wrong creates false differences:

| Dataset | Rule | Evidence |
|---|---|---|
| Sonnet | `ToEven` | `OMS7091414`: 5,500,000 × 102.234375 / 100 = 5,622,890.625, stored as 5,622,890.62. Under `AwayFromZero`, the inferred street fee on this exact pair becomes 550.01 instead of 550.00; the same happens on `OMS3919827` (7,503,515.625). |
| Fixture 2.0 | `AwayFromZero` | Its rule: "Amounts: 2 decimals, midpoint away from zero". |

`double` cells (Excel) convert through the shortest round-trip string, then round to scale, so binary noise never creates a delta. Trade scales: `Quantity` 2, `Price` 6, `Accrued`/`Fees`/`NetCash` 2, `Coupon` 6.

### 7.4 Derived fields and attribution

Net cash is a function of quantity, price, accrued and fees. A blanket cash tolerance would hide real breaks; no tolerance would flag every in-tolerance rate move twice. The fixture rule is that the cash difference "must exactly reconcile" to the root delta. So a derived field passes only if its formula, applied to the right side's roots, reproduces its delta to the cent.

| Group | Root delta | Derived delta | Check | Outcome |
|---|---|---|---|---|
| `G0127` | `Price` +0.0001, within | `NetCash` +100.00 | 1,000,000 × 0.0001 | `WithinTolerance` |
| `G0125` | `Accrued` −0.94, within 1.25 | `NetCash` −0.94 | one-for-one | `WithinTolerance` |
| `G0136` | `Fees` (derived) 67.50 → 67.48 | `NetCash` +0.02 | `SELL`: net = principal + accrued − fees | `WithinTolerance` |
| `G0157` | `Fees` (derived) 41.10 → 491.10 | `NetCash` −450.00 | as above | `Break`, root `Fees` |
| `G0144` | `Quantity` 1,000,000 → 800,000 | `Accrued` −956.94, `Fees` −10.00, `NetCash` −205,776.94 | accrued and fees pro rata (4,784.72 × 0.8; 50.00 × 0.8); net = −204,830.00 − 956.94 + 10.00 | `Break`, root `Quantity` |

If the formula does not reproduce the delta, the derived field is a `Break` in its own right.

Where the street fee cannot be derived (§3.3), `trading.netCash` reports a net-cash delta as a derived `Fees` difference only when quantity, price and accrued all agree, since principal must then agree too. Sonnet `G0158`, a single-row swap: the +450.00 net-cash delta becomes `Fees` 450.00 → 900.00, root `Fees`, as the oracle states.

### 7.5 Nulls and applicability

| Rule | Meaning |
|---|---|
| Both null | `Exact` |
| One null | `Break` (`MissingLeft` / `MissingRight`) |
| Null as zero | Never by default; a profile must name the field. Never for prices. |
| `applicableWhen` | A condition on the row; otherwise `NotCompared` |

**Conditions on a pair.** An `applicableWhen` condition holds if it holds on **either** side; a tolerance's `when` holds only if it holds on **both**. Disagreement therefore never hides a field or widens a tolerance. If one side says `Bond` and the other `Equity` (possible after P1, P3 or P4, whose keys do not include `AssetClass`), `Accrued` is compared, the FX-rate tolerance does not apply, and `AssetClass` itself is a `Break`.

Applicability is what the data needs: an internal equity has `Accrued_Interest = 0`, the street equity row leaves `Reported_Accrued_Int` blank, and both mean "does not apply". The trade profiles compare `Accrued` and `Coupon` only for `Bond` and `Swap`, and `Fees` only where the street fee can be derived (§3.3).

### 7.6 Dates and aggregates

Dates compare `Exact`. `G0149` shows why: settle 2025-06-13 vs 2025-06-16 is three calendar days but one business day (Friday to Monday), and the oracle calls it a break. Holiday calendars are out of scope. Groups and packages compare their aggregated projection with the same field tolerances. P3's conservation sums are exact at field scale unless the profile sets a sum tolerance; sums use the individually rounded member amounts, never a re-rounded block estimate.

Per-profile tolerance values are in §9.2.

---

## 8. Diff engine

A `DiffPlan` is built once per compiled profile: an array of typed steps, each binding one field's column pair to its comparer, tolerance and applicability. A step reads `ReadOnlySpan<decimal>`, `ReadOnlySpan<DateOnly>` or `ReadOnlySpan<int>` directly, so the plan needs no reflection and no runtime code generation. Plans belong to the immutable `CompiledProfile`; there is no static cache.

```csharp
public interface IFieldComparer<T> where T : unmanaged
{
    FieldOutcome Compare(in T left, in T right, out Delta delta);
}
public readonly record struct CanonicalValue(FieldType Type, bool IsNull, decimal Number, DateOnly Date, int Interned);  // one typed slot is used
public readonly record struct Delta(DeltaKind Kind, decimal Number, int Days);
public readonly record struct FieldDiff(FieldId Field, FieldOutcome Outcome, CanonicalValue Left, CanonicalValue Right,
    Delta Delta, ToleranceId Tolerance, AttributionId Attribution);
```

| Path | Guarantee |
|---|---|
| Field comparison and delta | No boxing, no allocation: 0 B per exact pair (required by AC-12) |
| Recording a non-exact field | No boxing: a `FieldDiff` struct written to a pooled per-run buffer |
| Index, result objects, explanations, JSON | Allocate, off the hot path |

"Zero boxing" means the comparison path, not the whole run. `FieldDiff` carries both canonical values as `CanonicalValue`, a union struct, so recording a difference with its values still does not box; for an aggregated group the values are those of the aggregate projection (`Projection`, §11).

**Projection from caller types.** Typed objects enter through `SourceMap<T>`, built from caller-written lambdas (`map.Decimal("Quantity_Notional", t => t.Quantity)`). The C# compiler compiles those, so projection needs no reflection, expression trees or source generator. Tabular sources resolve column ordinals once per source.

**AOT and trimming.** `RecDiffy.dll` is `IsAotCompatible` and `IsTrimmable` and must publish without warnings. Profiles name rules from a closed registry; JSON never names a CLR type and nothing is compiled from JSON. `System.Text.Json` uses source-generated contexts.

Not compared: `Identity`, `Lifecycle`, `Package` and `Text` fields. `TradeRef`, `Portfolio_Book` and `Security_Text` (which the Sonnet street feed truncates, for example `CITIGROUP INC 7.000% DUE 11/07/2`) never produce breaks.

---

## 9. Configuration and profiles

### 9.1 Profile document

A profile is JSON. Rule, pass, aggregator and tolerance names resolve against a closed `RuleRegistry`, so a document configures registered behavior and cannot inject code. Sonnet profile, abridged:

```json
{
  "formatVersion": 1,
  "profileId": "trade-recon.sonnet",
  "version": "1.0.0",
  "rulePacks": ["trading@1"],
  "rounding": { "midpoint": "ToEven" },
  "sources": { "left": { "rowKey": "Internal_Trade_ID" }, "right": { "rowKey": "Cpty_Report_ID" } },
  "normalization": { "right": {
    "identify": [
      { "rule": "party.resolveLei", "from": "Counterparty_LEI", "to": "Counterparty" },
      { "rule": "tradeRef.classify", "from": "Shared_Market_Exec_Ref", "to": "TradeRef", "shared": "lei30" } ],
    "parse": [
      { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formatBy": "Counterparty" },
      { "rule": "price.from32nds", "from": "Exec_Price_Rate", "to": "Price",
        "when": { "field": "Price_Quote_Convention", "equals": "32NDS_UST" } } ],
    "derive": [
      { "rule": "side.resolve", "code": "Dir_Ind", "quantity": "Reported_Qty_Notional", "invertWhen": "DEALER_BILATERAL_VIEW" },
      { "rule": "fees.inferFromNet", "to": "Fees" } ] } },
  "consolidation": {
    "right": { "lifecycle": { "rule": "lifecycle.newCancCorr", "corrTarget": "CANC" } },
    "left":  { "packages": [ { "rule": "package.byLink", "link": "Parent_Link_ID", "role": "Leg_Structure",
               "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"],
               "aggregate": { "Quantity": "Same", "Price": "FromRole(LEG_1_PAY)", "Side": "FromRole(LEG_1_PAY)",
                              "NetCash": "DifferenceByRole(LEG_1_PAY,LEG_2_RECEIVE)",
                              "Accrued": "DifferenceByRole(LEG_1_PAY,LEG_2_RECEIVE)" } } ] } },
  "paths": [ { "id": "trade", "route": { "rule": "route.all" },
    "passes": [
      { "id": "P1", "pass": "match.sharedRef" },
      { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "AssetClass", "TradeDate", "Side", "Quantity"] },
      { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"],
        "conserve": ["Quantity", "NetCash"] },
      { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] } ],
    "diff": { "default": "exact", "fields": {
      "Accrued": { "tolerance": { "absolute": 1.25 }, "attributed": "trading.proRataToQuantity",
                   "applicableWhen": { "field": "AssetClass", "in": ["Bond", "Swap"] } },
      "Price":   { "tolerance": { "absolute": 0.0001, "when": { "field": "AssetClass", "equals": "Forward" } } },
      "Fees":    { "tolerance": { "absolute": 0.05 }, "attributed": "trading.proRataToQuantity",
                   "applicableWhen": { "field": "AssetClass", "in": ["Bond", "Equity", "Forward"] } },
      "NetCash": { "tolerance": { "attributed": "trading.netCash" } } } } } ]
}
```

Reference data (aliases, LEIs, per-counterparty date formats, security master) is passed at run time as a `ReferenceDataSet` with an ID, as-of date and content hash. A code builder produces the same document.

### 9.2 Per-profile differences

The acceptance datasets disagree on conventions, so each has its own profile. This table is the single statement of those differences.

| Setting | Sonnet | Fixture 2.0 | Fixture v1 | Opus (not verified) |
|---|---|---|---|---|
| Passes | P1, P2, P3, P4 | P1, P2 | P1, P2 | to be determined |
| Shared reference scheme | 30 characters, first 20 a known LEI; else broker | `EX-…` shared; `EXT-…` broker | `ExecutionRef`, blank allowed | |
| Dates | text, format per counterparty | Excel dates | Excel dates | |
| Lifecycle links | `CANC`→`NEW`, `CORR`→`CANC` | `CANC`→`NEW`, `CORR`→`NEW` | none | |
| `CANC` payload | repeats the `NEW`, unsigned | reverses it (quantity −18,900,000; cash +19,089,000.00) | none | |
| Legs | `Same` quantity; `FromRole(LEG_1_PAY)` price and side; `DifferenceByRole` cash and accrued | `Same` quantity, never doubled; `Sum` of signed cash; side `PAY_FIXED` on both | none | `FX_SWAP_NET`: price = far − near; cash = \|far − near\| |
| Allocations | discovered by P3 | package `Sum` on `BLOCK-…` links | none | |
| Perspective inversion | `DEALER_BILATERAL_VIEW` | `DEALER_BILATERAL_VIEW`, and its cash is negated | `Perspective = Sender` | `DEALER_BILATERAL_VIEW` |
| Cash sign | unsigned | signed (`BUY` negative) | n/a | unsigned |
| Midpoint | `ToEven` | `AwayFromZero` | n/a | |
| Tolerances (accrued / FX rate / fee) | 1.25 / 0.0001 / 0.05 | 1.20 / 0.0001 / 0.01 | all exact | 1.25 / 0.0005 / 0.05 (net cash) |

### 9.3 Validation and versioning

`Reconciler.Compile` rejects a profile before any row is read when a rule, pass or aggregator ID is unknown; a parameter is missing or mistyped; a rule is listed under a phase it does not belong to; a rule reads a field before it is written (`FieldReadBeforeWrite`) or two rules write one field ambiguously (`AmbiguousWrite`, §3.3); a key or tolerance names a field no rule produces; a tolerance kind does not fit the field type; a pass key uses a `Text` field; an aggregator names an undeclared role; route rules overlap; or a P3 limit exceeds its hard cap (`MaxGroupSize` 16).

| Version | Meaning |
|---|---|
| `formatVersion` | JSON shape. v1 accepts `1` only and rejects anything else; no silent upgrade. |
| `version` (semver) | Profile content. Major when keys, passes, rules or tolerances can change outcomes. |
| Content hash (SHA-256 of canonical JSON) | Stamped on every result with the reference-data snapshot ID and hash. |
| Rule IDs (`side.resolve@2`) | A behavior change is a new rule version; old versions stay registered. |

A compiled profile is immutable and shareable. Changing one means editing the document and compiling again.

---

## 10. Determinism, threading, cancellation, observability

### 10.1 Determinism

Same rows (in any order), same profile hash, same reference snapshot and same engine version give the same groups, alignments, outcomes, deltas, dispositions and diagnostic codes. No decision reads `RowKey`, row order, ordinals, `string.GetHashCode`, dictionary order or the clock. Strings compare ordinally; parsing uses the invariant culture.

**ID-scramble invariance.** The scramble applies a bijection to each side's row keys and remaps every field that refers to a row key (`Cancels_Prior_Report_ID`). Evidence identifiers such as UTIs and `Parent_Link_ID` are left unchanged in the main test and renamed by a bijection applied to both sides in a second variant. After mapping IDs back, the results must be identical. This is achievable because:

1. row keys are used only for equality (lifecycle targets), never for ordering or as match evidence (§3.2, §4.6), and the scramble preserves those equalities;
2. each subset search depends only on the values of one target's eligible items, with a budget of its own (§4.7), so a search that completes, or exhausts its budget, does so under any IDs and in any processing order;
3. duplicate handling needs no representative copy (§3.2);
4. components are disjoint, so commit order affects output order only.

`GroupId` is the first 128 bits of SHA-256 over the sorted member `(source, rowKey)` pairs. It is stable across runs of the same data, and it changes under a scramble by design, so tests compare membership through the ID mapping and compare output as sets.

### 10.2 Threading and cancellation

Normalization runs in parallel over rows; consolidation over chains and packages; index build over key partitions; candidate generation over blocks and components; diff over groups. Commits are single-threaded. `RunOptions.MaxDegreeOfParallelism` defaults to the processor count. `CompiledProfile` and `ReconciliationResult` are immutable.

`CancellationToken` is checked between stages and periodically inside them. Cancellation throws `OperationCanceledException` and returns no result. Search budgets are not cancellation: a budget stops one block (§4.7) and the run continues.

### 10.3 Observability

`ActivitySource` and `Meter`, both named `RecDiffy`: spans per run, stage and pass; counters for rows read, rows quarantined by code, items, commits and deferrals by pass, contests, largest block, search nodes, `SearchIncomplete` blocks, field outcomes by field, stage durations. Telemetry carries counts and rule and pass IDs, never field values; row keys only with `RunOptions.TelemetryIncludesRowKeys`. The same counts are in `RunSummary`, so tests assert on the result.

---

## 11. Public API

Signatures only, at the boundaries. `IMatchPass` is in §4.1; `IFieldComparer<T>`, `Delta` and `FieldDiff` are in §8; `Explanation` is in §6.4.

```csharp
namespace RecDiffy;

public static class Reconciler
{
    public static CompiledProfile Compile(ProfileDocument document, RuleRegistry registry);
    public static ReconciliationResult Run(CompiledProfile profile, RecordSource left, RecordSource right,
        ReferenceDataSet referenceData, RunOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<ReconciliationResult> RunAsync(CompiledProfile profile, RecordSource left, RecordSource right,
        ReferenceDataSet referenceData, RunOptions? options = null, CancellationToken cancellationToken = default);
}

public static class RecordSources
{
    public static RecordSource FromObjects<T>(string name, IEnumerable<T> rows, SourceMap<T> map);
    public static RecordSource FromObjects<T>(string name, IAsyncEnumerable<T> rows, SourceMap<T> map);
    public static RecordSource FromDataReader(string name, Func<IDataReader> open, TabularOptions options);
}

public sealed record TabularOptions(int HeaderRowIndex = 0, string? RowKeyColumn = null);
```

```csharp
namespace RecDiffy.Rules;

public enum RulePhase { Identify = 1, Parse = 2, Derive = 3, Validate = 4 }
public interface INormalizationRule
{
    string RuleId { get; }
    RulePhase Phase { get; }
    IReadOnlyList<FieldId> Reads { get; }       // canonical fields; checked by Compile (§3.3)
    IReadOnlyList<FieldId> Writes { get; }
    void Apply(ref RowContext row);
}
public interface ILifecycleRule      { string RuleId { get; } LifecycleResolution Resolve(LifecycleChain chain); }
public interface IFieldAggregator    { string AggregatorId { get; } void Aggregate(FieldId field, ReadOnlySpan<int> members, in AggregationContext context); }
```

```csharp
namespace RecDiffy.Results;

public enum Alignment     { Matched, Ambiguous, SearchIncomplete, LeftOnly, RightOnly }
public enum Disposition   { Matched, Ambiguous, SearchIncomplete, LeftOnly, RightOnly, Quarantined, Suppressed }
public enum SuppressionReason { Superseded, CancelEvent, FullyCancelled }
public enum InputValidity { Valid, ValidWithWarnings, Invalid }
public enum FieldOutcome  { NotCompared, Exact, WithinTolerance, Break }
public enum PassVerdict   { Committed, NoKey, NoCandidate, Vetoed, Contest, SearchIncomplete }
public enum Side          { Left, Right }
[Flags] public enum Composition { None = 0, OneToOne = 1, ManyToOne = 2, OneToMany = 4, Package = 8, LifecycleCollapsed = 16 }

public sealed class ReconciliationResult
{
    public RunInfo Run { get; }                           // profile hash, reference snapshot, engine version
    public IReadOnlyList<ResultEntry> Entries { get; }    // matched groups, contests, incomplete blocks, unmatched items
    public IReadOnlyList<RowResult> Rows { get; }         // one per input row: disposition, validity, diagnostics, entry id
    public RunSummary Summary { get; }
    public SideBySideView BuildSideBySide(SideBySideSpec spec);

    public CanonicalRow Projection(GroupId entry, Side side);       // what the diff compared: one item, a package, or an N:1 aggregate
    public CanonicalRow Canonical(SourceRef row);                   // one row's canonical values (null payload for quarantined fields)
    public IReadOnlyList<TraceStep> Trace(SourceRef row);           // rule, field, raw text, canonical value, for every transformed field
    public string? Raw(SourceRef row, string column);               // raw text if kept (§2.4)
    public string Format(in CanonicalValue value);                  // invariant text; symbols and identifiers resolved
}

public readonly struct CanonicalRow
{
    public bool IsNull(string field);
    public CanonicalValue this[string field] { get; }
    public string Text(string field);                               // Format of this[field]
}

public sealed record ResultEntry(GroupId Id, string PathId, Alignment Alignment, Composition Composition, FieldOutcome Outcome,
    IReadOnlyList<SourceRef> Left, IReadOnlyList<SourceRef> Right, IReadOnlyList<FieldDiff> Diffs, Explanation Explanation);

public sealed record RowResult(SourceRef Row, Disposition Disposition, SuppressionReason? Suppression,
    InputValidity Validity, IReadOnlyList<Diagnostic> Diagnostics, GroupId? EntryId);   // suppressed chain rows link to their survivor's entry
```

`Reconciler`, `RecordSources`, `CompiledProfile`, `RuleRegistry` and the result types are the supported surface; everything else is internal or rule-pack API.

---

## 12. Verification, tests and benchmarks

### 12.1 Verification status

**Verified in this revision.** An analysis script in Python implemented the normalization, consolidation, pass and tolerance rules of §3–§7 that these datasets exercise (not quarantine or duplicate handling) and was run against the repository data. It is evidence that the rules reproduce the oracles; it is not a test of the engine and is not part of the deliverable.

| Claim | Evidence |
|---|---|
| Sonnet: 275/275 oracle groups, 0 wrong pairs; P1 86, P2 81, P3 12, P4 4; residue 85/7 | Script, membership |
| Sonnet: 145 `Exact`, 18 `WithinTolerance`, 20 `Break`, under `ToEven`; each break has exactly one unattributed root, equal to the oracle's field (`G0158` via the §7.4 swap rule) | Script, §7 rules |
| Sonnet under `AwayFromZero`: 2 known-exact pairs gain a 0.01 inferred-fee difference | Script |
| Fixture 2.0: 275/275 cases, 0 wrong pairs with P1 and P2 | Script, membership only |
| Fixture 2.0 with P4 and no veto: one false pair (§4.3); with the veto: none | Script |
| Fixture v1: 310/310 cases; the residue contests are exactly the 10 `AMBIGUOUS` cases; with P4 added, 90 single-sided rows become contests | Script, alignment only |
| The §4.5 re-evaluation example and both §4.7 twin examples behave as described; all 12 Sonnet P3 blocks have one target | Script, constructed data |
| Sonnet membership unchanged under 3 row permutations | Script |
| 6 of 12 Sonnet allocation groups have no UTI on any internal row and no `Parent_Link_ID` | Direct read of the workbook |
| The Opus fixture's own checker (`verify_dataset.py`) reports 113 passed, 0 failed | Run in this revision; it checks the dataset's consistency, not this design |

**Proposed, not yet verified:** every acceptance criterion in §12.3, including field-level checks against fixture 2.0's `expected_differences` and `expected_canonical`, ID-scramble and thread invariance, AOT, and the 0 B-per-pair diff path.

**Unverified:** membership on the Opus, v2, Qwen and DeepSeek datasets with these rules, and any performance figure.

### 12.2 Datasets

Only the two input tabs reach the matcher. `Known_Exact_Matches`, oracle JSON and `Expected Results` stay in the harness. One adapter per oracle shape maps its outcomes through §6.5.

| Dataset | Role | Notes |
|---|---|---|
| `dataset/claude-sonnet-5.5` | **Primary acceptance** | No labels in the input tabs; IDs not aligned across sides. |
| `fixtures/RecDiffy_Trade_Reconciliation_Dataset.xlsx` + `RecDiffy_Hidden_Test_Oracle.json` (schema 2.0) | **Acceptance, field level** | `expected_differences`, `expected_canonical`, written rules. |
| `fixtures/RecDiffy.NET-fixture-v1.xlsx` | **Acceptance: ambiguity, identity-first breaks** | Same columns on both sides; 10 `AMBIGUOUS` cases. |
| `fixtures/…-claude-opus-5.5` | Acceptance after its profile is verified | 15 decoy orphans. |
| `fixtures/…-v2`, `dataset/qwen-3.8`, `dataset/deepseek-v4-pro` | Secondary, **ID-scrambled only** | IDs align across sides (`HF-2026-10139` ↔ `CP-BARC-50139`, `INT-000121` ↔ `CPTY-000121`, `IT0121` ↔ `CP0121`); Qwen labels orphans `DK_UNCONFIRMED`; the v2 header is on row 4; DeepSeek's run notes report 3 near-matches and 3 breaks with no difference in the fields checked. |
| `dataset/grok-4.7` | Excluded | No workbook or oracle was produced. |

### 12.3 Acceptance criteria

| ID | Criterion |
|---|---|
| AC-1 Membership | Sonnet, fixture 2.0 and fixture v1: every oracle group or case is one entry, and the rows linked to it (`RowResult.EntryId`, including suppressed chain rows) are exactly the oracle's member IDs; no other entries. |
| AC-2 Outcomes | Alignment, composition and outcome per §6.5. Sonnet: 145 / 18 / 20 / 85 / 7. |
| AC-3 Field deltas | Sonnet: each near-match delta equals the oracle `delta`; each break's root field equals the oracle `field`. Fixture 2.0: every `expected_differences` entry matches a `FieldDiff` on field, `Left`, `Right` and `Delta` as `decimal`, with no unattributed extra difference; `expected_canonical` equals `Projection(entry, Right)` field by field, and `Projection(entry, Left)` for exact and aggregated cases. |
| AC-4 Lifecycle | The survivor is the oracle's active report; the other chain rows are `Suppressed`. |
| AC-5 Row accounting | Every input row has exactly one disposition (§6.2) on every dataset. |
| AC-6 Shuffle invariance | 20 seeded permutations per dataset give identical results. |
| AC-7 ID-scramble invariance | Per §10.1, on every dataset; required to pass on v2, Qwen and DeepSeek, whose IDs leak matches. |
| AC-8 Thread invariance | `MaxDegreeOfParallelism` 1 and 16 give identical results. |
| AC-9 Budget invariance | The search-limit fixtures (§12.4) give the same `SearchIncomplete` or commit outcome under AC-6 and AC-7. |
| AC-10 AOT | A Native AOT build of a sample host passes AC-1 to AC-5 on Sonnet with identical output and no publish warnings. |
| AC-11 Generality | A non-trading profile (warehouse bins vs book stock by SKU) runs without the trading rule pack. |
| AC-12 Hot path | The diff benchmark allocates 0 B per exact pair. |

Pass-level counts (§4.4) are regression assertions; changing them requires a profile major version.

### 12.4 Synthetic fixtures

| Fixture | Expected |
|---|---|
| `98-1X`; `B` with −500,000; unknown LEI; `31/02/2025` | `Quarantined`: `PriceParse`, `SideSignConflict`, `UnknownParty`, `DateParse` |
| Identical duplicate row; conflicting duplicate row; conflicting duplicate `CANC` | One item with both copies; both copies quarantined; whole chain quarantined |
| `CANC` with absent target; two `CORR`s without sequence; a 2-cycle; `NEW` + `CANC` only | `MissingLifecycleTarget`; `LifecycleConflict`; `LifecycleCycle`; both rows `Suppressed` (`FullyCancelled`) |
| Leg without its partner | `Quarantined`, `IncompletePackage` |
| §4.5 re-evaluation example | A ↔ S committed by P2 |
| Block of 25 eligible items; a target that finds one solution then hits its node budget | `SearchIncomplete` (`BlockTooLarge`); `SearchIncomplete` (`BudgetExhausted`), solution reported, nothing committed |
| Two coupled targets, one `Unique` and one `Incomplete`; the same two targets made uncoupled by a veto | Nothing commits, component `SearchIncomplete`; the `Unique` target commits |
| §4.7 twin with and without a conflicting shared reference | Contest; `G0159` commits and the twin is `LeftOnly` |
| Accrued deltas 1.25 and 1.26; FX deltas 0.0001 and 0.00011 | `WithinTolerance`, `Break`, `WithinTolerance`, `Break` |
| `OMS7091414` principal under `ToEven` and `AwayFromZero` | `Exact`; `WithinTolerance` (fee 0.01) |
| P1 pair with `AssetClass` `Bond` vs `Equity` | `AssetClass` `Break`; `Accrued` compared; FX-rate tolerance not applied |
| Profile whose `date.parse` uses `formatBy: Counterparty` with no earlier rule writing `Counterparty`; profile listing `party.resolveLei` under `parse` | `Compile` fails: `FieldReadBeforeWrite`; wrong phase |
| Warehouse SKU: bins 4 and 6 against book 10 | One `ManyToOne` group, exact |

Property tests: no item in two groups; dispositions partition the rows; results invariant under permutation, scramble and thread count; P3 agrees with a brute-force enumerator on random blocks up to 12 items; normalizing a canonical row changes nothing; group sums conserve.

### 12.5 Benchmarks

BenchmarkDotNet with `MemoryDiagnoser`, per stage: time, allocated bytes, Gen0/1/2 and peak working set. Jobs: a seeded scale-up of the Sonnet scenario mix at 10k, 100k and 1M rows per side; skew (one counterparty holding half the rows); 10% key multiplicity; P3 blocks at `MaxBlockItems`; diff on 1M exact pairs (AC-12); cold vs warm profile compile; Native AOT. No throughput target is set (Q5); CI fails a job that regresses more than 10% against its stored baseline.

---

## 13. Open questions

| # | Question | Recommended default |
|---|---|---|
| Q1 | The wiki says the NuGet package is "the DLL only". Ship the trading rules as a second package, or inside `RecDiffy.dll` under their own namespace? | Second package, so the core DLL carries no domain. |
| Q2 | What records, keys and oracle should define cash-type matching? The repository has no cash data. | Ship the path template; add acceptance when data exists. |
| Q3 | A direction code says buy (or receive-fixed) but the quantity is negative: quarantine, or trust the code? | Quarantine (`SideSignConflict`). |
| Q4 | A lifecycle event whose target is not in the batch, such as a correction of yesterday's trade: quarantine, or treat a `CORR` as a new trade? | Quarantine (`MissingLifecycleTarget`). |
| Q5 | Real volumes and time or memory budgets per run. | No target; benchmark baselines with a 10% regression gate. |

---

## 14. Decisions

Where the two source specifications disagree or the repository contradicts them:

| Topic | `ARCHITECTURE-SPEC.md` | `ARCHITECTURE-SPEC-claude.md` | Decision | Evidence |
|---|---|---|---|---|
| Quantity in the natural key | Excluded; quantity breaks match on the natural key | Included in P2; reduced-key P4 afterwards | Claude's, with P4 per profile (§4.4) | Fixture 2.0 and v1 identity rules include quantity; v1 needs P4 off |
| Group-pass block | Shared reference, `Parent_Link_ID` or package ID only | Business fields plus price | Business fields (§4.4) | 6 Sonnet allocation groups have neither a UTI nor a link |
| Why passes are ordered | Earlier commits are final | "Each weaker key drops fields from a stronger one" | Evidence-based precedence (§4.2) | P1's key and P3's block are not subsets of P2's key |
| Deferred candidates | Ambiguity decided after the last pass | Any deferred, unconsumed item is `Ambiguous` | Re-evaluate after each pass; contests recomputed on the residue (§4.5) | Historical multiplicity would misclassify the §4.5 example |
| Conflicting shared references | Not covered | Not covered | Veto in P2–P4 (§4.3) | Fixture 2.0 false pair |
| Subset uniqueness | Commit when one subset fits; a second solution is a tie | Same | Commit only when every coupled target's search is complete and the unique solutions are disjoint (§4.7) | Neither defines one solution found before a budget stop, which does not exclude others |
| Subset tie-break | Optional smallest-cardinality-then-source-ID | None | None (§4.6) | Source IDs would break ID-scramble invariance |
| Search order and budgets | Sorted by ordinal and source ID; one budget per block | Quantity, ties by source reference; one budget per block | Per target, over its eligible items; classes by all conserved values; a budget per target; coupled targets decided together (§4.7) | Needed for AC-7 and AC-9 under finite budgets; equal cash and quantity alone do not make items interchangeable |
| Duplicates | Quarantine every copy | Dedupe identical, quarantine conflicting; events separate | One rule for rows and events (§3.2) | Every copy accounted for |
| Final dispositions | Committed, unresolved, ambiguous, unmatched | Exhaustive list, no precedence | Precedence table (§6.2) | Rows can qualify for several |
| Canonical model | Caller's generic `TPayload`, source generator | Typed columns, source generator, expression fallback | Typed columns, minimal (§2.1); caller lambdas, no generator | Runtime-declared fields without boxing or codegen |
| Midpoint rounding | Away from zero for the trade sample | Per profile | Per profile (§7.3) | Sonnet stores `ToEven` values |
| Lifecycle target missing | Quarantine the `CANC` | `CORR` stays live with a warning | Quarantine (Q4) | One rule; no oracle case |
| Scored matching | Hook, off | P5 opt-in, suggest-only | Not in v1 | No requirement; fixtures forbid fuzzy matching |
| 1:N | Off | Symmetric | `many` side parameter; acceptance uses left | No oracle case |
| Nested diff | Child collections at depth one | No | No | Out of scope |
| Conditions when the sides disagree | Not covered | Not covered | Applicability on either side, tolerance on both (§7.5) | Disagreement must not hide a field or widen a tolerance |
| Materiality budget, business-day tolerance, format migrations | Not included | Included | Not included | No requirement |
| Ambiguity in the oracles | "No oracle contains an ambiguous group" | Fixture v1 has 10 | 10 `AMBIGUOUS` cases in fixture v1 | `Expected Results` tab |
| Target framework | `net8.0` minimum | `net10.0` | `net10.0` | .NET 8 support ends November 2026 |
| Performance targets | None | Proposed numbers | None (Q5) | No measured basis |
| Side-by-side | Per-group grid with totals | Pivot with delta decomposition | Pivot with decomposition (§6.6) | Wiki: cells hold both sides, totals underneath |
| Public API | Generic `Run<TLeft,TRight,TCanonical>` | Non-generic, broad | Non-generic, small (§11) | Follows the canonical model |
