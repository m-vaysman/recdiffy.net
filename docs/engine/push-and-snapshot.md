# Push rows in batches

Part of the [engine section](llms.txt). The `RecPush` extension methods, in the `RecDiffy` namespace, keep every row pushed so far and reconcile them when you ask. Each call returns a new value and changes nothing.

```csharp
// A row is its source cells by column, as text: any IReadOnlyDictionary<string, string?>.
var batch = profile.Push(left: ourRows, right: theirRows);

var (view, current) = batch.Snapshot();   // runs Reconciler.Run over every row pushed so far
var breaks = view.Breaks;                 // the entries whose outcome is Break
var result = view.Result;                 // the full IReconciliationResult

batch = current.Push(moreOurRows, moreTheirRows);   // adds rows; the next snapshot runs again

var tomorrow = profile.Push(view.Result);   // a new batch holding the rows this result left open
```

| Member | What it does |
|---|---|
| `profile.Push(left, right, reference)` | Starts a `RecBatch` with rows for each side. `reference` defaults to `ReferenceDataSet.Empty`. |
| `profile.Push(previous, reference)` | Starts a batch with the rows a previous result left open. |
| `batch.Push(left, right)`, `batch.Push(previous)` | Adds rows to a batch. |
| `batch.Snapshot()` | Returns `(View, Batch)`: a `RecView` with `Result`, `Breaks` and `Version`, and the batch to keep. |
| `batch.IsCurrent` | Whether the last snapshot covers every push, so the next one runs nothing. |
| `batch.Version` | How many pushes the batch holds. |

- **`Snapshot()` runs every pushed row again.** It isn't incremental. Keep the batch it returns: snapshotting that batch again, with no push in between, returns the same view without running.
- **`Push(previous)` carries what a result left open:** the rows of every entry that isn't `Matched`, the rows their lifecycle chains replaced, and quarantined rows. Each is rebuilt from `Raw` with the row-key column and the columns the profile's rules read, so other columns are dropped.
- **Pushed cells are text.** Each one reaches the profile's rules as a `Text` cell, so a decimal column is read by `decimal.parse` in the side's culture.

The code is [RecDiffy/Push](../../RecDiffy.NET/src/RecDiffy/Push), and the tests are [RecPushTests.cs](../../RecDiffy.NET/src/RecDiffy.IO.Tests/RecPushTests.cs).
