using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.RulePacks.Trading;

internal sealed class ProRataToQuantityAttribution : IAttribution
{
    private const string Quantity = "Quantity";

    public string Name => "trading.proRataToQuantity";

    public ImmutableArray<string> Roots { get; } = [Quantity];

    public AttributionStage Stage => AttributionStage.Measured;

    // The left quantity is the base. A zero left quantity is unexplained, and so is an expected value past the decimal
    // range (#224).
    public bool Explains(RowContext left, RowContext right, FieldDeclaration field, decimal delta, MidpointMode midpoint)
    {
        if (left.IsNull(Quantity) || right.IsNull(Quantity) || left.IsNull(field.Name) || right.IsNull(field.Name))
            return false;
        var leftQuantity = left.Values[Quantity].Number;
        if (leftQuantity == 0m)
            return false;
        var expected = CheckedDecimal.Divide(CheckedDecimal.Multiply(left.Values[field.Name].Number, right.Values[Quantity].Number), leftQuantity);
        return expected is { } value && Rounding.Round(value, field.Scale, midpoint) == Rounding.Round(right.Values[field.Name].Number, field.Scale, midpoint);
    }

    public FieldOutcome Outcome(RootOutcomes roots) => roots.Of(Quantity);

    public ImmutableArray<string> AttributedTo(RootOutcomes roots) => [Quantity];
}
