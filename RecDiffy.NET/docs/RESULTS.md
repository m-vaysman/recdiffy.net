# Reading a reconciliation result

`Reconciler.Run` returns a `ReconciliationResult`. This guide explains what each part of it means, and how to report it without overstating what was found.

The types are in [ResultModel.cs](../src/RecDiffy/Results/ResultModel.cs) and [Alignment.cs](../src/RecDiffy/Results/Alignment.cs). The result is assembled in [ResultBuilder.cs](../src/RecDiffy/Results/ResultBuilder.cs). For the profile settings that shape a result, see [PROFILES.md](PROFILES.md).

Two rules to keep in mind:

- **Matched doesn't mean balanced.** Matching decides which records belong together. Comparison then decides whether their values agree. A matched entry can hold a break.
- **Missing isn't zero.** A blank or unreadable value is unknown. RecDiffy never reads it as 0.

## What the result holds

| Member | What it is |
|---|---|
| `Entries` | One `ResultEntry` per group of rows: a match, an ambiguous cluster, an incomplete search, or one unmatched row. |
| `Rows` | One `RowResult` per input row, so every input row appears exactly once. |
| `Summary` | A `RunSummary` of counts. Some count entries and some count rows. See [Counting](#counting). |
| `Run` | A `RunInfo`: the profile hash, the reference data id and hash, the engine version, and how each side's text was read. |

An entry can hold many rows. A row belongs to at most one entry, and `RowResult.EntryId` names it.

## Four separate questions

A result answers four questions. Each has its own property, and they don't imply each other.

| Question | Property | Values |
|---|---|---|
| Did rows correspond across the two sides? | `ResultEntry.Alignment` | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly` |
| How were rows grouped? | `ResultEntry.Composition` | `OneToOne`, `ManyToOne`, `OneToMany`, plus the flags `Package` and `LifecycleCollapsed`. Entries that aren't matched are `None`. |
| Do the compared values agree? | `FieldDiff.Outcome`, and `ResultEntry.Outcome` for the entry | `Exact`, `WithinTolerance`, `Break`. `NotCompared` when no comparison ran. |
| What happened to this input row? | `RowResult.Disposition` | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly`, `Quarantined`, `Suppressed` |

`RowResult.Validity` adds a fifth, smaller question: was the row's input clean? It is `Valid`, `ValidWithWarnings` or `Invalid`.

## Matched doesn't mean balanced

Matching uses the keys a pass declares. Comparison runs afterwards, on every matched entry, over every field whose role is `Key` or `Economic`. Fields with the roles `Identity`, `Lifecycle`, `Package` and `Text` are never compared.

- **The entry outcome is the worst field outcome.** Any `Break` makes the entry a `Break`. Otherwise any `WithinTolerance` makes it `WithinTolerance`. Otherwise it is `Exact`.
- **`Diffs` lists only the fields that differ.** A field that is `Exact` or `NotCompared` is left out, so an `Exact` entry has an empty list.
- **Only matched entries are compared.** Every other entry, ambiguous and incomplete ones included, has the outcome `NotCompared` and an empty `Diffs`.

```csharp
var breaks = result.Entries.Where(entry => entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Break });
```

The [README example](../../README.md#see-it-work-two-files-two-readers-one-reconciliation) shows this: C-1003 matches T-5003 on its trade reference, but its quantity and net amount break.

### How a field is compared

| Field type | Comparison |
|---|---|
| `Decimal` | Both values are rounded to the field's `scale` with the profile's midpoint rule. The delta is right minus left. A tolerance then decides between `WithinTolerance` and `Break`. |
| `Date` | The delta is in days. Only a `days` tolerance allows a gap. |
| `Symbol`, `Identifier`, `Text` | Ordinal equality. No tolerance applies. |

- **Tolerances are inclusive.** A delta exactly equal to the bound is `WithinTolerance`.
- **Rounding comes first.** At scale 2, 10.004 and 10.006 become 10.00 and 10.01, so the delta is 0.01.
- **A relative bound** is a fraction of the larger of the two absolute values.
- **A tolerance `when`** must hold on both sides. If it fails on either side, any difference in a decimal field is a `Break`. A `days` tolerance on a date field ignores its `when` today; see [PROFILES.md](PROFILES.md#known-gaps).
- **`applicableWhen`** skips the field, as `NotCompared`, only when it fails on both sides.
- **`FieldDiff.Tolerance`** names the tolerance kind declared for the field, such as `Exact`, `Absolute` or `Days`. It names the declared kind even when the tolerance didn't apply, for example because its `when` failed.

The comparison code is [FieldDiffer.cs](../src/RecDiffy/Diff/FieldDiffer.cs). The inclusive bound is tested by `Keeps_an_accrued_delta_inside_an_inclusive_absolute_tolerance` in [DiffTests.cs](../src/RecDiffy.Tests/DiffTests.cs) and `Breaks_an_accrued_delta_just_outside_the_tolerance` in [VerificationTests.cs](../src/RecDiffy.Tests/VerificationTests.cs).

### Grouped entries

A `ManyToOne` or `OneToMany` entry compares one combined row against the single row on the other side. `result.Projection(entry.Id, side)` returns the combined row.

- **Compared decimals are summed** over the many side, then rounded to scale. The pass's `block` fields are the exception: they are equal by construction, so they aren't summed.
- **Every other field** keeps its value when all members agree. When members disagree, the value is unknown, with a `MembersDisagree` warning if the run uses the field.

### Attributed differences

The trading pack registers attributions that explain one field's difference by another's. For example, `trading.netCash` checks whether a net cash difference follows from the quantity, price, accrued and fee differences.

- **An explained field takes its roots' outcome.** If the quantity breaks and net cash moves only because of it, net cash is a `Break` too.
- **`FieldDiff.AttributedTo`** names the root fields, so a report can show the root break once and the consequences beside it.
- **Known issue:** `AttributedTo` is also filled when the formula doesn't explain the difference ([#254](https://github.com/m-vaysman/recdiffy.net/issues/254)). Check the root fields' own outcomes before you treat a field as a consequence.

## Missing isn't zero

A value that isn't there stays unknown all the way through.

- **Reading.** A blank cell becomes null, with no diagnostic. A value that can't be read becomes null, with an `Invalid` diagnostic, and the row is quarantined. One rule differs: `date.parse` with `formatBy` reports even a blank as `DateParse` when the row's party has no date format.
- **Matching.** A pass leaves a row out when any of its key fields is null. The row stays free for later passes. A group pass never treats a null amount as 0 (`Does_not_match_a_blank_or_invalid_quantity_as_zero` in [VerificationTests.cs](../src/RecDiffy.Tests/VerificationTests.cs)).
- **Comparing.** Null against null is `Exact`. Null against any value, 0 included, is a `Break`.
- **Combining.** One null member makes a group's sum null. One unknown leg makes a package's total unknown ([AdversarialPackageNullTests.cs](../src/RecDiffy.Tests/AdversarialPackageNullTests.cs)).
- **Overflow.** A sum past the decimal range is unknown too, with an `AggregateOverflow` warning.

When one side is null, `FieldDiff.Delta` is zero. Check `FieldDiff.Left.IsNull` and `FieldDiff.Right.IsNull` before you read the delta, and never total deltas across such rows.

One formula is the exception. When `trading.netCash` checks whether the roots explain net cash, it reads a missing quantity, price, accrued or fee as 0. That affects only the explanation. Each of those fields still compares as above.

## Ambiguity

An entry is `Ambiguous` when a pass found more than one way to match its rows and had no rule to choose. RecDiffy doesn't guess.

- **Pair passes** (`match.sharedRef`, `match.naturalKey`, `match.reducedKey`): a key has rows on both sides, and more than one row on at least one side.
- **Group passes** (`match.groupSum`): two subsets fit the same target, a subset would take only some of several rows with identical amounts, or two targets need the same row.

A row that is ambiguous in one pass can still be matched by a later pass. It is reported `Ambiguous` only when no pass commits it. The ambiguous rows share one entry, its trail records the verdict `Contest`, and each row's disposition is `Ambiguous`.

To resolve ambiguity, add a key field that tells the rows apart, or add a pass that runs earlier with a stricter key. The cases are in [CascadeTests.cs](../src/RecDiffy.Tests/CascadeTests.cs), for example `Leaves_a_one_to_many_key_ambiguous` and `Treats_a_partial_class_as_a_contest`.

## Incomplete search

`SearchIncomplete` means a group pass stopped before it reached a decision. The rows may match or may not. Don't report them as unmatched.

`entry.Explanation.Trail[0].Detail` gives the reason:

- **`BlockTooLarge`:** the component had more rows on the many side than `limits.maxBlockItems`, or more target rows than `limits.maxTargetsPerComponent`. It was never searched.
- **`BudgetExhausted`:** the search used `limits.maxNodesPerTarget` nodes without finishing.

Narrow the pass's `block` first. Raise the limits only if the block is right, because the search cost grows quickly with block size. See [Search limits](PROFILES.md#limits-search-limits).

## Quarantine

A quarantined row has input that RecDiffy can't trust. It is never matched or compared.

- **Why:** any diagnostic with severity `Invalid`, such as `DecimalParse`, `MissingKey`, `DuplicateConflict` or `MissingLifecycleTarget`. The full list is in [PROFILES.md](PROFILES.md#run-diagnostics).
- **What you see:** `Disposition` is `Quarantined`, `Validity` is `Invalid`, `EntryId` is null, and `Diagnostics` names the cause.
- **Warnings don't quarantine.** A row with only warnings, such as `DuplicateRowCollapsed`, is matched normally. Its validity is `ValidWithWarnings`.
- **Quarantine wins.** A row that is both invalid and suppressed, or invalid and part of a match, is reported `Quarantined`.

Each `Diagnostic` has a `Code`, a `Severity`, the `RuleId` that raised it, and, when there is one, the `Field` it concerns and the `RawText` involved. The sample [ResultReading.cs](../src/RecDiffy.Samples/ResultReading.cs) shows a row quarantined for `MissingKey`.

## Suppression

When a profile declares lifecycle consolidation for a side, cancels and corrections on that side are collapsed before matching. Only the surviving row of each chain is matched. The rows it replaced are suppressed, and `RowResult.Suppression` says why:

| Reason | Meaning |
|---|---|
| `Superseded` | This row didn't survive its chain: a later correction replaced it, or a cancel removed it while another event survived. |
| `CancelEvent` | This row is a cancel, in a chain that still has a survivor. |
| `FullyCancelled` | Every event in the chain was cancelled, so nothing survived. |

- **A superseded row or cancel event** links to the survivor's entry through `EntryId`, so you can trace a match back to every event behind it.
- **A fully cancelled row** has no entry.
- **The survivor's entry** has the `LifecycleCollapsed` composition flag.
- **If the survivor is then merged into a package,** the links and the flag are lost. See [PROFILES.md](PROFILES.md#known-gaps).
- **A broken chain** is quarantined, not suppressed. See `LifecycleConflict`, `LifecycleCycle`, `MissingLifecycleTarget` and `ChainMemberQuarantined` in [PROFILES.md](PROFILES.md#run-diagnostics).

The behaviour is in [Lifecycle.cs](../src/RecDiffy/Consolidation/Lifecycle.cs) and tested in [LifecycleTests.cs](../src/RecDiffy.Tests/LifecycleTests.cs).

## Provenance

Every number in a result can be traced back to its input.

- **`SourceRef`** identifies an input row by side, row key and position. `Source` is the side name, `left` or `right`, not the name you pass to `RecordSources`. `Ordinal` is the row's position in its source.
- **`ResultEntry.Id`** is a hash of its members' sides and row keys. It doesn't depend on row order or position, so the same rows get the same id in every run (`Keeps_the_group_id_when_the_rows_are_swapped` in [DiffTests.cs](../src/RecDiffy.Tests/DiffTests.cs)).
- **`ResultEntry.PathId`** is the path that handled the rows.
- **`Explanation.CommittedBy`** names the pass that decided the entry: the pass that matched it, or the pass that found it ambiguous or stopped its search. Check `Alignment` before you read it as a match ([#258](https://github.com/m-vaysman/recdiffy.net/issues/258)).
- **`Explanation.Trail`** records the pass, its verdict (`Committed`, `Contest`, `SearchIncomplete` or `NoCandidate`), the key, the rows, and a detail code. The key is the value form a person can read, not the matcher's internal key: a pass on the field `Ref` shows `T-1`, and a pass on two fields shows their values in key order joined by ` · `, such as `T-1 · 2026-11-05`. A committed group pass keys on its target row's row key. An ambiguous entry's trail has no key today ([#257](https://github.com/m-vaysman/recdiffy.net/issues/257)). `Explanation.Hints` is always empty in this version.
- **`result.Raw(row, column)`** returns a source cell as text. `result.Canonical(row)` returns the row's values after normalization. `result.Trace(row)` lists each rule that wrote a field, with the raw and canonical text. For a leg of a package, all three read the package's combined item, not the leg: `Raw` returns only the cells every leg holds the same, and null for the others.
- **`result.Run`** records the profile hash, the reference data id and hash, and each side's culture, so you can tell which configuration produced the result.

Rows that no route accepts get their own `LeftOnly` or `RightOnly` entry with an empty `PathId`, a `NoCandidate` trail step from `route`, and the detail `Unrouted` ([RunSummaryAccountingTests.cs](../src/RecDiffy.Tests/RunSummaryAccountingTests.cs)). An unmatched row that a pass saw but couldn't pair has an empty trail.

## Counting

`RunSummary` mixes two units. Keep them apart in reports.

| Count | Unit | What it counts |
|---|---|---|
| `Matched` | entries | Entries with alignment `Matched`. |
| `Exact`, `WithinTolerance`, `Break` | entries | Matched entries by outcome. |
| `LeftOnly`, `RightOnly` | entries | Unmatched entries. Each holds one row after consolidation: a single input row, or the identical copies or package legs it stands for. |
| `Ambiguous`, `SearchIncomplete` | entries | Clusters. Each can hold many rows. |
| `Quarantined`, `Suppressed` | rows | Input rows with that disposition. |

As the [README](../../README.md#understand-the-result) notes, one matched entry can account for several input rows. To count rows by outcome, group `result.Rows` by `Disposition`.

## Before you publish a report

- **Count rows and entries separately.**
- **Show `Ambiguous` and `SearchIncomplete` apart from unmatched rows.** They mean "not decided", not "no counterpart".
- **Show quarantined rows with their diagnostics.** They were never compared.
- **Check `IsNull` before reading a delta.**
- **Record `result.Run`** next to the report, so it can be reproduced.
