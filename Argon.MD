
```markdown
We are designing and implementing **RecDiffy.NET**, a general-purpose, high-performance record reconciliation and diffing library for modern .NET (C# 12/13, .NET 8/9+). 

Previously, we evaluated naive two-step pipelines (`SuperLinq`/`MoreLINQ` `FullOuterJoin` -> `CompareNETObjects`). However, after building our benchmark back-office test dataset (300 internal OMS trades vs. 200 external street/counterparty rows), it is clear that a naive `FullOuterJoin` + `CompareNETObjects` pipeline will fail before it even reaches field comparison.

### Why the Naive Pipeline Fails on Real Data
Look at the actual shape of the data our library must reconcile:
1. **Heterogeneous Schemas & Perspectives:** Left (`InternalTrade`) and Right (`CounterpartyTrade`) have different schemas and conventions:
   * **Aliases:** Left has `GSCO` / `JPM`; Right sends `GS_NY_PRIME`, `JPMS_CLEAR`, `BOFA_MLPF&S`, or `20-char LEI`.
   * **Direction & Sign Encoding:** Left uses `BUY`/`SELL`/`PAY_FIXED`/`REC_FIXED` with unsigned quantities. Right might send `B`/`S`, `BOT`/`SLD`, lowercase `buy`/`sell`, or leave direction **completely blank** and encode sells as **negative quantities** (`-500,000`), or send *both* `S` and `-500,000`.
   * **Dealer Perspective Inversion:** On bilateral OTC feeds (`DEALER_BILATERAL_VIEW`), an internal `BUY` / `PAY_FIXED` is reported from the dealer's side as `SLD` / `REC`.
   * **Domain Value Encodings:** Left stores US Treasury prices as decimal (`98.359375`); Right sends fractional 32nds strings (`"98-11+"`) or Dirty Prices (`Clean + Accrued`).
   * **Cross-Column Identifier Lookup:** Left has separate `Ticker`, `ISIN`, `CUSIP`, and `SEDOL` properties. Right has a single `Primary_Security_ID` column whose value could match *any* of those four columns.
2. **Pre-Match Stream Mutations (Intra-Stream):**
   * **Cancel & Correct Chains (1-to-3 collapsing to 1-to-1):** The Right feed contains `NEW` (busted), `CANC` (reversal referencing `Cancels_Prior_Report_ID`), and `CORR` (rebook) rows that must collapse *before* cross-side matching.
   * **Multi-Leg Packaging (2-to-1):** Left books swaps/forwards as 2 linked rows (`LEG_1_PAY` and `LEG_2_RECEIVE` sharing a `Parent_Link_ID`), while Right sends 1 combined net row.
3. **Multi-Pass & Many-to-One Set Alignment:**
   * Half the trades share a `UTI_Exec_ID`; the other half lack a shared ID and must match on natural business keys (`Counterparty + Instrument + Side + TradeDate`).
   * **N-to-1 Allocation Splits (3-to-1):** Left books 3 sub-fund allocations (`400k + 500k + 600k`) that sum up to 1 single Prime Broker block trade (`1,500k`) on the Right.
4. **Tri-State Field Diffing:**
   * Once paired, differences must be classified into **Exact Match**, **Matched Within Tolerance** (e.g., $\pm \$1.50$ accrued interest rounding or 4th-decimal FX rate, preserving the exact numeric delta), or **Economic Break** (busted price, 80% partial fill quantity, $T+1$ vs. $T+2$ settle date, or $450 fee discrepancy).

---

### Your Task

Keeping in mind that **RecDiffy.NET is a general-purpose record diffing library** (where financial trade/bond/cash reconciliation is a flagship use case configured via policies, not hardcoded into the core engine), provide a comprehensive architectural revision and C# implementation blueprint:

1. **Revised Opinion on External Libraries vs. Custom Core:**
   * Given this data shape, revisit `SuperLinq`, `MoreLINQ`, and `CompareNETObjects`. What (if anything) survives in the core DLL, what gets pushed to optional adapters, and why?
2. **The Revised 5-Stage Pipeline Architecture:**
   * Design the exact interfaces and data flow for a general-purpose engine that handles:
     * **Stage 1: Canonical Normalization & Projection** (`TLeft` and `TRight` -> `TCanonical`, or adapter views, handling 32nds parsing, sign/direction normalization, and dealer perspective inversion).
     * **Stage 2: Intra-Side Lifecycle & Package Consolidation** (collapsing `NEW`/`CANC`/`CORR` chains and grouping `Parent_Link_ID` multi-leg rows before matching).
     * **Stage 3: Multi-Index / Cross-Key Resolver** (how to index a record when an entity can be identified by `UTI` OR `ISIN` OR `CUSIP` OR `SEDOL` OR `Ticker` without duplicating records across buckets).
     * **Stage 4: Cascading Match Passes** (Pass 1: Exact Shared ID -> Pass 2: Natural Business Key 1:1 -> Pass 3: N-to-1 Subset-Sum / Group Aggregation for allocation splits -> Pass 4: Fuzzy/Heuristic candidate scoring for broken trades).
     * **Stage 5: Zero-Boxing Compiled Diff Engine** (producing a structured diff AST with `Exact`, `WithinTolerance`, and `Break` states plus strongly-typed deltas).
3. **Concrete C# Code Blueprint:**
   * Write the core C# interfaces, `readonly record struct` result types, and a fluent DSL (`ReconciliationProfile<TLeft, TRight, TCanonical>`) showing how a consumer configures this entire pipeline cleanly.
   * Provide a working C# implementation of the **Cascading Matcher** (including the multi-key index and the N-to-1 subset/group matcher) and the **Compiled Expression Tree Field Differ** with tolerance support.

```
