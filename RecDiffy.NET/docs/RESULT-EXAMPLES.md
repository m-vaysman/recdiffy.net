# Result examples

This page shows what the engine actually returns for each concept in [What a result means](RESULTS.md): alignment, composition, field outcome, source-row disposition, ambiguity, quarantine, suppression, incomplete search and provenance. That guide says what each value means. This page shows the values in real runs.

Every block of output below is real engine output. [ResultExamplesTests.cs](../src/RecDiffy.Tests/ResultExamplesTests.cs) runs each example and asserts the text shown here, so a change in the engine fails those tests before it makes this page wrong.

There are three runs:

1. [A ledger against a bank statement](#a-ledger-against-a-bank-statement): breaks, a tolerance, an ambiguous key, unmatched rows and a quarantined row.
2. [Bins against a book](#bins-against-a-book): many rows matched to one, and a block too large to search.
3. [Trade events](#trade-events): a lifecycle chain that suppresses rows.

## A ledger against a bank statement

The ledger is the left side and the bank is the right. The profile matches rows one to one on `Ref`, and compares `Amount` to two places with an absolute tolerance of 0.05.

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

Fifteen rows became eight entries. `Summary.Ambiguous` is 1, but three rows are ambiguous, and `Summary.Matched` is 4 for eight matched rows. See [Counting](RESULTS.md#counting).

### Matched isn't balanced

Four entries are matched, and only one of them is exact.

- **L2 and B2** match on `A-2`, but their amounts differ by 5.00. That's a `Break`.
- **L3 and B3** differ by 0.03, inside the 0.05 tolerance. That's `WithinTolerance`: accepted, but not equal. The delta is still 0.03.
- **L4 and B4** compare a blank with 0.00. That's a `Break` too.

The summary says `Matched = 4` and `Break = 2`. A count of matched entries counts correspondences, not balances. See [Matched doesn't mean balanced](RESULTS.md#matched-doesnt-mean-balanced).

### Missing isn't zero

Four rows in this run are missing something, and none of them reads as 0. See [Missing isn't zero](RESULTS.md#missing-isnt-zero).

- **A blank value is unknown.** L4's amount is blank and B4's is 0.00, so the pair is a `Break`, not `Exact`. On that `FieldDiff`, `Left.IsNull` is true, `result.Format(diff.Left)` is an empty string, and `Delta.Number` is **0**. A break's delta alone doesn't size it: check `IsNull` on each side.
- **A missing row isn't a zero row.** L5 has no partner. It isn't "40.00 against 0": its entry is `LeftOnly` and `NotCompared`, with no diffs. It counts in `Summary.LeftOnly`, not in `Summary.Break`.
- **An unreadable value isn't compared.** L6's `n/a` didn't become 0, so B6 isn't a break of 80.00. B6 is `RightOnly`, and L6 is `Quarantined`.
- **An undecided row isn't unmatched.** L7, B7 and B8 are `Ambiguous`. None of them is `LeftOnly` or `RightOnly`.

### Ambiguity

L7 has two partners with the same `Ref`, B7 and B8:

```text
Ambiguous  None      NotCompared      L7 | B7 B8
```

- **Nothing was picked or compared.** All three rows are `Ambiguous`, and the entry is `NotCompared` with no diffs.
- **The trail names the pass.** The entry's explanation has one attempt, from the `ref` pass, with the verdict `Contest` and the candidates L7, B7 and B8.

Two open issues may change what this entry says about itself. [#257](https://github.com/m-vaysman/recdiffy.net/issues/257): the `Contest` attempt's `Key` is null, though the rows share a key. [#258](https://github.com/m-vaysman/recdiffy.net/issues/258): `Explanation.CommittedBy` is `ref` on this entry too, and the Excel add-in reads it as "Matched by". The tests check neither value, so this page stays true either way. Read `Alignment` first. See [Ambiguity](RESULTS.md#ambiguity).

### Quarantine

L6's amount is `n/a`:

```text
left:L6    Quarantined  Invalid  DecimalParse Invalid on Amount, raw "n/a"
```

- **L6 has no entry.** Its `EntryId` is null.
- **Its partner is unmatched.** B6 has nothing left to match, so it's `RightOnly`.
- **The summary counts rows.** `Summary.Quarantined` is 1: one row, not one entry.

See [Quarantine](RESULTS.md#quarantine).

### Provenance

Each matched entry's trail has one attempt. For L2 and B2 it's the `ref` pass, with the verdict `Committed` and the candidates L2 and B2, and `CommittedBy` is `ref`. A `LeftOnly` or `RightOnly` entry has an empty trail and no `CommittedBy`.

For a row, `result.Raw` gives the source's text, `result.Canonical` the value after normalization, and `result.Trace` each normalization step:

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

- **`left` and `right` are sides.** `SourceRef.Source` is the side, not the name the run gave each record source (`ledger` and `bank` here). `#1` is the row's `Ordinal`, its position on its side from 0.
- **A blank stays blank.** L4's cell is empty, so the parse step read no value and wrote none.
- **A failure says why.** L6's parse read `n/a` and wrote nothing, and L6's `DecimalParse` diagnostic gives the reason.
- **The run is recorded.** `result.Run.ProfileHash` is 64 lowercase hex characters, `ReferenceId` is `empty` for `ReferenceDataSet.Empty`, and `Reading` shows both sides read with the invariant culture, declared by the run.
- **Entry ids don't depend on row order.** The same rows in reverse order give the same entry ids.

See [Provenance](RESULTS.md#provenance).

### Totals leave rows out

`result.BuildSideBySide` with an `Amount` measure and no dimensions gives one cell:

```text
SideBySideCell { Left = 440.00, Right = 505.03, Delta = 65.03, LeftOnly = -40.00, RightOnly = 110.00, MatchedDelta = -4.97 }
```

- **Only matched, left-only and right-only rows are in it.** L6 (quarantined), and L7, B7 and B8 (ambiguous), aren't in these totals at all. Suppressed and search-incomplete rows are left out the same way.
- **Unmatched amounts have their own columns.** `LeftOnly` (shown negative) and `RightOnly` hold them, apart from `MatchedDelta`. The matched rows alone differ by -4.97.
- **A blank amount adds 0 to a total.** L4's blank adds nothing to `Left`. Its break is in the entry's diffs, not in these totals.

So a side-by-side total isn't a file total. Read it with the summary, which says how many rows it doesn't cover.

## Bins against a book

The bins and book from the [README's quickstart](../../README.md#quickstart-your-first-reconciliation). A group pass matches several bins to one book position by SKU, and a natural-key pass then pairs what is left.

```text
Matched    ManyToOne  Exact            A1 A2 | BOOK-1
Matched    OneToOne   Break            B1 | BOOK-2  Quantity: 3 vs 5, delta 2
LeftOnly   None       NotCompared      C1 | -
```

- **One entry, three rows.** A1, A2 and BOOK-1 share one entry, and all three rows point at it. Three entries hold six rows.
- **The group is compared as one row.** `result.Projection(entry.Id, side)` returns the row each side was compared with: a quantity of 10 on each side, for 4 + 6 against 10.
- **The group pass committed it.** `CommittedBy` is `bins`.

See [Grouped entries](RESULTS.md#grouped-entries).

### Incomplete search

With the same profile, one SKU, bins that each hold 1, and one book position of 3:

| Bins | Entry | Trail |
|---|---|---|
| 24 | `Ambiguous` | `bins Contest`: many sets of three bins add up to 3. |
| 25 | `SearchIncomplete` | `bins SearchIncomplete BlockTooLarge`: one bin over the default limit of 24 items in a block, so the pass didn't search. |

Each run has one entry that holds every row, and every row has that entry's disposition. Nothing is `LeftOnly` or `RightOnly`, and nothing is `Matched`. A `SearchIncomplete` row may have a match. The engine didn't look far enough to say. See [Incomplete search](RESULTS.md#incomplete-search).

## Trade events

The street (right) sends trade events, and the book (left) holds one row per live trade. The profile collapses each chain of street events to the event that survives.

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

- **The survivor is matched.** E2 corrects E1, so E2 survives and matches L1. The entry's composition has the `LifecycleCollapsed` flag.
- **A superseded row points at its survivor.** E1 is suppressed, but its `EntryId` is the entry of L1 and E2.
- **A cancelled trade isn't a zero trade.** E3 and E4 cancel out and have no entry. The book still holds T-2, so L2 has nothing to match and is `LeftOnly`.
- **A broken chain is quarantined, not suppressed.** E5 cancels E9, which isn't in the file, so E5 is `Quarantined` with `MissingLifecycleTarget`.
- **The summary counts rows.** `Summary.Suppressed` is 3 and `Summary.Quarantined` is 1.

See [Suppression](RESULTS.md#suppression).
