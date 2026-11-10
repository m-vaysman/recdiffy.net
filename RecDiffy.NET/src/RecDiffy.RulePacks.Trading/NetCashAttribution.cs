using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.RulePacks.Trading;

internal sealed class NetCashAttribution : IAttribution
{
    private static readonly string[] NetCashRoots = ["Quantity", "Price", "Accrued", "Fees"];

    public string Name => "trading.netCash";

    public ImmutableArray<string> Roots { get; } = ["Quantity", "Price"];

    public AttributionStage Stage => AttributionStage.Finished;

    // Each step is the one the expression took before #224, in the same order, so a value that fits is unchanged. A step
    // past the decimal range leaves net cash unexplained.
    public bool Explains(RowContext left, RowContext right, FieldDeclaration field, decimal delta, MidpointMode midpoint)
    {
        var asset = left.Display("AssetClass") ?? right.Display("AssetClass");
        var principal = CheckedDecimal.Subtract(Principal(right, asset, midpoint), Principal(left, asset, midpoint));
        var withAccrued = CheckedDecimal.Subtract(CheckedDecimal.Add(principal, Number(right, "Accrued")), Number(left, "Accrued"));
        var fees = CheckedDecimal.Multiply(FeeSign(left, right), CheckedDecimal.Subtract(Number(right, "Fees"), Number(left, "Fees")));
        var expected = CheckedDecimal.Add(withAccrued, fees);
        return expected is { } value && Rounding.Round(value, 2, midpoint) == Rounding.Round(delta, 2, midpoint);
    }

    // A root that is not a step is a Break. When no root has moved, the explained net is still a Break.
    public FieldOutcome Outcome(RootOutcomes roots) =>
        NetCashRoots.Select(roots.Of).ToArray() switch
        {
            var outcomes when outcomes.Contains(FieldOutcome.Break) => FieldOutcome.Break,
            var outcomes when outcomes.Contains(FieldOutcome.WithinTolerance) => FieldOutcome.WithinTolerance,
            _ => FieldOutcome.Break,
        };

    // The moved roots in the fixed root order, so the field declaration order cannot reorder AttributedTo (#218).
    public ImmutableArray<string> AttributedTo(RootOutcomes roots) => [.. NetCashRoots.Where(roots.Moved)];

    // Bonds divide quantity times price by 100. A missing asset class does not. A product past the decimal range is null.
    private static decimal? Principal(RowContext row, string? asset, MidpointMode midpoint) =>
        CheckedDecimal.Multiply(Number(row, "Quantity"), Number(row, "Price")) switch
        {
            { } raw => Rounding.Round(asset is "Bond" ? raw / 100m : raw, 2, midpoint),
            null => null,
        };

    // SELL, REC_FIXED and REC make the fee negative. A missing side on both rows leaves it positive.
    private static int FeeSign(RowContext left, RowContext right) =>
        (left.Display("Side") ?? right.Display("Side")) is "SELL" or "REC_FIXED" or "REC" ? -1 : 1;

    // A null field contributes zero, so a missing Accrued can still explain net cash.
    private static decimal Number(RowContext row, string field) => row.IsNull(field) ? 0m : row.Values[field].Number;
}
