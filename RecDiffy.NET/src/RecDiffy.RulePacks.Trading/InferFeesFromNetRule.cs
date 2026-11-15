using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

// Spec v3.1 §3.3: BUY and PAY_FIXED give fees = net - principal - accrued, SELL and REC_FIXED give principal + accrued - net.
// Principal is quantity times price (divided by 100 for a bond) rounded to cents; swaps and unknown asset classes have none,
// so their fee stays null. The magnitude of net cash is used, so unsigned cash (§9.2 Sonnet) and signed cash (fixture 2.0) give one fee.
// A missing accrued counts as zero, as it does in trading.netCash. Null inputs leave the fee null and do not quarantine the row.
internal sealed class InferFeesFromNetRule(string to, int scale, MidpointMode midpoint) : INormalizationRule
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
        (row.Display(TradeFields.Side), Principal(row), Number(row, TradeFields.NetCash)) switch
        {
            ("BUY" or "PAY_FIXED", { } principal, { } net) =>
                CheckedDecimal.Subtract(decimal.Abs(net), CheckedDecimal.Add(principal, Number(row, TradeFields.Accrued) ?? 0m)),
            ("SELL" or "REC_FIXED", { } principal, { } net) =>
                CheckedDecimal.Subtract(CheckedDecimal.Add(principal, Number(row, TradeFields.Accrued) ?? 0m), decimal.Abs(net)),
            _ => null,
        };

    private decimal? Principal(RowContext row) =>
        (CheckedDecimal.Multiply(Number(row, TradeFields.Quantity), Number(row, TradeFields.Price)), row.Display(TradeFields.AssetClass)) switch
        {
            ({ } gross, "Bond") => Rounding.Round(gross / 100m, 2, midpoint),
            ({ } gross, "Equity" or "Forward") => Rounding.Round(gross, 2, midpoint),
            _ => null,
        };

    private RowContext Write(RowContext row, decimal fees) =>
        row.Write(to, CanonicalCell.FromDecimal(fees), RuleId, row.Display(TradeFields.NetCash), fees.ToString(CultureInfo.InvariantCulture));

    private static decimal? Number(RowContext row, string field) => row.IsNull(field) ? null : row.Values[field].Number;
}
