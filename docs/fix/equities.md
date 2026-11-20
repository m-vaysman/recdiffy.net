# Equity allocations: did each account get the right shares?

You bought 300 IBM for two funds. You tell the broker how to split it with an allocation instruction (`35=J`). The broker answers with one confirmation per account (`35=AK`), carrying the shares, the price, the commission, the fees and the net money it will settle. Every account has to agree.

## The messages

Header and trailer trimmed.

```text
US → GS   35=J  | 70=AL1 | 71=0 | 626=2 | 55=IBM | 6=150.25 | 78=2
                | 79=FUND-A | 80=200 | 467=IA1 | 154=30050.00 | 12=10.00 | 136=1 | 137=2.50
                | 79=FUND-B | 80=100 | 467=IA2 | 154=15025.00 | 12=5.00  | 136=1 | 137=1.25

GS → US   35=AK | 664=CF1 | 666=0 | 467=IA1 | 79=FUND-A | 80=200 | 6=150.25 | 118=30050.00 | 2640=10.00 | 137=2.50
GS → US   35=AK | 664=CF2 | 666=0 | 467=IA2 | 79=FUND-B | 80=100 | 6=150.25 | 118=15025.03 | 2640=5.00  | 137=1.25
```

Your instruction is flattened one row per account (`Grain: 78`). Each row carries the instruction's fields and that account's own. A row key such as `AL1|FUND-A` is put together from the tags you name.

## The profile, in C#

```csharp
ProfileBuilder.Create("fix-equity-allocations-generic-columns", "1.0.0")
    .Culture(CultureInfo.GetCultureInfo("en-US"))
    .RowKeys("Key", "664")
    .Key("Counterparty", FieldType.Symbol)
    .Identity("IndividualAllocId", FieldType.Symbol)
    .Key("Account", FieldType.Symbol)
    .Key("Instrument", FieldType.Symbol)
    .Economic("Quantity", FieldType.Decimal, scale: 0)
    .Economic("AvgPx", FieldType.Decimal, scale: 6)
    .Economic("NetMoney", FieldType.Decimal, scale: 2)
    .Economic("Commission", FieldType.Decimal, scale: 2)
    .Economic("Fees", FieldType.Decimal, scale: 2)
    .Identity("AllocType", FieldType.Symbol)
    .Lifecycle("Action", FieldType.Symbol)
    .Lifecycle("Target", FieldType.Text)
    .Left(ours => ours
        .MapSymbol("Counterparty", "Counterparty")
        .MapSymbol("467", "IndividualAllocId")
        .MapSymbol("79", "Account")
        .MapSymbol("55", "Instrument")
        .MapSymbol("626", "AllocType")
        .MapSymbol("71", "Action", new Dictionary<string, string> { ["0"] = "NEW", ["1"] = "CORR", ["2"] = "CANC" })
        .CopyText("TargetKey", "Target")
        .ParseDecimal("80", "Quantity")
        .ParseDecimal("6", "AvgPx")
        .ParseDecimal("154", "NetMoney")
        .ParseDecimal("12", "Commission")
        .ParseDecimal("137", "Fees"))
    .Right(theirs => theirs
        .MapSymbol("Counterparty", "Counterparty")
        .MapSymbol("467", "IndividualAllocId")
        .MapSymbol("79", "Account")
        .MapSymbol("55", "Instrument")
        .MapSymbol("35", "AllocType", new Dictionary<string, string> { ["AK"] = "2" }, passthrough: false)
        .MapSymbol("666", "Action", new Dictionary<string, string> { ["0"] = "NEW", ["1"] = "CORR", ["2"] = "CANC" })
        .CopyText("772", "Target")
        .ParseDecimal("80", "Quantity")
        .ParseDecimal("6", "AvgPx")
        .ParseDecimal("118", "NetMoney")
        .ParseDecimal("2640", "Commission")
        .ParseDecimal("137", "Fees"))
    .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
    .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
    .Path("allocations", path => path
        .NaturalKey("P1", "Counterparty", "IndividualAllocId")                        // match on the allocation ID first
        .ReducedKey("P2", "Counterparty", "Account", "Instrument", "Quantity")        // fall back to the economics
        .Tolerance("NetMoney", absolute: 0.05m, when: Condition.Equal("AllocType", "2")));
```

The two sides read different tags for the same thing, such as net money (`154` on yours, `118` on theirs). That's what `.Left` and `.Right` are for.

**Who calculates the money?** When the sell side calculates (AllocType `2`), its net money is official, and a few cents' difference is fine. When you calculate (`1`), it must match exactly. A tolerance's condition has to hold on both sides, but confirmations don't carry `626`, so this profile declares the broker's side as `2`, from `35=AK`. That's a fact about your relationship with that broker, and it's yours to set.

## Put it together

```csharp
var ours = FixFlattener.Read(instructions, fix, new FixFlattenOptions(
    Grain: 78,                                                                         // one row per account
    Keys: new Dictionary<string, IReadOnlyList<int>> { ["Key"] = [70, 79], ["TargetKey"] = [72, 79] },
    Reference: (Id: 70, Ref: 72)));                                                    // a cancel stands for every account it cancels
var theirs = FixFlattener.Read(confirmations, fix);
var (view, _) = profile.Push(ours.Rows, theirs.Rows).Snapshot();
```

## What it handles

- **Matching on IndividualAllocID (`467`)**, the industry's exact match, with a fallback to account, symbol and quantity when a confirmation has none.
- **Cancel and send again.** Your cancel carries no accounts, yet it cancels every account of the instruction it names. The broker's cancelled confirmation is replaced by its new one.
- **Commission, fees and net money** compared per account.
- **Net money within tolerance** only when the sell side calculates.

JSON twin: [generic-equity-allocations.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-equity-allocations.profile.json).
