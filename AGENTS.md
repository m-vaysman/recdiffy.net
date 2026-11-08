# AGENTS.md

These are the instructions for every AI agent that works in this repository. That includes Cursor, GitHub Copilot, Codex, and Claude Code (through `CLAUDE.md`). People can follow them too.

Read the whole file before you change anything. After the repository basics, it has two rule sets:

1. [Extend, not edit](#extend-not-edit): code on `main` is closed.
2. [Functional C# first](#functional-c-first): how to write C# here.

## Repository basics

### Build and test

- **Solution:** `RecDiffy.NET/RecDiffy.slnx`
- **Build:** this must be clean, with no warnings:

  ```
  dotnet build RecDiffy.NET/RecDiffy.slnx -c Release -warnaserror
  ```
- **Tests:** CI runs every test project in the solution, in Release, with no filter:

  ```
  dotnet test RecDiffy.NET/RecDiffy.slnx -c Release
  ```

  The Excel tests skip the LibreOffice recalc tests when `soffice` isn't installed.

### Branches and PRs

- Branch from `dev`, and open PRs into `dev`.
- `main` only receives release merges from `dev`. Never push to `main`, and don't create tags.
- Never force-push or rebase `dev`.
- Don't merge your own PR.

### Working beside other agents

- **Check open PRs first.** Several agents work on this repository at the same time. Before you open a PR, list the open PRs and the files they touch. If one already covers your change, don't duplicate it; build on whatever it leaves undone.
- **Put new tests in new files.** Other agents append to the shared test files.

### Build gotcha

- **No `[GeneratedRegex]` in test projects.** The Sonar build strips source generators from test projects, so that code fails there with CS8795 even though `ci.yml` passes. Use `new Regex(pattern, options, TimeSpan)` fields instead.

## Where changes go

Profiles refer to rules by **name**, never by class. The name-to-class mapping is the seam, so look for it before you edit anything.

| Change | Where |
|---|---|
| A new core rule | A new class, plus a `Register(...)` line in `RuleRegistry.CreateCore` (`RecDiffy.NET/src/RecDiffy/RuleRegistry.cs`) |
| A new trading rule | A new class, plus a `Register(...)` line in `TradingRulePack.AddTo` (`RecDiffy.NET/src/RecDiffy.RulePacks.Trading/TradingRulePack.cs`) |
| A fix to a rule that is on `main` | [Replace and repoint](#replace-and-repoint): a new class, then repoint the same name at it and tag the old class `[Obsolete]` |

Repointing a name fixes every profile that uses it. No profile needs an edit, and no profile hash changes.

---

## Extend, not edit

You are EXTEND_NOT_EDIT.

Your default action is to add new code beside shipped code, not modify shipped code.

### Shipped

`main` is shipped.

If the type or file already exists on `main`, it is CLOSED. The ticket is EXTEND.

You add a neighbor. You do not amend the copy that is on `main`.

If the type exists only on this feature branch, it is clay. You may change it on this branch when the ticket requires it. It has not shipped.

- Do not infer CHANGE because the edit looks small, local, or obvious.
- Do not infer CHANGE from difficulty, convenience, architecture, or intent.
- CHANGE is only when the ticket says CHANGE, and only for the types it names.

A bug in a type that is already on `main` is fixed by REPLACE AND REPOINT when a registration, factory, or composition seam creates that type. It is CHANGE of that type only when no such seam exists.

A wrapper that hides the bug is not EXTEND.

### Default

Every ticket is EXTEND unless it explicitly says CHANGE. If the ticket says neither, treat it as EXTEND.

### Open / closed

Closed means: this text is on `main`.

A closed type is read-only under EXTEND. Its existing public surface and behavior stay where they are:

- fields and properties
- constructors
- method signatures
- implemented interfaces
- externally observable invariants

Do not open a closed type and add behavior to it. New behavior belongs in:

- a new file
- a wrapper or decorator
- a new implementation of an existing interface
- existing composition or registration code, outside the locked type

A test does not make a type shipped. `main` does.

A test does not count as a reason to edit a closed type.

### Ticket words

The ticket must say EXTEND or CHANGE. If it says neither, treat it as EXTEND.

### EXTEND

The named existing type is locked if it is on `main`.

You may:

- add new files
- add tests of the new type
- make the minimum composition or registration change required to select or wire the new behavior

You may not:

- edit the locked type
- add methods, fields, properties, constructors, branches, flags, or interface implementations to it
- move its existing public surface
- change its existing behavior to create a seam
- rewrite tests of the locked type to change what it is supposed to do

Composition lives outside the locked type.

If the only factory is a method on the locked type, the seam is missing.

If the requested behavior cannot be implemented through an existing seam, stop. Say:

> The seam is missing.

Explain briefly which dependency or extension point is missing and why the requested behavior cannot be added without changing the locked type.

Do not create the seam yourself unless the ticket says CHANGE.

### CHANGE

Only types explicitly named under CHANGE may be edited.

Before making changes, state in one sentence why the existing design has no usable seam.

CHANGE permits only the modification required by the ticket. It is not permission to:

- refactor neighboring code
- rename unrelated symbols
- reformat unrelated code
- reorganize files
- modernize syntax
- clean up nearby design
- fix unrelated problems

Types not named under CHANGE remain subject to EXTEND rules. If they are on `main`, they stay locked.

### Green clay

A type that exists only on this feature branch is clay and may be changed when necessary for the ticket.

The moment that type is on `main`, it is closed.

Do not use clay as permission for unrelated cleanup.

### While I am here

Banned.

If a change is not required by the ticket, it does not belong in the diff.

Do not rename, reformat, reorder, fold, extract, modernize, or improve unrelated code.

### Diff shape

For an EXTEND ticket, the expected diff is:

- new implementation file
- new tests of the new type
- minimal registration or composition change, outside the locked type
- locked implementation file absent from the diff
- tests of the locked type unchanged

If the locked file appears in the diff, the EXTEND implementation has failed.

The one exception is REPLACE AND REPOINT: the old file appears with only the obsolete tag and its comment.

Registration may require more than one physical line, but it must be the smallest wiring-only change necessary. No behavior belongs there.

### How to extend

Prefer, in order:

1. New implementation of an existing interface.
2. Decorator or wrapper around the existing implementation.
3. Existing strategy, factory, provider, handler, middleware, policy, callback, or registration seam.
4. New partial file, only if that type is already designed and used as a partial type.

Adding a new method, property, field, constructor, branch, flag, or interface to the old type is not EXTEND. That requires CHANGE.

### Example

Illegal under EXTEND:

- Edit `GuardedNoticeExtractor.ExtractAsync` and add retry behavior.

Legal under EXTEND:

- Add `RetryingNoticeExtraction : INoticeExtraction`, delegate to the existing implementation, add tests for retry behavior, and change only the composition root so callers receive the wrapper.

If no interface, wrapper point, factory, or composition seam exists:

> The seam is missing.

Stop. Do not manufacture one inside the locked type.

### Replace and repoint

Use this when a type on `main` has a bug and something outside it creates it: a registry entry, a factory, a DI registration, a plugin pack, a composition root.

Callers and configuration refer to the name, key, or interface. They do not refer to the class. That reference is the seam.

Look for the seam before you propose CHANGE. Find where the buggy type is constructed or registered. Then:

1. **Write a new implementation** of the same interface in a new file. Do not call or wrap the old type. It has the bug.
2. **Repoint the existing registration** at the new type. Keep the same name, id, or key, so every existing caller and configuration gets the fix without being edited.
   - Do not register a second name beside the old one. That leaves the bug live for everything that uses the old name.
3. **Tag the old type obsolete.** Use `[Obsolete("Replaced by <NewType>: <one-line defect>. See <issue>.")]`, or the language's deprecation marker. Add a short comment that names the replacement and the defect.
   - The obsolete tag and its comment are the only edit allowed to the old type. They need no CHANGE ticket. Name them in the PR description.
   - Do not change anything else in the old type. Leave its tests unchanged.
4. **Write a new test file for the new type.** If the type is internal, test it through the registration, so the test also proves the repoint.
5. **Leave deleting the old type to a human.**

Only when no registration, factory, or composition seam creates the type is the fix a CHANGE of the type itself. Before proposing that, say in one sentence where you looked for the seam and why there is none.

Expected diff:

- new implementation file
- new test file for the new type
- the registration line repointed to the new type, same name
- the obsolete tag and comment on the old type, and nothing else in that file

Example: a registry maps `"quantity.absolute"` to `QuantityAbsoluteRule`, which silently drops `int` input.

- **Illegal:** edit the switch inside `QuantityAbsoluteRule`.
- **Illegal:** register `"quantity.abs"` to a new rule beside the old name.
- **Illegal:** wrap `QuantityAbsoluteRule` and convert `int` before calling it.
- **Legal:**
  - add `AbsoluteQuantityRule : INormalizationRule` in a new file;
  - change the registration to `"quantity.absolute" => new AbsoluteQuantityRule(...)`;
  - tag `QuantityAbsoluteRule` `[Obsolete]`, with a comment naming `AbsoluteQuantityRule` and the issue;
  - add `AbsoluteQuantityRuleTests`.

### Output

Do not write comments unless asked. The obsolete comment in REPLACE AND REPOINT counts as asked.

Do not add TODOs, breadcrumbs, migration notes, or speculative abstractions.

For EXTEND, return only:

- the new implementation
- the tests of the new type
- the minimal registration or composition snippet

Do not return modifications to the locked type.

For REPLACE AND REPOINT, return:

- the new implementation
- the new test file
- the repointed registration line
- the obsolete tag and comment on the old type

If the seam is missing:

- return one short paragraph explaining the missing seam
- make no code changes

For CHANGE:

- begin with the one-sentence seam explanation
- return only changes required by the named CHANGE scope

---

## Functional C# first

When building anything in .NET, lean functional where it stays readable. Target modern C# (12+ / .NET 8+) unless the project specifies otherwise.

Functional C# prioritizes pure transformations, explicit state transitions, and immutable data. Declarative syntax supports these goals, but LINQ, records, and expressions alone do not make code functional.

Prefer a readable sequence of named, single-assignment stages. Do not minimize local variables at the expense of clarity.

### Required (hard rules)

- **Use switch expressions instead of if/else chains** when choosing a value or outcome from a discriminant (enum, type, constant set, or other multi-way branch). Do not leave multi-arm if/else ladders that only pick a result.
- **Use pattern matching** (`is` patterns, switch patterns, relational/list patterns as appropriate) instead of type checks followed by casts, `as` + null checks, `GetType()` comparisons, and hand-rolled type/value discriminators that patterns express directly.
- **Prefer pure transformations and explicit state transitions over mutation.** Use functions that return results and updated state instead of modifying arguments or shared state. Compose named, single-assignment stages rather than repeatedly reassigning locals. Local implementation mutation is permitted under the scoped rules in "Be pragmatic."
- **Prefer primary constructors** for types that take constructor parameters. Do not write classic constructors that only assign fields when a primary constructor would do. Keep explicit constructors when required by initialization logic, framework constraints, or language compatibility.
- **When an `if` is necessary, prefer `is` / `is not` with property patterns** where they express the condition clearly, such as `if (o is { Total: > 100 and Qty: < 50 })`.
- **Prefer `is not`, and pattern `and` / `or`, over `!=`, `&&`, and `||` where patterns apply.** Examples include `o is not null` and `x is > 0 and < 10`. Keep ordinary boolean operators for plain boolean logic; do not force boolean expressions into patterns.
- **Do not use unreadable ternary chains or nested `?:` expressions.** Ternaries are fine where they stay clear and short. Readability wins over packing logic into one expression.
- **Prefer collection expressions (`[...]`) for arrays and lists** when the target type is clear. Use spreads (`[..existing]`) instead of manual copies or `AddRange` boilerplate when eager materialization is intended. Preserve deferred execution when it matters.
- **Validate required reference-type parameters at public boundaries.** If a parameter is null and there is no valid way to continue, throw, preferably with `ArgumentNullException.ThrowIfNull`. Do not silently proceed or hide the issue with null-forgiving operators.
- **Keep exception handling at appropriate boundaries.** Use concise guards at the edge, keep core transformations free of try/catch control flow, and reserve exceptions for exceptional conditions or contract failures rather than expected outcomes.

### Refactoring stateful pipelines

- **Identify inputs, outputs, mutations, and ordering dependencies before changing syntax.** When a snippet contains overlapping implementations, flag the duplication and state which version the refactor assumes.
- **Model each stage as a transformation from inputs and prior state to a result and next state.** Prefer returning updated state over mutating arguments, shared counters, indexes, or collections.
- **Verify immutability through the entire state graph.** Records and immutable collections can contain mutable objects. `init` limits assignment but does not make referenced objects immutable. `IReadOnlyList<T>` exposes a read-only interface but does not guarantee immutable storage. Returning a mutated input is not a pure state transition.
- **Choose the operator that matches the dependency.** Use `Select` / `Where` for independent transformations, `SelectMany` for flattening, and `Aggregate` when each step genuinely depends on accumulated state. Do not use `Aggregate` merely to hide mutation inside a lambda.
- **Flatten nested folds when they thread the same state through nested sequences and flattening preserves behavior.** Preserve checks or actions that must still occur for empty inner sequences.
- **Preserve observable behavior.** Retain ordering, equality comparers, snapshot semantics, enumeration timing and count, cancellation behavior, and exception behavior. Do not introduce parallel execution when later steps depend on earlier state.
- **Keep intermediate stages named and single-assignment.** Extract helpers when they clarify a responsibility; avoid abstractions that merely relocate a short expression. Use named tuples for small local states and dedicated types when the state needs a reusable contract or invariants.
- **Distinguish verified changes from assumptions.** For incomplete snippets, state assumptions about unseen types and methods. Do not claim purity without inspecting the relevant implementations.

### Prefer

- **`with` expressions and deconstruction** when updating or unpacking immutable records, structs, or tuples. Return a modified copy instead of mutating fields. Capture the source separately when both the original and updated values are needed.
- **Indices and ranges** (`^1`, `..`, `arr[^1]`, `arr[1..^1]`) over equivalent LINQ only when readability and concision clearly improve and allocation or evaluation behavior remains appropriate.
- **Pure functions:** output depends only on inputs; no hidden state or observable side effects. Make them `static` where possible.
- **Immutable data:** use records, immutable collections, and frozen collections as appropriate. Treat `required`, `init`, and read-only interfaces as contract tools, not proof of deep immutability.
- **LINQ for transformations:** use `Select`, `Where`, `GroupBy`, `Aggregate`, `Zip`, `Chunk`, and `MaxBy` when they express the operation clearly. Prefer them over loops whose only purpose is projecting or filtering into a collection.
- **Expressions over statements** where they stay clear, including short ternaries and expression-bodied members. Do not compress substantial logic merely to make it expression-bodied.
- **Higher-order functions:** pass delegates and compose small functions when doing so clarifies variation or rule application. Keep related rules cohesive; do not require one file per function or rule.
- **Explicit results for expected failures:** use a small result type or an appropriate nullable return when absence is sufficient. Represent failure reasons explicitly when callers need them.

### Keep side effects at the edges

- Core transformations should be deterministic. Keep HTTP, database access, files, logging, clock access, and randomness in a thin outer layer or supply their results as explicit inputs.
- Inject `TimeProvider` instead of calling `DateTime.Now` directly when time is a dependency. Pass a captured time value into pure core logic when appropriate.
- Keep cancellation explicit. Preserve where cancellation is observed during refactoring; a cancellation-aware orchestration method is not strictly pure just because its collections are immutable.

### Avoid

- Mutable fields and shared static state in core logic.
- `ref` / `out` plumbing where returning a value or result type is clearer.
- Methods that both compute a result and mutate caller-owned inputs.
- Loops that only add projected or filtered values to a list when a clear LINQ transformation would do.
- `void` methods in core logic that communicate results through mutation.
- `Aggregate` calls that mutate their accumulator and return the same object.
- Multi-way if/else or statement `switch` used only to select a value when a switch expression would do.
- Type-check-and-cast ladders when pattern matching expresses the operation directly.
- Long imperative blocks that mainly shuffle values through reassigned locals.
- Cosmetic "functional" rewrites that preserve hidden mutation or change behavior.

### Be pragmatic

- **Readability wins.** If a LINQ chain gets long or clever, name the stages or use a small helper. Well-named, single-assignment locals are encouraged.
- **Local mutation inside a helper is acceptable when it remains unobservable.** Do not mutate caller-owned inputs, leak mutable working state, or depend on hidden state. Return a stable result.
- **Keep iterative algorithms when they preserve stack safety, early exits, or clear control flow.** Do not replace loops with recursion for functional appearance. C# does not guarantee tail-call optimization.
- **Use loops, builders, or spans when justified by algorithmic constraints or measured performance.** Keep the public contract explicit and isolate implementation mutation. Explain non-obvious tradeoffs.
- **Early-exit `if` guards that only `return`, `continue`, or `throw` are fine.** They are not a substitute for switch expressions when a multi-way branch computes or selects a value.
- **Do not use pragmatism to retain unnecessary multi-way if/else or cast ladders.** Apply the expression and pattern-matching rules when they fit the operation.
- **Validate meaningful behavior after refactoring.** Use appropriate existing checks and focused tests for ordering, state ownership, cancellation, or other affected semantics. Do not add tests that merely enforce a preferred syntax.
