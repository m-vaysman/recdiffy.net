# Cursor brief: RecDiffy.NET architectural specification

**Goal:** Produce an implementation-ready architectural specification for RecDiffy.NET, a general-purpose, dynamic reconciliation library for .NET. It is built on **cascading matching** (passes from strongest to weakest evidence), **not** deep object compare. Bond, trade and cash matching are use cases, not the scope.

**Do not write implementation code.** The only deliverable is the spec.

## Sources, read in this order

1. **Project wiki: read all of it first.**
   - https://github.com/m-vaysman/recdiffy.net/wiki
   - Clone it with `git clone https://github.com/m-vaysman/recdiffy.net.wiki.git`.
   - Treat Michael's own pages (for example, Excel-Side-by-Side-Reconciliation) as requirements. Treat the AI opinion pages as input only.
2. **Test data: learn the data shape and the expected output.**
   - https://github.com/m-vaysman/recdiffy.net/tree/main/test-data/trade-reconciliation-dataset
   - Read `prompt.md` and `README.md`, then each model folder.
   - `claude-sonnet-5.5` is the cleanest dataset.
   - `qwen-3.8` and `deepseek-v4-pro` are usable but leak matches through shared ID numbers.
   - `grok-4.7` has no generated files.
   - Also see https://github.com/m-vaysman/recdiffy.net/tree/main/test-data/trade-reconciliation-fixtures, which holds the hidden-test oracles in JSON.
   - Confirm exactly which output the oracles expect: match groups, left-only and right-only rows, ambiguous rows, and field breaks.
3. **Argon.MD: the baseline design.**
   - https://github.com/m-vaysman/recdiffy.net/blob/main/Argon.MD
   - It defines five stages: normalize, collapse lifecycle and packages, multi-key index, cascading matcher with N-to-1, and compiled typed diff with tolerances.
4. **The seven "what is missing" opinions on Argon.MD.** Each branch holds `opinions/argon-missing/<slug>.md` and a shared `prompt.md`.
   - https://github.com/m-vaysman/recdiffy.net/tree/opinion/gpt-6-astra
   - https://github.com/m-vaysman/recdiffy.net/tree/opinion/deepseek-v4-pro
   - https://github.com/m-vaysman/recdiffy.net/tree/opinion/kimi-k3.1-pro
   - https://github.com/m-vaysman/recdiffy.net/tree/opinion/qwen-3.8
   - https://github.com/m-vaysman/recdiffy.net/tree/opinion/grok-4.7
   - https://github.com/m-vaysman/recdiffy.net/tree/opinion/mistral-large-4
   - https://github.com/m-vaysman/recdiffy.net/tree/opinion/claude-opus-5.5

## Decision rule

**Grok makes the final call.** Run this task on Grok (Grok 4.7 or the newest available, highest reasoning). Grok weighs every input above and decides what to keep, change or drop.

The spec ends with a **Decisions** table. Each row gives the item, its source (wiki, test data, Argon.MD, or a named model), whether it is kept, changed or rejected, and a one-line reason.

## Constraints

- Use C# and current .NET.
- Prefer rule-based designs: each match or normalization rule is its own small type, one rule per file, and the engine runs a collection of rules. Don't use long if-chains.
- Matching must be configured through profiles, not hard-coded per asset class.
- Don't hard-code file paths.
- Don't reference or borrow from any third-party reconciliation library's design unless the wiki explicitly endorses it.

## The spec must cover

1. Scope and non-goals, including batch versus streaming, N-to-1 versus N-to-M, and nested diff (yes or no).
2. A canonical record model, typed and scoped identifiers, and provenance back to the raw input.
3. Each pipeline stage: its contract, its inputs and outputs, its failure and quarantine policy, and its ordering.
4. Cascading matcher passes, candidate generation, ambiguity and tie policy, atomic consumption (no record in two groups), and limits on the subset-sum search.
5. Lifecycle (new, cancel, correct) and package consolidation, including out-of-order and missing events.
6. A result model with alignment status, input validity and field-diff outcome kept separate, plus roll-up and an explanation for each match and non-match.
7. The tolerance contract: absolute and relative tolerance, boundaries, rounding, nulls, and dates.
8. Diff engine design: compiled plans cached per type, the boxing and allocation guarantees, and the AOT and trimming position.
9. Configuration and profile schema, with versioning.
10. Determinism, threading, cancellation and observability.
11. A public API surface: namespaces, key interfaces and types, as signatures only.
12. A test plan that maps the test data and oracles to acceptance criteria, plus a benchmark plan.
13. Open questions for Michael.

## Output

- Write the spec to `docs/ARCHITECTURE-SPEC.md` on a new branch `spec/architecture`, and open a pull request against `main`. Don't merge it.
- Keep it implementation-ready: precise, with tables where they help, and no filler.