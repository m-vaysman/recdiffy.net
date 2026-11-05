using System.Globalization;

namespace RecDiffy;

internal static class Rounding
{
    // Scale outside 0..28 throws from decimal.Round. Compile stores a field scale without that check.
    public static decimal Round(decimal value, int scale, MidpointMode mode) =>
        decimal.Round(value, scale, mode switch
        {
            MidpointMode.ToEven => MidpointRounding.ToEven,
            MidpointMode.AwayFromZero => MidpointRounding.AwayFromZero,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown midpoint mode."),
        });

    // A cast rounds the value. 0.1 + 0.2 becomes 0.3 by cast, and 0.30000000000000004 through the round-trip string.
    public static decimal FromDouble(double value) =>
        decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
}
