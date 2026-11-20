# Breaking changes

1.2.0 is the first stable release. From 1.2.0 on, a breaking change to the public API waits for a new major version. Before it, the API could change from one preview to the next. Each entry lists what breaks and how to update.

## 1.3.0-preview.1 — push and snapshot, and the first RecDiffy.Fix

Nothing breaks. Since `1.2.0` the public API of `RecDiffy` only gains members, and `RecDiffy.IO`, `RecDiffy.Excel` and `RecDiffy.RulePacks.Trading` are unchanged. Code built against `1.2.0` builds and runs as before.

New in `RecDiffy`:

- **Push and snapshot (#527).** `profile.Push(left, right)` starts a `RecBatch`, `batch.Push(left, right)` adds rows, and `batch.Snapshot()` reconciles every row pushed so far and returns a `RecView` with the result and its breaks. Every call returns a new value. `Push(previous)` carries the rows a previous result left open into the next batch. A snapshot with nothing pushed since the last one runs nothing.
- **Spellings and opposites in `RecBuilder` (#511, #512).** `Value("BUY", accepts: ["B", "BOT", "BUY"])` makes a key match every spelling of a value as one. `Opposite("BUY", "SELL")` makes a key match opposite values, such as our buy and the broker's sell.

New package `RecDiffy.Fix` (#528, #532), at the same version and depending on `RecDiffy` at that version:

- **`FixReader`** reads FIX tag=value messages strictly, against the spec. A malformed message comes back as `FixRejected` with its reason, never guessed at.
- **`FixFlattener`** turns each message into a row whose columns are its tags, repeating groups included. The profile decides what each value means; nothing is built in.
- **Scenario docs.** [docs/fix](https://github.com/m-vaysman/recdiffy.net/blob/main/docs/fix/README.md) walks through orders, fills, and equity, futures and bond allocations, each with sample messages and the profile in C#.

`RecDiffy.Fix` stays a prerelease, even on a stable tag: install it with `dotnet add package RecDiffy.Fix --prerelease`.

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
