# Breaking changes

RecDiffy is in preview; the public API can change between previews. Each entry lists what breaks and how to update.

## 1.2.0-preview.7 — results through read-only interfaces (#408)

- `Reconciler.Run` and `RunAsync` return `IReconciliationResult` (was `ReconciliationResult`).
- `BuildSideBySide` returns `ISideBySideView` (was `SideBySideView`).
- `ReconciliationResult` and `SideBySideView` are now internal.
- Result collections are `ImmutableArray` / `ImmutableDictionary` (`Entries`, `Rows`, `Lines`, `Cells`, `ResultEntry.Left`/`Right`/`Diffs`, `PassAttempt.Candidates`, `ProfileDocument.SourceReadings`).

**To update:** declare results as `IReconciliationResult` / `ISideBySideView`; use `Length` instead of `Count` (including patterns like `Left.Length: 1`); don't cast collections to `List<T>`, `Dictionary<,>` or arrays. Reading, indexing and `foreach` are unchanged.
