using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

internal static class PartyLookup
{
    public static RowContext Lei(RowContext row, string from, string to, string ruleId)
    {
        var lei = WhenMatcher.Text(row, from);
        return lei switch
        {
            null or "" => row,
            var code when row.Reference.LeiToCode.TryGetValue(code, out var party) => Write(row, to, party, ruleId, code),
            var code => row.Diagnose("UnknownParty", DiagnosticSeverity.Invalid, ruleId, to, code),
        };
    }

    public static RowContext Alias(RowContext row, string from, string to, string ruleId) =>
        row.IsNull(to) ? Lookup(row, WhenMatcher.Text(row, from), to, ruleId) : row;

    private static RowContext Lookup(RowContext row, string? alias, string to, string ruleId) => alias switch
    {
        null or "" => row,
        var name when row.Reference.AliasToCode.TryGetValue(name, out var party) => Write(row, to, party, ruleId, name),
        var name => row.Diagnose("UnknownParty", DiagnosticSeverity.Invalid, ruleId, to, name),
    };

    private static RowContext Write(RowContext row, string field, string party, string ruleId, string raw)
    {
        var (pool, id) = row.Pool.InternIdentifier("PARTY", "", party);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Identifier, id), ruleId, raw, party);
    }
}

internal sealed class PartyLeiRule(string from, string to) : INormalizationRule
{
    public string RuleId => "party.resolveLei";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) => row = PartyLookup.Lei(row, from, to, RuleId);
}

internal sealed class PartyAliasRule(string from, string to) : INormalizationRule
{
    public string RuleId => "party.resolveAlias";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads => [new(to)];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) => row = PartyLookup.Alias(row, from, to, RuleId);
}
