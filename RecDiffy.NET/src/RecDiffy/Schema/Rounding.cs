using System.Globalization;

namespace RecDiffy;

internal static class Rounding
{
    public static decimal Round(decimal value, int scale, MidpointMode mode) =>
        decimal.Round(value, scale, mode switch
        {
            MidpointMode.ToEven => MidpointRounding.ToEven,
            MidpointMode.AwayFromZero => MidpointRounding.AwayFromZero,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown midpoint mode."),
        });

    public static decimal FromDouble(double value) =>
        decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
}
