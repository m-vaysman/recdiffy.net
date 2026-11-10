# What a result means

`Reconciler.Run` returns a `ReconciliationResult`. This guide explains how to read one: what each part of it answers, why a matched entry can still be a break, and why a missing value or a missing row is never zero.

Every example below is real engine output. [ResultConceptsGuideTests.cs](../src/RecDiffy.Tests/ResultConceptsGuideTests.cs) runs each one and checks the text shown here, so a change in the engine fails those tests before it makes this guide wrong. The types are in [ResultModel.cs](../src/RecDiffy/Results/ResultModel.cs) and [Alignment.cs](../src/RecDiffy/Results/Alignment.cs).

This guide reads results. To write the profile that produces them, see the [profile reference](PROFILES.md).

## In short

- **Matched isn't balanced.** Alignment says which rows belong together. The outcome says whether their values agree. A `Matched` entry can be a `Break`.
- **Missing isn't zero.** A blank value is unknown, not 0. A row with no partner is unmatched, not matched to 0. A row the engine couldn't decide on is neither matched nor unmatched.
- **Entries aren't rows.** One entry can hold many rows, and some rows have no entry. Count each where it lives.

## The parts of a result

| Member | One per | Holds |
|---|---|---|
| `Entries` | decision: a match, or rows left unmatched or undecided | Alignment, composition, outcome, field differences, explanation |
| `Rows` | source row | Disposition, validity, diagnostics, and the entry that holds the row |
| `Summary` | run | Counts of entries and rows |
| `Run` | run | Profile hash, reference data, engine version, and how each side was read |

Four separate questions are answered in four separate places. None of them implies another.

| Concept | Where | Question | Values |
|---|---|---|---|
| Alignment | `ResultEntry.Alignment` | Did rows correspond across the two sides? | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly` |
| Composition | `ResultEntry.Composition` | How many rows did each side give, and did consolidation build any of them? | Flags: `OneToOne`, `ManyToOne`, `OneToMany`, `Package`, `LifecycleCollapsed`. `None` when nothing matched. |
| Field outcome | `ResultEntry.Outcome` and `FieldDiff.Outcome` | Do the compared values agree? | `Exact`, `WithinTolerance`, `Break`, `NotCompared` |
| Source-row disposition | `RowResult.Disposition` | What happened to this input row? | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly`, `Quarantined`, `Suppressed` |

## The example run

A ledger (left) against a bank statement (right). The profile matches rows one to one on `Ref`, and compares `Amount` to two places with an absolute tolerance of 0.05.

| Ref | Ledger (left) | Bank (right) |
|---|---|---|
| A-1 | L1: 100.00 | B1: 100.00 |
| A-2 | L2: 250.00 | B2: 245.00 |
| A-3 | L3: 50.00 | B3: 50.03 |
| A-4 | L4: *blank* | B4: 0.00 |
| A-5 | L5: 40.00 | |
| A-6 | L6: `n/a` | B6: 80.00 |
| A-7 | L7: 60.00 | B7: 60.00 and B8: 60.00 |
| A-9 | | B9: 30.00 |

The entries, one per line: alignment, composition, outcome, the rows on each side, and every field that isn't exact. The delta is right minus left.

```text
Matched    OneToOne  Exact            L1 | B1
Matched    OneToOne  Break            L2 | B2    Amount: 250.00 vs 245.00, delta -5.00
Matched    OneToOne  WithinTolerance  L3 | B3    Amount: 50.00 vs 50.03, delta 0.03
Matched    OneToOne  Break            L4 | B4    Amount: (blank) vs 0.00, delta 0
Ambiguous  None      NotCompared      L7 | B7 B8
LeftOnly   None      NotCompared      L5 | -
RightOnly  None      NotCompared      - | B6
RightOnly  None      NotCompared      - | B9
```

Every source row, with its disposition, its validity and any diagnostic:

```text
left:L1    Matched      Valid
left:L2    Matched      Valid
left:L3    Matched      Valid
left:L4    Matched      Valid
left:L5    LeftOnly     Valid
left:L6    Quarantined  Invalid  DecimalParse Invalid on Amount, raw "n/a"
left:L7    Ambiguous    Valid
right:B1   Matched      Valid
right:B2   Matched      Valid
right:B3   Matched      Valid
right:B4   Matched      Valid
right:B6   RightOnly    Valid
right:B7   Ambiguous    Valid
right:B8   Ambiguous    Valid
right:B9   RightOnly    Valid
```

