using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class DirtyPriceFactory : IRuleFactory
{
    public string RuleId => "price.dirtyToClean";
    public RulePhase Phase => RulePhase.Derive;
    public INormalizationRule Bind(RuleArguments args) => new DirtyPriceRule(args.Optional("price") ?? "Price", args.Optional("accrued") ?? "Accrued", args.Optional("quantity") ?? "Quantity");
}

internal sealed class DirtyPriceRule : INormalizationRule
{
    private readonly string _price;
    private readonly string _accrued;
    private readonly string _quantity;
    public DirtyPriceRule(string price, string accrued, string quantity)
    {
        _price = price;
        _accrued = accrued;
        _quantity = quantity;
    }

    public string RuleId => "price.dirtyToClean";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<FieldId> Reads => [new(_price), new(_accrued), new(_quantity)];
    public IReadOnlyList<FieldId> Writes => [new(_price)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryCanonical(_price, out var dirty) || !row.TryCanonical(_quantity, out var quantity) || quantity.Number == 0m)
        {
            row.Diagnose("PriceParse", DiagnosticSeverity.Invalid, _price);
            return;
        }
        var accrued = row.TryCanonical(_accrued, out var accruedValue) ? accruedValue.Number : 0m;
        var clean = dirty.Number - accrued / quantity.Number * 100m;
        row.SetDecimal(_price, clean, dirty.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
