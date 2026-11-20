# Reconcile FIX with RecDiffy

Your OMS says one thing. Your broker's drop copy says another. Somewhere in a few thousand FIX messages, an order was replaced, a fill was busted, or an allocation went to the wrong account.

RecDiffy finds those differences in three steps:

1. **Read.** `RecDiffy.Fix` checks every message against the FIX spec. A broken one is set aside with a reason, never guessed at.
2. **Flatten.** Each message becomes a row, and its tags become the columns: `55` is the symbol, `38` the quantity, `467` the allocation ID.
3. **Reconcile.** A profile, written by you, says which columns identify a trade, what each value means, and how close two numbers must be.

Nothing about your setup is built into the code. The profile is yours, and you can write it in a few lines of C#.

## Pick your scenario

| Scenario | The question it answers |
|---|---|
| [Orders](orders.md) | Did the broker see the same order you did, after every replace and cancel? |
| [Fills](fills.md) | Does every fill match the drop copy, corrections and busts included? |
| [Equity allocations](equities.md) | Did each account get the right shares, commission and fees? |
| [Futures allocations](futures.md) | Do the broker's reshaped give-ups add up to what you allocated? |
| [Bond allocations](bonds.md) | Does the dealer's net money and accrued interest match yours? |

## The whole thing, start to finish

```shell
dotnet add package RecDiffy.Fix --prerelease
```

```csharp
using RecDiffy;
using RecDiffy.Fix;

var fix = new FixOptions(OurCompId: "US");                          // you are "US"; the other side is the counterparty
var ours = FixFlattener.Read(File.ReadAllText("oms.log"), fix);      // what you recorded
var theirs = FixFlattener.Read(File.ReadAllText("dropcopy.log"), fix); // what the broker reported

var profile = Reconciler.Compile(MyProfile().Build(RuleRegistry.CreateCore()).Document, RuleRegistry.CreateCore());
var (view, next) = profile.Push(ours.Rows, theirs.Rows).Snapshot();

view.Breaks;                                  // what disagrees
var (later, _) = next.Push(moreOurs, moreTheirs).Snapshot();   // more messages arrive? push and snapshot again
```

Each scenario page shows the `MyProfile()` for that case, written with the lambda builders, plus a few sample messages.

## Every example here is tested

The profiles on these pages come from [FixScenarioProfilesInCodeTests.cs](../../RecDiffy.NET/src/RecDiffy.Fix.Tests/FixScenarioProfilesInCodeTests.cs). Each one is checked to build exactly the same profile as its JSON twin in [test-data/fix-reconciliation](../../RecDiffy.NET/test-data/fix-reconciliation) (`generic-*.profile.json`), and to run from FIX text to a snapshot. [FixScenarioTests.cs](../../RecDiffy.NET/src/RecDiffy.Fix.Tests/FixScenarioTests.cs) covers the edge cases each page lists.

The reader's checks and every flattener option are in [FixReader and FixFlattener](flattener.md).
