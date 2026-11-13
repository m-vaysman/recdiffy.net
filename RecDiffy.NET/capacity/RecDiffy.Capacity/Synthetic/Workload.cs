using System.Globalization;

namespace RecDiffy.Capacity;

internal sealed record RawTrade(
    string TradeId,
    string Counterparty,
    string Instrument,
    string TradeDate,
    string Side,
    string Currency,
    string Quantity,
    string Price,
    string NetCash);

internal enum UnitKind
{
    Exact,
    Break,
    LeftOnly,
    RightOnly,
}

internal sealed record ExpectedOutcome(int Matched, int Exact, int Break, int LeftOnly, int RightOnly);

internal sealed class Workload
{
    private const string Buy = "BUY";
    private const string Sell = "SELL";

    private static readonly DateOnly Epoch = new(2025, 6, 2);

    private readonly UnitKind[] _kinds;
    private readonly ulong _seed;

    public Workload(int rowsPerSide, double breakRate, double unmatchedRate, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rowsPerSide, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(breakRate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(breakRate, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegative(unmatchedRate);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(unmatchedRate, 1.0);

        _seed = (ulong)(uint)seed;
        var singles = 2 * (int)Math.Round(rowsPerSide * unmatchedRate / 2.0, MidpointRounding.ToEven);
        var matched = rowsPerSide - (singles / 2);
        var breaks = (int)Math.Round(matched * breakRate, MidpointRounding.ToEven);
        var exact = matched - breaks;

        _kinds =
        [
            .. Enumerable.Repeat(UnitKind.Exact, exact),
            .. Enumerable.Repeat(UnitKind.Break, breaks),
            .. Enumerable.Repeat(UnitKind.LeftOnly, singles / 2),
            .. Enumerable.Repeat(UnitKind.RightOnly, singles / 2),
        ];

        var random = new SplitMix64((_seed << 32) ^ 0x5EED_5EED_5EED_5EEDUL);
        for (var index = _kinds.Length - 1; index > 0; index--)
        {
            var other = (int)(random.Next() % (ulong)(index + 1));
            (_kinds[index], _kinds[other]) = (_kinds[other], _kinds[index]);
        }

        RowsPerSide = rowsPerSide;
        LeftRows = matched + (singles / 2);
        RightRows = matched + (singles / 2);
        Expected = new ExpectedOutcome(matched, exact, breaks, singles / 2, singles / 2);
    }

    public int RowsPerSide { get; }

    public int LeftRows { get; }

    public int RightRows { get; }

    public ExpectedOutcome Expected { get; }

    public IEnumerable<RawTrade> Left() => Rows(isLeft: true);

    public IEnumerable<RawTrade> Right() => Rows(isLeft: false);

    private IEnumerable<RawTrade> Rows(bool isLeft)
    {
        for (var index = 0; index < _kinds.Length; index++)
        {
            var kind = _kinds[index];
            if (kind switch
                {
                    UnitKind.LeftOnly => !isLeft,
                    UnitKind.RightOnly => isLeft,
                    _ => false,
                })
            {
                continue;
            }

            yield return Row(index, kind, isLeft);
        }
    }

    private RawTrade Row(int index, UnitKind kind, bool isLeft)
    {
        var values = Values(index);
        var breaking = kind is UnitKind.Break && !isLeft;
        var quantity = breaking ? values.Quantity + 0.01m : values.Quantity;
        var netCash = breaking
            ? Math.Round(quantity * values.Price, 2, MidpointRounding.ToEven) + values.Fee
            : values.NetCash;
        return new RawTrade(
            (isLeft ? "L-" : "R-") + index.ToInvariant(),
            values.Counterparty,
            values.Instrument,
            values.TradeDate,
            values.Side,
            values.Currency,
            quantity.ToInvariant("0.00"),
            values.Price.ToInvariant("0.000000"),
            netCash.ToInvariant("0.00"));
    }

    private (string Counterparty, string Instrument, string TradeDate, string Side, string Currency, decimal Quantity, decimal Price, decimal Fee, decimal NetCash) Values(int index)
    {
        var seed = SplitMix64.Mix((_seed << 32) ^ (ulong)index);
        var counterparty = "CP" + ((seed % 4) + 1).ToInvariant();
        var instrument = "I" + index.ToInvariant("D8");
        var tradeDate = Epoch.AddDays((int)(SplitMix64.Mix(seed + 1) % 13)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var side = (SplitMix64.Mix(seed + 2) & 1) == 0 ? Buy : Sell;
        var currency = (SplitMix64.Mix(seed + 6) & 1) == 0 ? "USD" : "EUR";
        var quantity = (1_000m + (SplitMix64.Mix(seed + 3) % 9_999_000)) / 100m;
        var price = (5_000_000m + (SplitMix64.Mix(seed + 4) % 10_000_000)) / 1_000_000m;
        var fee = (SplitMix64.Mix(seed + 5) % 5_000) / 100m;
        var netCash = Math.Round(quantity * price, 2, MidpointRounding.ToEven) + fee;
        return (counterparty, instrument, tradeDate, side, currency, quantity, price, fee, netCash);
    }
}
