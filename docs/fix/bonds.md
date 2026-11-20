# Bond allocations: does the dealer's money match yours?

With bonds, the dealer calculates the money. It reports each account's net money and accrued interest in an allocation report (`35=AS`), and you check those against your own numbers. A cent of rounding is fine. A difference in accrued interest is not.

## The messages

Header and trailer trimmed.

```text
US → GS   35=J  | 70=BJ1  | 54=1 | 55=US912828XY | 78=2
                | 79=FUND-A | 80=1000000 | 154=1002345.67 | 742=1234.56
                | 79=FUND-B | 80=500000  | 154=501172.84  | 742=617.28

GS → US   35=AS | 755=BR1 | 54=1 | 55=US912828XY | 78=2
                | 79=FUND-A | 80=1000000 | 154=1002345.68 | 742=1234.56
                | 79=FUND-B | 80=500000  | 154=501172.84  | 742=617.30
```

## The profile, in C#

```csharp
ProfileBuilder.Create("fix-bond-allocations-generic-columns", "1.0.0")
    .Culture(CultureInfo.GetCultureInfo("en-US"))
    .RowKeys("Key", "Key")
    .Key("Counterparty", FieldType.Symbol)
    .Key("Account", FieldType.Symbol)
    .Key("Instrument", FieldType.Symbol)
    .Key("Side", FieldType.Symbol)
    .Economic("Quantity", FieldType.Decimal, scale: 0)
    .Economic("NetMoney", FieldType.Decimal, scale: 2)
    .Economic("Accrued", FieldType.Decimal, scale: 2)
    .Left(BondColumns)
    .Right(BondColumns)
    .Path("allocations", path => path
        .NaturalKey("P1", "Counterparty", "Account", "Instrument", "Side")
        .Tolerance("NetMoney", absolute: 0.02m));                       // two cents

static SideBuilder BondColumns(SideBuilder side) =>
    side
        .MapSymbol("Counterparty", "Counterparty")
        .MapSymbol("79", "Account")
        .MapSymbol("55", "Instrument")
        .MapSymbol("54", "Side")
        .ParseDecimal("80", "Quantity")
        .ParseDecimal("154", "NetMoney")
        .ParseDecimal("742", "Accrued");
```

Bond reports often carry no allocation ID you could match on, so this profile matches on account, bond and side.

## Put it together

```csharp
var ours = FixFlattener.Read(instructions, fix, new FixFlattenOptions(
    Grain: 78, Keys: new Dictionary<string, IReadOnlyList<int>> { ["Key"] = [70, 79] }));
var theirs = FixFlattener.Read(dealerReports, fix, new FixFlattenOptions(
    Grain: 78, Keys: new Dictionary<string, IReadOnlyList<int>> { ["Key"] = [755, 79] }));
var (view, _) = profile.Push(ours.Rows, theirs.Rows).Snapshot();
view.Breaks;   // FUND-B: accrued 617.28 against 617.30
```

## What it handles

- **Net money within tolerance.** FUND-A is a cent apart and passes.
- **An accrued interest difference is a break.** FUND-B is two cents off, and `view.Breaks` shows it with the difference.

JSON twin: [generic-bond-allocations.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-bond-allocations.profile.json).
