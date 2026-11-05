using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class PriceFrom32ndsFactory : IRuleFactory
{
    public string RuleId => "price.from32nds";
    public RulePhase Phase => RulePhase.Parse;
    public INormalizationRule Bind(RuleArguments args) => new PriceFrom32ndsRule(args.Required("from"), args.Required("to"));
}

internal sealed class PriceFrom32ndsRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    public PriceFrom32ndsRule(string from, string to) { _from = from; _to = to; }
    public string RuleId => "price.from32nds";
    public RulePhase Phase => RulePhase.Parse;
    public IReadOnlyList<FieldId> Reads { get; } = [];
    public IReadOnlyList<FieldId> Writes => [new(_to)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryRaw(_from, out var raw) || raw.IsNull || string.IsNullOrWhiteSpace(raw.Text))
        {
            row.Diagnose("PriceParse", DiagnosticSeverity.Invalid, _to, raw.Text);
            return;
        }
        if (!TryParse(raw.Text.Trim(), out var price))
        {
            row.Diagnose("PriceParse", DiagnosticSeverity.Invalid, _to, raw.Text);
            return;
        }
        row.SetDecimal(_to, price, raw.Text);
    }

    internal static bool TryParse(string text, out decimal price)
    {
        price = 0m;
        var plus = text.EndsWith('+');
        var body = plus ? text[..^1] : text;
        var dash = body.IndexOf('-');
        if (dash <= 0 || dash != body.LastIndexOf('-'))
            return false;
        if (!int.TryParse(body[..dash], out var whole))
            return false;
        var fraction = body[(dash + 1)..];
        if (fraction.Length != 2 || !int.TryParse(fraction, out var ticks) || ticks is < 0 or > 31)
            return false;
        price = whole + ticks / 32m + (plus ? 1m / 64m : 0m);
        return true;
    }
}
