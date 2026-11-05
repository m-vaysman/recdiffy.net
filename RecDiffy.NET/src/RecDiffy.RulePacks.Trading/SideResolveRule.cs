using System.Globalization;
using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class SideResolveFactory : IRuleFactory
{
    public string RuleId => "side.resolve";
    public RulePhase Phase => RulePhase.Derive;
    public INormalizationRule Bind(RuleArguments args) => new SideResolveRule(
        args.Required("code"),
        args.Required("quantity"),
        args.Optional("invertWhen"),
        args.Optional("perspective") ?? "Reporting_Perspective",
        args.Optional("asset") ?? "AssetClass");
}

internal sealed class SideResolveRule : INormalizationRule
{
    private readonly string _code;
    private readonly string _quantity;
    private readonly string? _invertWhen;
    private readonly string _perspective;
    private readonly string _asset;
    public SideResolveRule(string code, string quantity, string? invertWhen, string perspective, string asset)
    {
        _code = code;
        _quantity = quantity;
        _invertWhen = invertWhen;
        _perspective = perspective;
        _asset = asset;
    }

    public string RuleId => "side.resolve";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<FieldId> Reads => [new(_asset)];
    public IReadOnlyList<FieldId> Writes => [new("Side")];

    public void Apply(ref RowContext row)
    {
        var code = row.TryRaw(_code, out var raw) && raw.Text is not null ? raw.Text.Trim() : "";
        var quantity = Signed(row);
        var asset = row.TryCanonical(_asset, out var assetValue) ? row.State.Pool.Symbol(assetValue.Interned) : "";
        if (!TryResolve(code, quantity, asset, out var side, out var codeName))
        {
            row.Diagnose(codeName, DiagnosticSeverity.Invalid, "Side", code);
            return;
        }
        if (_invertWhen is not null && row.TryRaw(_perspective, out var perspective) && perspective.Text == _invertWhen)
            side = Invert(side);
        row.SetSymbol("Side", side, code);
    }

    private decimal Signed(RowContext row)
    {
        if (!row.TryRaw(_quantity, out var raw) || raw.IsNull)
            return 0m;
        if (raw.Number is decimal number)
            return number;
        return decimal.TryParse(raw.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    internal static bool TryResolve(string code, decimal quantity, string asset, out string side, out string diagnostic)
    {
        side = "";
        diagnostic = "UndeterminedSide";
        var token = code.ToUpperInvariant();
        var family = token switch
        {
            "BUY" or "B" or "BOT" => "BUY",
            "SELL" or "S" or "SLD" => "SELL",
            "PAY_FIXED" or "PAY" or "P" => "PAY_FIXED",
            "REC_FIXED" or "REC" or "R" => "REC_FIXED",
            "" => "",
            _ => "?",
        };
        if (family == "?")
            return false;
        var swap = asset == "Swap";
        if (family is "BUY" or "REC_FIXED" && quantity < 0m)
        {
            diagnostic = "SideSignConflict";
            return false;
        }
        if (family == "" && quantity == 0m)
            return false;
        side = family switch
        {
            "BUY" => "BUY",
            "SELL" => "SELL",
            "PAY_FIXED" => "PAY_FIXED",
            "REC_FIXED" => "REC_FIXED",
            _ => quantity < 0m ? (swap ? "PAY_FIXED" : "SELL") : (swap ? "REC_FIXED" : "BUY"),
        };
        return true;
    }

    private static string Invert(string side) => side switch
    {
        "BUY" => "SELL",
        "SELL" => "BUY",
        "PAY_FIXED" => "REC_FIXED",
        "REC_FIXED" => "PAY_FIXED",
        _ => side,
    };
}
