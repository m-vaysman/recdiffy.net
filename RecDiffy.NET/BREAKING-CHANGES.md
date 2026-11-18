# Breaking changes

1.2.0 is the first stable release. From 1.2.0 on, a breaking change to the public API waits for a new major version. Before it, the API could change from one preview to the next. Each entry lists what breaks and how to update.

## 1.2.0 — the field comparers are internal (#408)

The one break between `1.2.0-preview.7` and `1.2.0`. Every preview up to `1.2.0-preview.7` has these types public.

- `IFieldComparer<T>`, `DecimalComparer` and `DateComparer` are now internal.

They compare two values as given. They don't round to the field's scale and they don't know the profile's tolerance, so a delta inside the tolerance came back as `Break`. The engine rounds first, calls them, then applies the tolerance.

**To update:** there is no replacement type. Declare the tolerance on the profile and read `FieldDiff.Outcome` and `FieldDiff.Delta` from the result's entries. [How a field is compared](docs/RESULTS.md#how-a-field-is-compared) lists the rules.

## 1.2.0-preview.7 — results through read-only interfaces (#408)

- `Reconciler.Run` and `RunAsync` return `IReconciliationResult` (was `ReconciliationResult`).
- `BuildSideBySide` returns `ISideBySideView` (was `SideBySideView`).
- `ReconciliationResult` and `SideBySideView` are now internal.
- Result collections are `ImmutableArray` / `ImmutableDictionary` (`Entries`, `Rows`, `Lines`, `Cells`, `ResultEntry.Left`/`Right`/`Diffs`, `PassAttempt.Candidates`, `ProfileDocument.SourceReadings`).

**To update:** declare results as `IReconciliationResult` / `ISideBySideView`; use `Length` instead of `Count` (including patterns like `Left.Length: 1`); don't cast collections to `List<T>`, `Dictionary<,>` or arrays. Reading, indexing and `foreach` are unchanged.
