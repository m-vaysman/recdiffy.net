using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class TradeRefClassifyFactory : IRuleFactory
{
    public string RuleId => "tradeRef.classify";
    public RulePhase Phase => RulePhase.Identify;
    public INormalizationRule Bind(RuleArguments args) => new TradeRefClassifyRule(
        args.Required("from"),
        args.Required("to"),
        args.Optional("shared") ?? "lei30",
        args.Optional("prefix") ?? "EX-",
        args.Optional("brokerPrefix") ?? "EXT-",
        args.Optional("party") ?? "Counterparty");
}

internal sealed class TradeRefClassifyRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    private readonly string _mode;
    private readonly string _prefix;
    private readonly string _brokerPrefix;
    private readonly string _party;
    public TradeRefClassifyRule(string from, string to, string mode, string prefix, string brokerPrefix, string party)
    {
        _from = from;
        _to = to;
        _mode = mode;
        _prefix = prefix;
        _brokerPrefix = brokerPrefix;
        _party = party;
    }

    public string RuleId => "tradeRef.classify";
    public RulePhase Phase => RulePhase.Identify;
    public IReadOnlyList<FieldId> Reads => [new(_party)];
    public IReadOnlyList<FieldId> Writes => [new(_to)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryRaw(_from, out var raw) || raw.IsNull || string.IsNullOrWhiteSpace(raw.Text))
        {
            row.SetNull(_to);
            return;
        }
        var text = raw.Text.Trim();
        var party = "";
        if (row.TryCanonical(_party, out var partyValue))
            party = row.State.Pool.Id(partyValue.Interned).Value;
        var shared = _mode switch
        {
            "lei30" => text.Length == 30 && row.Reference.LeiToCode.ContainsKey(text[..20]),
            "prefix" => text.StartsWith(_prefix, StringComparison.Ordinal) && !text.StartsWith(_brokerPrefix, StringComparison.Ordinal),
            "nonBlank" => true,
            _ => false,
        };
        if (shared)
            row.SetIdentifier(_to, "shared", "", text, text);
        else
            row.SetIdentifier(_to, "broker", party, text, text);
    }
}
