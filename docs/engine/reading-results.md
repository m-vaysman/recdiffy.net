# Read the result

Part of the [engine section](llms.txt). `Reconciler.Run` returns an `IReconciliationResult`. It answers four separate questions, and none implies another.

| Question | Property | Values |
|---|---|---|
| Did rows correspond across the sides? | `ResultEntry.Alignment` | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly` |
| How were rows grouped? | `ResultEntry.Composition` (flags) | `OneToOne`, `ManyToOne`, `OneToMany`, plus `Package` and `LifecycleCollapsed`; `None` when not matched |
| Do the compared values agree? | `ResultEntry.Outcome`, `FieldDiff.Outcome` | `Exact`, `WithinTolerance`, `Break`; `NotCompared` when nothing was compared |
| What happened to this input row? | `RowResult.Disposition` | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly`, `Quarantined`, `Suppressed` |

```csharp
var summary = result.Summary;   // entry counts, except Quarantined and Suppressed, which count rows

var breaks = result.Entries.Where(entry => entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Break });
foreach (var entry in breaks)
    foreach (var diff in entry.Diffs)   // only the fields that differ
        Console.WriteLine($"{diff.Field}: {result.Format(diff.Left)} vs {result.Format(diff.Right)}");

foreach (var row in result.Rows.Where(row => row.Disposition == Disposition.Quarantined))
    Console.WriteLine($"{row.Row.Source} {row.Row.RowKey}: {string.Join(", ", row.Diagnostics.Select(d => d.Code))}");
```

## Members

| Member | What it gives |
|---|---|
| `result.Entries` | One `ResultEntry` per match, ambiguous cluster, incomplete search or unmatched row: `Id`, `PathId`, `Alignment`, `Composition`, `Outcome`, `Left` and `Right` (`SourceRef`s), `Diffs`, `Explanation`. |
| `result.Rows` | One `RowResult` per input row: `Row`, `Disposition`, `Suppression`, `Validity`, `Diagnostics`, `EntryId`. |
| `result.Summary` | A `RunSummary`: `Matched`, `Exact`, `WithinTolerance`, `Break`, `LeftOnly`, `RightOnly`, `Ambiguous`, `SearchIncomplete` count entries; `Quarantined` and `Suppressed` count rows. |
| `result.Raw(row, column)` | A source cell as text. |
| `result.Canonical(row)` | The row's values after normalization. |
| `result.Trace(row)` | Each rule that wrote a field: `RuleId`, `Field`, `Raw` and `Canonical` text. |
| `result.Projection(entry.Id, side)` | The combined row a `ManyToOne` or `OneToMany` entry compared. |
| `result.Format(value)` | A canonical value as text, at its field's scale. |
| `result.BuildSideBySide(spec)` | A side-by-side view of chosen fields. |
| `entry.Explanation` | `CommittedBy`, the pass that decided the entry, and `Trail`, each pass's verdict, key and rows. Check `Alignment` before you read `CommittedBy` as a match. |
| `result.Run` | `ProfileHash`, `ReferenceId`, `ReferenceHash`, `EngineVersion`, and `Reading["left"]` and `Reading["right"]`: each side's culture and separators. |

- **`Diffs` lists only the fields that differ.** An `Exact` entry has an empty list.
- **Only matched entries are compared.** Ambiguous, incomplete and one-sided entries are `NotCompared`.
- **`Raw`, `Canonical` and `Trace` read the row after consolidation.** A package leg reads its package's combined item, and identical copies of one row key read the one row they collapsed into.
- **The result types are read-only,** and the concrete types are internal, so don't cast them.

## In depth

Every topic of [RESULTS.md](../../RecDiffy.NET/docs/RESULTS.md) has its own page: [matched doesn't mean balanced](../../RecDiffy.NET/docs/RESULTS.md#matched-doesnt-mean-balanced), [missing isn't zero](../../RecDiffy.NET/docs/RESULTS.md#missing-isnt-zero), [ambiguity](../../RecDiffy.NET/docs/RESULTS.md#ambiguity), [incomplete search](../../RecDiffy.NET/docs/RESULTS.md#incomplete-search), [quarantine](../../RecDiffy.NET/docs/RESULTS.md#quarantine), [suppression](../../RecDiffy.NET/docs/RESULTS.md#suppression), [provenance](../../RecDiffy.NET/docs/RESULTS.md#provenance) and [counting](../../RecDiffy.NET/docs/RESULTS.md#counting). [RESULT-EXAMPLES.md](../../RecDiffy.NET/docs/RESULT-EXAMPLES.md) shows real output for each.
