using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

// Spec v3.1 §3.3: BUY and PAY_FIXED give fees = net - principal - accrued, SELL and REC_FIXED give principal + accrued - net.
// Principal is the quantity's magnitude times price (divided by 100 for a bond) rounded to cents; swaps and unknown asset
// classes have none, so their fee stays null. Net cash is unsigned unless the profile says cash is signed (§9.2, fixture 2.0:
// a BUY pays out, so its cash is negative and the BUY formula is read with the sign reversed). Accrued does not apply to an
// equity or a forward (§7.5), so a blank one counts as zero there; for any other asset class a blank accrued leaves the fee
// null, as does any other missing input. A missing input does not quarantine the row.
internal sealed class InferFeesFromNetRule(string to, int scale, MidpointMode midpoint, bool signedCash) : INormalizationRule
{
    public string RuleId => "fees.inferFromNet";

    public RulePhase Phase => RulePhase.Derive;

    public IReadOnlyList<FieldId> Reads { get; } =
    [
        new(TradeFields.Side),
        new(TradeFields.AssetClass),
        new(TradeFields.Quantity),
        new(TradeFields.Price),
        new(TradeFields.Accrued),
        new(TradeFields.NetCash),
    ];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) =>
        row = Fees(row) switch
        {
            { } fees => Write(row, Rounding.Round(fees, scale, midpoint)),
            null => row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, row.Display(TradeFields.NetCash), null),
        };

    private decimal? Fees(RowContext row) =>
        (row.Display(TradeFields.Side), Principal(row), Accrued(row), DecimalCells.Of(row, TradeFields.NetCash)) switch
        {
            ("BUY" or "PAY_FIXED", { } principal, { } accrued, { } net) =>
                CheckedDecimal.Subtract(CheckedDecimal.Subtract(signedCash ? 0m - net : net, principal), accrued),
            ("SELL" or "REC_FIXED", { } principal, { } accrued, { } net) =>
                CheckedDecimal.Subtract(CheckedDecimal.Add(principal, accrued), net),
            _ => null,
        };

    // Dividing the price by 100 is exact, and keeps a large quantity from overflowing before it is scaled down.
    private decimal? Principal(RowContext row) =>
        (DecimalCells.Of(row, TradeFields.Quantity) is { } quantity ? decimal.Abs(quantity) : (decimal?)null, DecimalCells.Of(row, TradeFields.Price), row.Display(TradeFields.AssetClass)) switch
        {
            ({ } face, { } price, "Bond") => CheckedDecimal.Multiply(face, price / 100m) is { } gross ? Rounding.Round(gross, 2, midpoint) : null,
            ({ } face, { } price, "Equity" or "Forward") => CheckedDecimal.Multiply(face, price) is { } gross ? Rounding.Round(gross, 2, midpoint) : null,
            _ => null,
        };

    private static decimal? Accrued(RowContext row) =>
        DecimalCells.Of(row, TradeFields.Accrued) ?? (row.Display(TradeFields.AssetClass) is "Equity" or "Forward" ? 0m : null);

    private RowContext Write(RowContext row, decimal fees) =>
        row.Write(to, CanonicalCell.FromDecimal(fees), RuleId, row.Display(TradeFields.NetCash), fees.ToString(CultureInfo.InvariantCulture));
}
