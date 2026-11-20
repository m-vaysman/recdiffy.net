# Start here: RecDiffy.NET for AI agents

This page is for an AI agent that writes code **with** RecDiffy.NET. It covers installing, picking the API, and the rules that keep a result correct. The index of every page is [llms.txt](llms.txt). An agent that changes the RecDiffy.NET repository itself follows [AGENTS.md](../AGENTS.md) instead.

Every name in these docs is in the public API baseline, [RecDiffy.txt](../RecDiffy.NET/src/RecDiffy.Tests/ApiBaseline/RecDiffy.txt). When a doc and that file disagree, the file is right.

## Install

Every release is a prerelease, so `dotnet add package` needs `--prerelease`. The packages target .NET 8 and .NET 10.

```shell
dotnet add package RecDiffy --prerelease
```

| Package | Add it when you need |
|---|---|
| `RecDiffy` | The engine: profiles, matching, comparison, results, `RecBuilder`, push and snapshot. |
| `RecDiffy.IO` | To read CSV and Excel files as record sources. |
| `RecDiffy.Excel` | To write formatted Excel reports from a result. |
| `RecDiffy.RulePacks.Trading` | Trading rules: LEI and alias lookup, side resolution, absolute quantity, 32nds prices, and the net cash and pro rata attributions. |
| `RecDiffy.Fix` | To read FIX tag=value messages strictly and turn them into rows. |
| `RecDiffy.Extensions` | `DataTable` and HTML grids from a `RecResult`. |

`RecDiffy`, `RecDiffy.IO`, `RecDiffy.Excel`, `RecDiffy.RulePacks.Trading` and `RecDiffy.Fix` ship together at one version, so use the same version of each. `RecDiffy.Extensions` has its own version numbers.

## Pick the API

| You have | Use |
|---|---|
| Two lists of your own objects, matched on equal members, with a few members to compare | [RecBuilder](builders/recbuilder.md) |
| A reconciliation that needs row keys, a culture, lifecycle chains, packages, routes, a conditional tolerance, date formats or trading rules | [ProfileBuilder](builders/profilebuilder.md), or a JSON profile |
| A profile that people edit, review or keep as configuration | JSON, read with `ProfileDocument.Parse`: see [compile and run](engine/compile-and-run.md) and the [profile reference](../RecDiffy.NET/docs/PROFILES.md) |
| CSV or Excel files | [`RecDiffy.IO`](io/files.md) |
| Rows that arrive in batches | [Push and snapshot](engine/push-and-snapshot.md) |
| FIX logs or drop copies | [`RecDiffy.Fix`](fix/flattener.md): `FixFlattener.Read`, then a profile |
| Excel reports for operations staff | `RecDiffy.Excel`: see [Write Excel reports](../README.md#write-excel-reports) |
| Tables or HTML from a `RecResult` | `RecDiffy.Extensions`: `ToMatchTable`, `ToBreakTable`, `ToControlTotalsTable`, `ToSummaryTable`, `ToDataGridHtml` |

## How a reconciliation runs

**Normalize → Consolidate → Match → Compare → Explain.** The two inputs are always called `left` and `right`.

1. **Normalize.** Each side's rules turn raw cells into typed, canonical fields. A value that can't be read becomes null with an `Invalid` diagnostic, and its row is quarantined.
2. **Consolidate** (optional, per side). `lifecycle` collapses each cancel and correct chain into the one row that survives. `packages` merges the linked legs of one trade into one item.
3. **Match.** Each row goes to the first path whose route accepts it. The path's passes run in order, and the earliest pass that can commit a match takes the rows.
4. **Compare.** On every matched entry, each `Key` and `Economic` field is compared, after rounding to its scale, with the tolerance declared for it.
5. **Explain.** Every entry records the pass that decided it, and every input row gets exactly one disposition.

## Rules that keep a result correct

1. **Declare a culture.** A profile with none reads text with the machine's culture, so a run can differ between machines. `"culture": ""` is the invariant culture.
2. **Keep amounts in `decimal` or text.** A `float` or `double` is never converted; its row is quarantined with `FloatValue`.
3. **Matched doesn't mean balanced.** Read `Outcome` and `Diffs` on every matched entry.
4. **Missing isn't zero.** Null against a value is a `Break`. Check `FieldDiff.Left.IsNull` and `FieldDiff.Right.IsNull` before you read `Delta`, and never total deltas across such rows.
5. **Count entries and rows separately.** One entry can hold many rows. To count rows, group `result.Rows` by `Disposition`.
6. **`Ambiguous` and `SearchIncomplete` aren't unmatched.** They mean "not decided". Report them apart from `LeftOnly` and `RightOnly`.
7. **Show quarantined rows with their diagnostics.** They were never matched or compared.
8. **Only `Key` and `Economic` fields are compared.** A tolerance on any other field is accepted and ignored.
9. **Trading rules and attributions need the trading pack** in the registry: `TradingRulePack.AddTo(RuleRegistry.CreateCore())`. Without it they are `UnknownRule`.
10. **Record `result.Run`** next to any report, so the run can be reproduced. The same rows in any order, with the same profile, reference data, cultures and engine version, give the same result.

## When something fails

| Symptom | First thing to check |
|---|---|
| `ProfileCompileException` | Its `Errors`. Each `Code` is explained in [Compile errors](../RecDiffy.NET/docs/PROFILES.md#compile-errors). |
| `InvalidOperationException` from `ProfileDocument.Parse` | A JSON value of the wrong type, such as `"scale": "2"`. |
| A setting has no effect | The key's spelling and case. Unknown keys are ignored. |
| Every row on one side is unmatched | That side's `normalization`. A side with no rules writes no fields. |
| A row is quarantined | `RowResult.Diagnostics`. Every code is in [Run diagnostics](../RecDiffy.NET/docs/PROFILES.md#run-diagnostics). |
| A number that looks right is `DecimalParse` | The side's culture, in `result.Run.Reading`. |
| Many rows are `Ambiguous` | Whether the key tells the rows apart. Add a key field, or an earlier pass with a stricter key. |
| `dotnet add package` finds no version | Add `--prerelease`. |

## Where the code is

| What | Where |
|---|---|
| Compile and run | [Reconciler.cs](../RecDiffy.NET/src/RecDiffy/Reconciler.cs) |
| Rule names and the classes they build | [RuleRegistry.cs](../RecDiffy.NET/src/RecDiffy/RuleRegistry.cs), [TradingRulePack.cs](../RecDiffy.NET/src/RecDiffy.RulePacks.Trading/TradingRulePack.cs) |
| Builders | [RecDiffy/Profile](../RecDiffy.NET/src/RecDiffy/Profile), [RecBuilder.cs](../RecDiffy.NET/src/RecDiffy/Typed/RecBuilder.cs) |
| Push and snapshot | [RecDiffy/Push](../RecDiffy.NET/src/RecDiffy/Push) |
| Result types | [IReconciliationResult.cs](../RecDiffy.NET/src/RecDiffy/Results/IReconciliationResult.cs), [ResultModel.cs](../RecDiffy.NET/src/RecDiffy/Results/ResultModel.cs) |
| FIX | [RecDiffy.Fix](../RecDiffy.NET/src/RecDiffy.Fix) |
| Runnable samples | [RecDiffy.Samples](../RecDiffy.NET/src/RecDiffy.Samples) |
