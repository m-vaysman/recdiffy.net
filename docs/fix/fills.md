# Fills: does every fill match the drop copy?

Fills are where money moves. A fill can be corrected later at a better price, or busted outright, and the drop copy has to agree with your OMS on every one of them.

## The messages

Header and trailer trimmed.

```text
GS → US   35=8 | 11=C1 | 17=F1  |        150=F | 32=100 | 31=50.00 | 60=20261119-15:00:02.000     a fill
GS → US   35=8 | 11=C1 | 17=F2  |        150=F | 32=100 | 31=50.00 | 60=20261119-15:00:02.000     another fill
GS → US   35=8 | 11=C1 | 17=F2c | 19=F2 | 150=G | 32=100 | 31=50.10 | 60=20261119-15:00:02.000     F2 corrected to 50.10
GS → US   35=8 | 11=C1 | 17=F3x | 19=F3 | 150=H | 32=100 | 31=50.00 | 60=20261119-15:00:02.000     F3 busted
```

Timestamps come with an extra column, `60:ns`: the same time as exact nanoseconds, so you can compare times with a tolerance.

## The profile, in C#

```csharp
ProfileBuilder.Create("fix-fills-generic-columns", "1.0.0")
    .Culture(CultureInfo.GetCultureInfo("en-US"))
    .RowKeys("17", "17")
    .Key("Counterparty", FieldType.Symbol)
    .Identity("ExecId", FieldType.Symbol)
    .Economic("Quantity", FieldType.Decimal, scale: 0)
    .Economic("Price", FieldType.Decimal, scale: 6)
    .Economic("TransactNanos", FieldType.Decimal, scale: 0)
    .Lifecycle("Action", FieldType.Symbol)
    .Lifecycle("Target", FieldType.Text)
    .Left(FillColumns)
    .Right(FillColumns)
    .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
    .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
    .Path("fills", path => path
        .NaturalKey("P1", "Counterparty", "ExecId")
        .Tolerance("TransactNanos", absolute: 500_000_000m));          // half a second

static SideBuilder FillColumns(SideBuilder side) =>
    side
        .MapSymbol("Counterparty", "Counterparty")
        .MapSymbol("17", "ExecId")
        .MapSymbol("150", "Action", new Dictionary<string, string> { ["F"] = "NEW", ["1"] = "NEW", ["2"] = "NEW", ["G"] = "CORR", ["H"] = "CANC" })
        .CopyText("19", "Target")                                       // a correction or bust points at the fill it changes
        .ParseDecimal("32", "Quantity")
        .ParseDecimal("31", "Price")
        .ParseDecimal("60:ns", "TransactNanos");
```

## Put it together

```csharp
var fills = new FixFlattenOptions(Filter: new FixFilter(Include: new Dictionary<int, IReadOnlyList<string>> { [35] = ["8"] }));
var (view, _) = profile
    .Push(FixFlattener.Read(omsLog, fix, fills).Rows, FixFlattener.Read(dropCopy, fix, fills).Rows)
    .Snapshot();
```

## What it handles

- **Corrections.** `F2c` replaces `F2`, and the match is made on the corrected price.
- **Corrections of corrections** follow the chain to the last one.
- **Busts** remove the fill on both sides.
- **A fill only one side has** stays open as a break.
- **Old-style fills** (`150=1` or `2`, still sent by some brokers) count as fills too.
- **Times** match within half a second, exact to the nanosecond.

JSON twin: [generic-fills.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-fills.profile.json).
