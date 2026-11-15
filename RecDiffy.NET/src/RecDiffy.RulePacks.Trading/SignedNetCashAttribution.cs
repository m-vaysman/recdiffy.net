using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.RulePacks.Trading;

// trading.netCash for cash whose sign says which way the money moves (#433; spec §9.2: fixture 2.0, where a BUY is negative).
//
//   unsigned, trading.netCash:      pays: net = principal + accrued + fees       receives: net = principal + accrued - fees
//   signed, trading.netCashSigned:  pays: net = -(principal + accrued) - fees    receives: net = principal + accrued - fees
//
// The receiving side is the same in both. The paying side is the exact negative, so a paying row's net-cash difference is
// explained when its negative is explained by the unsigned identity. Rounding a negated amount gives the negated rounding
// under both midpoint modes, so the comparison at 2 decimal places is the same one.
// The roots, the outcome and AttributedTo are those of trading.netCash.
internal sealed class SignedNetCashAttribution : IAttribution
{
    private static readonly NetCashAttribution Unsigned = new();

    public string Name => "trading.netCashSigned";

    public ImmutableArray<string> Roots => Unsigned.Roots;

    public AttributionStage Stage => Unsigned.Stage;

    public bool Explains(RowContext left, RowContext right, FieldDeclaration field, decimal delta, MidpointMode midpoint) =>
        Unsigned.Explains(left, right, field, Receives(left, right) ? delta : decimal.Negate(delta), midpoint);

    public FieldOutcome Outcome(RootOutcomes roots) => Unsigned.Outcome(roots);

    public ImmutableArray<string> AttributedTo(RootOutcomes roots) => Unsigned.AttributedTo(roots);

    // SELL, REC_FIXED and REC receive cash. Every other side pays, and so does a pair of rows with no side: the same reading
    // trading.netCash gives the fee.
    private static bool Receives(RowContext left, RowContext right) =>
        (left.Display("Side") ?? right.Display("Side")) is "SELL" or "REC_FIXED" or "REC";
}
