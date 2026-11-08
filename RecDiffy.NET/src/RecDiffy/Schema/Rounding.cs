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

    // The round-trip format is parsed back. A non-finite double fails and leaves number at zero.
    public static bool TryFromDouble(double value, out decimal number)
    {
        number = 0m;
        return double.IsFinite(value)
            && decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
