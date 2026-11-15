using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

// Spec v3.1 §9.2 (fixture 2.0): a dealer-view row reports cash in the dealer's perspective, so its net cash is negated to the
// internal view. Any other perspective, and a null amount, is left as read. Accrued is not touched.
internal sealed class CashToPerspectiveRule(string to, string perspective, string invertWhen) : INormalizationRule
{
    public string RuleId => "cash.toPerspective";

    public RulePhase Phase => RulePhase.Derive;

    public IReadOnlyList<FieldId> Reads { get; } = [new(to)];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) =>
        row = row.IsNull(to) || WhenMatcher.Text(row, perspective) != invertWhen ? row : Negate(row, row.Values[to].Number);

    // Zero minus the amount, so a zero amount stays a positive zero.
    private RowContext Negate(RowContext row, decimal amount)
    {
        var negated = 0m - amount;
        return row.Write(to, CanonicalCell.FromDecimal(negated), RuleId, row.Display(to), negated.ToString(CultureInfo.InvariantCulture));
    }
}
