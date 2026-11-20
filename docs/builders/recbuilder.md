# RecBuilder: reconcile your own objects with typed lambdas

Part of the [builders section](llms.txt). `RecBuilder` reconciles two lists of your own objects. Each pair of `Expression<Func<...>>` lambdas selects a member on each side, and the builder writes the profile for you.

```csharp
using RecDiffy;

var trades = RecBuilder.Between(ours, confirms)
    .MatchOn(t => t.Isin, c => c.Isin)                      // pass P1: a natural key
        .And(t => t.Side, c => c.BuySell)                  // adds Side to P1's key
            .Value("BUY",  accepts: ["B", "BOT", "BUY"])   // spellings that mean BUY, on either side
            .Value("SELL", accepts: ["S", "SLD", "SELL"])
    .Compare(t => t.Qty, c => c.Qty)                        // compared exactly
    .Compare(t => t.Price, c => c.Price, tolerance: 0.0001m)
    .Run();

foreach (var match in trades.Breaks)
    foreach (var diff in match.Diffs)
        Console.WriteLine($"{match.Left[0].Ref} vs {match.Right[0].Ref}: {diff.Field} {trades.Result.Format(diff.Left)} vs {trades.Result.Format(diff.Right)}");

record Trade(string Ref, string Isin, string Side, int Qty, decimal Price);
record Confirm(string Ref, string Isin, string BuySell, int Qty, decimal Price);
```

| Method | What it does |
|---|---|
| `RecBuilder.Between(left, right)` | Starts a reconciliation. Both lists are read once, now. |
| `MatchOn(left, right)` | Adds a natural-key pass on one member pair. Passes are named `P1`, `P2` and so on. |
| `.And(left, right)` | Adds a member pair to the latest pass's key. |
| `ThenMatchOn(left, right)` | Adds the next pass, tried on what earlier passes left unmatched. |
| `Compare(left, right, tolerance:)` | Compares a member pair on matched items. A decimal may differ by up to `tolerance`, inclusive. |
| `Sum(left, right)` | Compares a decimal pair and adds two group-sum passes after the natural-key passes: several left rows against one right row, then several right rows against one left row. Each blocks on the latest `MatchOn` or `ThenMatchOn` key and matches only when every summed pair adds up exactly. |
| `Value(value, accepts)`, `Value(value, ours, theirs)`, `Value(spelling => ...)`, `Value(ours, theirs)` | Names a value of the string member just added to a key, and the spellings that mean it. A spelling nobody listed is quarantined with `UnknownSymbol`, never guessed. Call it right after `MatchOn`, `ThenMatchOn` or `And`. |
| `Opposite("BUY", "SELL")`, `Opposite(("BUY", "S"), ("SELL", "B"))`, `Opposite((ours, theirs) => ...)` | Says a left value matches the opposite right value, as for a cross trade. Two items that agree don't match. |
| `Named(profileId, version)` | Names the profile. The default is `typed`, version `1.0.0`. |
| `Profile` | The `ProfileDocument` it builds. `.ToJson()` writes it as JSON, a good start for a JSON profile. |
| `Run(cancellationToken)` | Runs with the core rules and `ReferenceDataSet.Empty`. |

`Run` returns a `RecResult<TLeft, TRight>`:

| Member | What it holds |
|---|---|
| `Matched` | Every matched pair, as `RecMatch(Left, Right, Outcome, Diffs)` of your own objects, in the order of the left items. |
| `Breaks` | The matched pairs whose compared fields break. |
| `Ambiguous` | `RecGroup(Left, Right)`: items that could match more than one item, so none was chosen. |
| `LeftOnly`, `RightOnly` | Your items with no counterpart. |
| `Result` | The engine's full `IReconciliationResult`. See [read the result](../engine/reading-results.md). |
| `Profile` | The profile that ran. |

## What it infers

- **Field types come from member types.** `decimal`, `int` and `long` are `Decimal` fields; `DateOnly` and `DateTime` are `Date` fields, and a `DateTime` loses its time of day; a `string` is an `Identifier` in a key and `Text` otherwise, or a `Symbol` once `Value` names its values. Any other type throws `ArgumentException`. Nullable types are fine.
- **A field is named after the left member,** and a nested member such as `row => row.Header.Ref` is named `Header.Ref`.

## What it can't express

Row keys, a culture, explicit roles or scales, lifecycle chains, packages, routes, a `when` on a tolerance, a group sum in one direction only, trading rules and reference data. For any of these, build the profile with [ProfileBuilder](profilebuilder.md) and pass your objects through `RecordSources.FromObjects` with a `SourceMap<T>`.

## Examples

- The README's ten examples all use `RecBuilder`: a [trade match](../../README.md#1-trade-match-in-60-seconds-our-blotter-vs-broker-confirms), a [break report](../../README.md#2-position-break-report-fund-books-vs-custodian), [allocations](../../README.md#4-allocations-oms-block-vs-accounting-one-block-to-many-funds) with `Sum`, [cross trades](../../README.md#5-cross-trades-fund-as-sell-vs-fund-bs-buy) and a [fallback match](../../README.md#10-fallback-match-when-the-reference-is-missing) with `ThenMatchOn`.
- [Typed reconciliation](../typed-reconciliation/README.md) walks through it step by step.
- [TypedReconciliation.cs](../../RecDiffy.NET/src/RecDiffy.Samples/TypedReconciliation.cs) is a runnable sample.
- `The_typed_builder_matches_booked_executions_to_fills_as_the_json_profile_does` in [FixProfilesBuiltInCodeTests.cs](../../RecDiffy.NET/src/RecDiffy.IO.Tests/FixProfilesBuiltInCodeTests.cs) shows `RecBuilder` giving the same entries as a JSON profile.

The code is [RecBuilder.cs](../../RecDiffy.NET/src/RecDiffy/Typed/RecBuilder.cs).