And the summary:

```text
RunSummary { Matched = 4, Exact = 1, WithinTolerance = 1, Break = 2, LeftOnly = 1, RightOnly = 2, Ambiguous = 1, SearchIncomplete = 0, Quarantined = 1, Suppressed = 0 }
```

Fifteen rows became eight entries. The sections below take this run apart.

## Alignment

Alignment belongs to an entry. It says whether the engine found the other side of the rows in it.

| Alignment | Meaning | In the example |
|---|---|---|
| `Matched` | A pass committed these rows as one group. | L1 to L4 with B1 to B4 |
| `Ambiguous` | A pass found more than one way to pair these rows, so it paired none of them. | L7 with B7 and B8 |
| `SearchIncomplete` | A group pass stopped before it could decide. | See [Incomplete search](#incomplete-search). |
| `LeftOnly`, `RightOnly` | No pass found a partner. | L5; B6 and B9 |

`Matched` says only that the rows correspond. It says nothing about their values. That is the outcome's job.

A row that no route accepts never reaches a pass, but it still gets a `LeftOnly` or `RightOnly` entry. That entry has an empty `PathId`, and its trail says `Unrouted`. `RunSummaryAccountingTests` covers this.

## Composition

Composition belongs to an entry. It says how many rows each side gave. The bins and book from the [README's quickstart](../../README.md#quickstart-your-first-reconciliation) give one entry that matches two bins to one book position:

```text
Matched    ManyToOne  Exact            A1 A2 | BOOK-1
Matched    OneToOne   Break            B1 | BOOK-2  Quantity: 3 vs 5, delta 2
LeftOnly   None       NotCompared      C1 | -
```

- **`OneToOne`:** one row on each side.
- **`ManyToOne`:** several left rows and one right row. `OneToMany` is one left row and several right rows.
- **`Package` and `LifecycleCollapsed`:** added to the flags above when consolidation built one of the rows. A package nets several legs into one row. A lifecycle chain collapses to its surviving event, as in the [suppression example](#suppression), whose match is `OneToOne, LifecycleCollapsed`.
- **`None`:** the entry isn't matched.

A many-row entry is compared as one row per side, with its compared amounts summed. `result.Projection(entry.Id, side)` returns that row: here 10 on each side, for A1 + A2 against BOOK-1. Only a matched entry has a projection.

These three entries hold six rows, and the first one alone holds three.

## Field outcome

A matched entry compares each `Key` and `Economic` field on the two sides.

| Outcome | Meaning |
|---|---|
| `Exact` | The values are equal. Decimals are rounded to the field's scale first. Two blank values are also `Exact`. |
| `WithinTolerance` | The values differ, but by no more than the field's tolerance. |
| `Break` | The values differ by more than the tolerance, or one side is blank. |
| `NotCompared` | Nothing was compared, because the entry isn't matched. A field whose condition holds on neither side isn't compared either. |

- **`Diffs`** lists only the fields that are `WithinTolerance` or `Break`. An empty list means every compared field was `Exact`.
- **The entry's outcome** is its worst field: any `Break` makes it `Break`, and otherwise any `WithinTolerance` makes it `WithinTolerance`.
- **The delta** is right minus left: a number for a decimal field, and days for a date. L2 against B2 has a delta of -5.00, because the bank shows 5.00 less.

### Why matched doesn't mean balanced

The example has four matched entries, and only one of them is exact.

- **L2 and B2** match on `A-2`, but their amounts differ by 5.00. That's a `Break`.
- **L3 and B3** differ by 0.03, inside the 0.05 tolerance. That's `WithinTolerance`: accepted, but not equal. The delta is still 0.03.
- **L4 and B4** compare a blank with 0.00. That's a `Break` too: see [Why missing isn't zero](#why-missing-isnt-zero).

The summary says `Matched = 4` and `Break = 2`. Report both. A count of matched entries counts correspondences, not balances. The [side-by-side totals](#totals-leave-rows-out) of the same run show it as well: the matched rows alone differ by -4.97.

## Source-row disposition

Disposition belongs to a source row. Each source row appears in `Rows` exactly once, even when an entry holds it with others.

A row takes its entry's alignment, unless one of two things happened to it first. In order of precedence:

1. **`Quarantined`:** the row is invalid, so it never reached matching. This wins over everything else.
2. **`Suppressed`:** consolidation removed the row before matching.
3. **The entry's alignment:** `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly` or `RightOnly`.

A `RowResult` also has:

- **`EntryId`:** the entry that holds the row. It's null on a quarantined row. A suppressed row points at the entry its chain's survivor is in, when the chain has one.
- **`Validity`:** `Valid`, `ValidWithWarnings` or `Invalid`. `Invalid` always means `Quarantined`. A row with warnings still matches.
- **`Diagnostics`:** each problem's code, severity, rule, field and raw text.
- **`Suppression`:** why a suppressed row was removed.

## Ambiguity

An ambiguous entry holds rows that a pass could pair in more than one way. The engine doesn't guess. It pairs none of them, compares nothing, and reports every contested row.

In the example, L7 has two partners with the same `Ref`, B7 and B8:

```text
Ambiguous  None      NotCompared      L7 | B7 B8
```

- **All three rows are `Ambiguous`.** None of them is `LeftOnly` or `RightOnly`, and none was compared.
- **The trail names the pass.** The entry's explanation has one attempt, from the `ref` pass, with the verdict `Contest` and the candidates L7, B7 and B8.
- **A group pass can be ambiguous too.** Twenty-four bins that each hold 1, against a book position of 3, have many sets of three bins that add up. The whole block comes back as one `Ambiguous` entry, with the verdict `Contest`.

The result doesn't choose a pairing for you. Resolve an ambiguity in the data, or with a key that tells the rows apart.

> **Pending change.** Two open issues change what an ambiguous entry says about itself, and the engine output above may change with them:
>
> - [#257](https://github.com/m-vaysman/recdiffy.net/issues/257): the `Contest` attempt's `Key` is null, though the contested rows share a key.
> - [#258](https://github.com/m-vaysman/recdiffy.net/issues/258): `Explanation.CommittedBy` also names a pass on an ambiguous entry (`ref` here), and the Excel add-in reads it as "Matched by".
>
> Whatever they decide, read `Alignment` first. `CommittedBy` names the matching pass only on a `Matched` entry, and the contested rows are always the attempt's candidates.

## Quarantine

A quarantined row has a diagnostic of `Invalid` severity. It's kept out of matching, and it isn't counted as unmatched.

In the example, L6's amount is `n/a`:

```text
left:L6    Quarantined  Invalid  DecimalParse Invalid on Amount, raw "n/a"
```

- **L6 has no entry.** Its `EntryId` is null.
- **Its partner is unmatched.** B6 has nothing left to match, so it's `RightOnly`. The bank's 80.00 isn't compared with anything.
- **The summary counts rows.** `Summary.Quarantined` is 1: one row, not one entry.

Other causes:

- **A missing row key** is `MissingKey`. The [ResultReading sample](../src/RecDiffy.Samples/ResultReading.cs) shows one.
- **A lifecycle chain that can't be resolved** has a code such as `MissingLifecycleTarget`, `LifecycleCycle`, `LifecycleConflict` or `ChainMemberQuarantined`. The [suppression example](#suppression) has a `MissingLifecycleTarget`.

The README's [Troubleshooting](../../README.md#troubleshooting) table lists what to check for the common codes.

## Suppression

Suppression is consolidation removing a row on purpose, before matching. In this version only lifecycle consolidation suppresses rows: it collapses each chain of trade events to the event that survives.

Here the street (right) sends trade events, and the book (left) holds one row per live trade:

| Row | Side | Ref | Action | Target | Qty |
|---|---|---|---|---|---|
| L1 | left | T-1 | | | 110 |
| L2 | left | T-2 | | | 50 |
| E1 | right | T-1 | NEW | | 100 |
| E2 | right | T-1 | CORR | E1 | 110 |
| E3 | right | T-2 | NEW | | 50 |
| E4 | right | T-2 | CANC | E3 | 50 |
| E5 | right | T-3 | CANC | E9 | 70 |

```text
Matched    OneToOne, LifecycleCollapsed  Exact            L1 | E2
LeftOnly   None                          NotCompared      L2 | -
```

Each row, with its suppression reason or quarantine code, and the entry it points at:

```text
left:L1    Matched                              entry L1 | E2
left:L2    LeftOnly                             entry L2 | -
right:E1   Suppressed   Superseded              entry L1 | E2
right:E2   Matched                              entry L1 | E2
right:E3   Suppressed   FullyCancelled          entry none
right:E4   Suppressed   FullyCancelled          entry none
right:E5   Quarantined  MissingLifecycleTarget  entry none
```

| Reason | Meaning | Example |
|---|---|---|
| `Superseded` | A later event in the chain replaced this one. | E1, corrected by E2 |
| `CancelEvent` | The row is a cancel in a chain that still has a survivor. | `LifecycleTests.Collapses_a_cancel_correct_chain_to_the_correction` |
| `FullyCancelled` | The chain was cancelled, so nothing survives. | E3 and E4 |

- **E1 points at its survivor.** E1 is suppressed, but its `EntryId` is the entry of L1 and E2. Follow it to see what the chain became.
- **A cancelled trade isn't a zero trade.** E3 and E4 cancel out and have no entry. The book still holds T-2, so L2 has nothing to match and is `LeftOnly`.
- **A broken chain is quarantined, not suppressed.** E5 cancels E9, which isn't in the file. That's an error in the data, so E5 is `Quarantined` with `MissingLifecycleTarget`.
- **The summary counts rows.** `Summary.Suppressed` is 3 and `Summary.Quarantined` is 1.

## Incomplete search

A group pass (`match.groupSum`) searches for sets of rows whose sums agree. The search is bounded. When a block is over the limits, or the search runs out of budget, the pass stops without a decision, and its rows are `SearchIncomplete`.

With the same warehouse profile as the composition example, one SKU, bins that each hold 1, and one book position of 3:

| Bins | Entry | Trail |
|---|---|---|
| 24 | `Ambiguous` | `Contest`: many sets of three bins add up to 3. |
| 25 | `SearchIncomplete` | `SearchIncomplete` with the detail `BlockTooLarge`: one bin over the default limit of 24 items in a block, so the pass didn't search. |

Each run has one entry that holds every row, and every row has that entry's disposition. Nothing is `LeftOnly` or `RightOnly`.

The trail's detail says why the search stopped:

- **`BlockTooLarge`:** the block had more items, or more targets, than the limits allow, so the pass didn't search it. `AdversarialSearchLimitTests` checks both limits at their boundaries.
- **`BudgetExhausted`:** the pass searched the block, but used up its node budget before it decided. [Decision.cs](../src/RecDiffy/Matching/Decision.cs) sets this code.

`SearchIncomplete` doesn't mean unmatched. A match may exist, but the engine didn't look far enough to say. Don't report these rows as left only or right only. Check the block's size and its blocking fields first. The limits are in the [profile reference](PROFILES.md).

## Provenance

Every value in a result traces back to the input and the run that produced it.

### The run

| `result.Run` | What it records |
|---|---|
| `ProfileHash` | A SHA-256 hash of the profile in a canonical form, as 64 lowercase hex characters |
| `ReferenceId`, `ReferenceHash` | The reference data set the run used: `empty` and an empty hash for `ReferenceDataSet.Empty` |
| `EngineVersion` | The version of the engine assembly |
| `Reading` | For each side: the culture, its separators, where the culture came from, and whether date formats were strict |

### Entries

- **`Id`** is a hash of the side and row key of each row in the entry. The same rows get the same id in any input order.
- **`PathId`** names the path that decided the entry.
- **`Explanation.CommittedBy`** names the pass that matched a `Matched` entry. It's null on a `LeftOnly` or `RightOnly` entry. See the note under [Ambiguity](#ambiguity) for other entries.
- **`Explanation.Trail`** lists the pass attempts. Each `PassAttempt` has a pass id, a verdict, a key, the candidate rows and a detail code. In this version an entry has at most one attempt:

| Verdict | Recorded on |
|---|---|
| `Committed` | A `Matched` entry. `Key` is the match key, and `Candidates` are the matched rows. |
| `Contest` | An `Ambiguous` entry. `Candidates` are the contested rows. |
| `SearchIncomplete` | A `SearchIncomplete` entry. `Detail` is `BlockTooLarge` or `BudgetExhausted`. |
| `NoCandidate` | The entry of a row no route accepted. The pass id is `route`, and `Detail` is `Unrouted`. |

A `LeftOnly` or `RightOnly` entry on a path has an empty trail. `Key` holds the engine's own separators between parts, so display it but don't parse it. `NoKey` and `Vetoed` are declared, but no pass records them yet, and `Hints` is always empty.

### Rows

`RowResult.Row` is a `SourceRef`:

- **`Source`** is the side, `left` or `right`, not the name you gave the record source.
- **`RowKey`** is the value of the side's row key column.
- **`Ordinal`** is the row's position on its side, from 0.

From a `SourceRef`:

- **`result.Raw(row, column)`** is the text the source gave.
- **`result.Canonical(row)[field]`** is the value after normalization.
- **`result.Trace(row)`** lists each normalization step: the rule, the field, the input it read and the value it wrote.

```text
right:B2 #1
  raw Amount "245.00", canonical Amount 245.00
  identifier.map  Ref     "A-2" -> "A-2"
  decimal.parse   Amount  "245.00" -> "245.00"
left:L4 #3
  raw Amount "", canonical Amount (blank)
  identifier.map  Ref     "A-4" -> "A-4"
  decimal.parse   Amount  (none) -> (none)
left:L6 #5
  raw Amount "n/a", canonical Amount (blank)
  identifier.map  Ref     "A-6" -> "A-6"
  decimal.parse   Amount  "n/a" -> (none)
```

L4's cell is empty, so the parse step read no value and wrote none. L6's parse read `n/a` and wrote none, and L6's `DecimalParse` diagnostic says why.

## Why missing isn't zero

RecDiffy keeps "no value" and "no row" apart from 0 everywhere it compares or counts.

1. **A blank value is unknown.** L4's amount is blank and B4's is 0.00. They're a `Break`, not `Exact`. On that `FieldDiff`, `Left.IsNull` is true, `result.Format(diff.Left)` is an empty string, and `Delta.Number` is **0**. Don't size a break by its delta alone: check `IsNull` on each side. Two blank values compare `Exact`.
2. **A missing row isn't a zero row.** L5 has no partner. It isn't "40.00 against 0": its entry is `LeftOnly` and `NotCompared`, with no diffs. It counts in `Summary.LeftOnly`, not in `Summary.Break`.
3. **A quarantined row isn't compared.** L6's `n/a` didn't become 0, so B6 isn't a break of 80.00. B6 is `RightOnly`, and L6 is `Quarantined`.
4. **An undecided row isn't unmatched.** Ambiguous and search-incomplete rows aren't `LeftOnly` or `RightOnly`, and the summary counts them apart.
5. **A sum with an unknown part is unknown.** When a package nets its legs, or a matched group adds up its rows, one blank member makes the total blank, not smaller. `AdversarialPackageNullTests` checks this for packages.
6. **A cancelled trade isn't a zero trade.** In the suppression example, the street's T-2 is fully cancelled, and the book's T-2 is `LeftOnly`.

### Totals leave rows out

`result.BuildSideBySide` totals a measure by the dimensions you choose. For the example, with `Amount` and no dimensions:

```text
SideBySideCell { Left = 440.00, Right = 505.03, Delta = 65.03, LeftOnly = -40.00, RightOnly = 110.00, MatchedDelta = -4.97 }
```

- **Only matched, left-only and right-only rows are in it.** L6 (quarantined), and L7, B7 and B8 (ambiguous), aren't in these totals at all. Suppressed and search-incomplete rows are left out the same way.
- **Unmatched amounts have their own columns.** `LeftOnly` (shown negative) and `RightOnly` hold them, apart from `MatchedDelta`.
- **A blank amount adds 0 to a total.** L4's blank adds nothing to `Left`. Its break is in the entry's diffs, not in these totals.

So a side-by-side total isn't a file total. Read it with the summary, which says how many rows it doesn't cover.

## Count entries and rows separately

| `Summary` member | Counts |
|---|---|
| `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly` | Entries with that alignment |
| `Exact`, `WithinTolerance`, `Break` | Entries with that outcome. Only a matched entry has one. |
| `Quarantined`, `Suppressed` | Rows with that disposition |

In the example, `Summary.Ambiguous` is 1, but three rows are ambiguous, and `Summary.Matched` is 4 for eight matched rows. To report how many records are in each state, count `Rows` by `Disposition`.

## See also

- **[README: Understand the result](../../README.md#understand-the-result):** the short version of this guide.
- **[Samples](../src/RecDiffy.Samples/README.md):** runnable examples. `ResultReading` prints a break, a left-only row and a quarantined row, and `SideBySideReport` prints a side-by-side cell.
- **[Excel report layouts](report-specs/README.md):** how the Excel reports present these concepts.
