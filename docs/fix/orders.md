# Orders: did the broker see the same order you did?

An order changes as the day goes on. You send it, the broker acknowledges it, you replace it twice, and maybe the broker cancels it on its own. By the close, you want to know one thing: for every order, do you and the broker agree on where it ended up?

## The messages

Header and trailer trimmed. You are `US`; the broker is `GS`.

```text
GS → US   35=8 | 11=M1 |       17=E1 | 150=0 | 39=0 | 1=ACC1 | 55=IBM | 54=1 | 38=100 | 44=150.25     new order acknowledged
GS → US   35=8 | 11=M2 | 41=M1 | 17=E2 | 150=5 | 39=0 | 1=ACC1 | 55=IBM | 54=1 | 38=150 | 44=150.25     replaced: now 150
GS → US   35=8 | 11=M2 |       17=E3 | 150=4 | 39=4 | 1=ACC1 | 55=IBM | 54=1 | 38=150 | 44=150.25     cancelled by the broker
```

The flattener adds two columns of its own to each execution report: `RootClOrdID`, the order's first ID (`M1`), and `PrevExecID`, the report before this one on the same order. Your profile uses them to chain the order's history.

## The profile, in C#

```csharp
ProfileBuilder.Create("fix-orders-generic-columns", "1.0.0")
    .Culture(CultureInfo.GetCultureInfo("en-US"))
    .RowKeys("17", "17")                                  // each execution report is a row, keyed by ExecID
    .Key("Counterparty", FieldType.Symbol)
    .Key("Account", FieldType.Symbol)
    .Key("Instrument", FieldType.Symbol)
    .Key("Side", FieldType.Symbol)
    .Key("Root", FieldType.Symbol)
    .Economic("Quantity", FieldType.Decimal, scale: 0)
    .Economic("Price", FieldType.Decimal, scale: 6)
    .Economic("OrdStatus", FieldType.Symbol)
    .Lifecycle("Action", FieldType.Symbol)
    .Lifecycle("Target", FieldType.Text)
    .Left(OrderColumns)
    .Right(OrderColumns)
    .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
    .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
    .Path("orders", path => path.NaturalKey("P1", "Counterparty", "Account", "Instrument", "Side", "Root"));

static SideBuilder OrderColumns(SideBuilder side) =>
    side
        .MapSymbol("Counterparty", "Counterparty")
        .MapSymbol("1", "Account")
        .MapSymbol("55", "Instrument")
        .MapSymbol("54", "Side", new Dictionary<string, string> { ["1"] = "BUY", ["2"] = "SELL" })
        .MapSymbol("RootClOrdID", "Root")
        .MapSymbol("39", "OrdStatus")
        .MapSymbol("150", "Action", new Dictionary<string, string> { ["0"] = "NEW", ["5"] = "CORR", ["D"] = "CORR", ["4"] = "CANC", ["C"] = "CANC" })
        .CopyText("PrevExecID", "Target")
        .ParseDecimal("38", "Quantity")
        .ParseDecimal("44", "Price");
```

The one line that matters most is `150` → `Action`. It tells RecDiffy that a replace (`5`) or restatement (`D`) supersedes the previous state, and a cancel (`4`) or expiry (`C`) ends it. Your firm decides that mapping, not the library.

## Put it together

```csharp
var fix = new FixOptions(OurCompId: "US");
var orderStates = new FixFlattenOptions(Filter: new FixFilter(
    Include: new Dictionary<int, IReadOnlyList<string>> { [35] = ["8"] },
    Exclude: new Dictionary<int, IReadOnlyList<string>> { [150] = ["A", "6", "E", "F", "I"] }));   // pending states, fills, status replies

var ours = FixFlattener.Read(omsLog, fix, orderStates);
var theirs = FixFlattener.Read(dropCopy, fix, orderStates);
var (view, _) = profile.Push(ours.Rows, theirs.Rows).Snapshot();
```

## What it handles

- **Replaces, one or many.** `M1` → `M2` → `M3` matches on the last version.
- **Cancels the broker sends on its own**, which keep the same ClOrdID.
- **Restatements and expiries.**
- **Orders not yet acknowledged** show up on your side only.
- **Pending states** (`150=A`, `6`, `E`) never become rows, because the filter leaves them out.
- **The same ID at two brokers** never matches across them, because `Counterparty` is in the key.

JSON twin: [generic-orders.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-orders.profile.json).
