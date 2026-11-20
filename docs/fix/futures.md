# Futures allocations: do the broker's give-ups add up?

With futures, the broker often reshapes your allocation. You asked for 200 contracts in one fund. The broker books them as 120 at one price and 80 at another, so each is cleared at a real execution price. Same fund, same allocation ID, two rows. The question is whether the pieces add up.

## The messages

Header and trailer trimmed.

```text
US → GS   35=J  | 70=AL1 | 55=ESZ6 | 78=1 | 79=FUND-A | 80=200 | 467=IA1

GS → US   35=AS | 755=AR1 | 70=AL1 | 55=ESZ6 | 78=2
                | 79=FUND-A | 366=10.25 | 80=120 | 467=IA1
                | 79=FUND-A | 366=10.50 | 80=80  | 467=IA1
```

## The profile, in C#

```csharp
ProfileBuilder.Create("fix-futures-allocations-generic-columns", "1.0.0")
    .Culture(CultureInfo.GetCultureInfo("en-US"))
    .RowKeys("Key", "Key")
    .Key("Counterparty", FieldType.Symbol)
    .Key("IndividualAllocId", FieldType.Symbol)
    .Economic("Quantity", FieldType.Decimal, scale: 0)
    .Left(AllocationIds)
    .Right(AllocationIds)
    .Path("allocations", path => path
        .NaturalKey("P1", "Counterparty", "IndividualAllocId")
        .GroupSum("P2", Side.Right, block: ["Counterparty", "IndividualAllocId"], conserve: ["Quantity"]));   // several broker rows may make one of yours

static SideBuilder AllocationIds(SideBuilder side) =>
    side
        .MapSymbol("Counterparty", "Counterparty")
        .MapSymbol("467", "IndividualAllocId")
        .ParseDecimal("80", "Quantity");
```

The group pass sums every compared number, so this profile compares quantity only. A sum of two prices means nothing.

## Put it together

```csharp
var ours = FixFlattener.Read(instructions, fix, new FixFlattenOptions(
    Grain: 78, Keys: new Dictionary<string, IReadOnlyList<int>> { ["Key"] = [70, 79] }));
var theirs = FixFlattener.Read(allocationReports, fix, new FixFlattenOptions(
    Grain: 78, Keys: new Dictionary<string, IReadOnlyList<int>> { ["Key"] = [755, 79, 366] }));   // the price makes each piece unique
var (view, _) = profile.Push(ours.Rows, theirs.Rows).Snapshot();
```

## What it handles

- **One account, reshaped into several broker rows.** It's matched one to many, as long as the quantities sum exactly.
- **Accounts the broker didn't reshape** match one to one.
- **An account the broker never booked** stays open on your side.

JSON twin: [generic-futures-allocations.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-futures-allocations.profile.json).
