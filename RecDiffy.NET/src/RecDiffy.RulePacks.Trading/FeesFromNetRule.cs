using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class FeesFromNetFactory : IRuleFactory
{
    public string RuleId => "fees.inferFromNet";
    public RulePhase Phase => RulePhase.Derive;
    public INormalizationRule Bind(RuleArguments args) => new FeesFromNetRule(args.Optional("to") ?? "Fees", args.Optional("cashSign") ?? "unsigned");
}

internal sealed class FeesFromNetRule : INormalizationRule
{
    private readonly string _to;
    private readonly string _cashSign;
    public FeesFromNetRule(string to, string cashSign) { _to = to; _cashSign = cashSign; }
    public string RuleId => "fees.inferFromNet";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<FieldId> Reads { get; } = [new("Side"), new("Quantity"), new("Price"), new("Accrued"), new("NetCash"), new("AssetClass")];
    public IReadOnlyList<FieldId> Writes => [new(_to)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryCanonical("AssetClass", out var assetValue) || !row.TryCanonical("Side", out var sideValue) ||
            !row.TryCanonical("Quantity", out var quantity) || !row.TryCanonical("Price", out var price) ||
            !row.TryCanonical("NetCash", out var net))
            return;
        var asset = row.State.Pool.Symbol(assetValue.Interned);
        var principal = asset switch
        {
            "Bond" => Rounding.Round(quantity.Number * price.Number / 100m, 2, row.State.Midpoint),
            "Equity" or "Forward" => Rounding.Round(quantity.Number * price.Number, 2, row.State.Midpoint),
            _ => (decimal?)null,
        };
        if (principal is null)
            return;
        var accrued = row.TryCanonical("Accrued", out var accruedValue) ? accruedValue.Number : 0m;
        var side = row.State.Pool.Symbol(sideValue.Interned);
        if (decimal.Abs(net.Number) * 4m < decimal.Abs(principal.Value))
            return;
        var fees = _cashSign == "signed"
            ? side is "SELL" or "REC_FIXED" ? principal.Value + accrued - net.Number : -(principal.Value + accrued) - net.Number
            : side is "SELL" or "REC_FIXED" ? principal.Value + accrued - net.Number : net.Number - principal.Value - accrued;
        row.SetDecimal(_to, fees);
        row.MarkDerived(_to);
    }
}
