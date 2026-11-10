using System.Globalization;

namespace RecDiffy.IO;

// A workbook stores a number as text, and the reader hands it over as a double. Programs that write workbooks store
// the double's shortest exact text, so that text is the stored value, noise included (0.032600000000000004), and it
// becomes that exact decimal. A double no decimal holds exactly (too large, or digits past 28 places) stays a double,
// and decimal.parse reports it (FloatValue).
internal static class WorkbookNumbers
{
    public static decimal? Exact(double number)
    {
        if (double.IsFinite(number) is false)
            return null;
        var text = number.ToString("R", CultureInfo.InvariantCulture);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
            && SameValue(double.Parse(exact.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), number)
            ? exact
            : null;
    }

    // Finite doubles match when their bits match, and +0 matches -0. A tolerance would accept a value no decimal holds.
    private static bool SameValue(double left, double right)
    {
        var leftBits = BitConverter.DoubleToInt64Bits(left);
        var rightBits = BitConverter.DoubleToInt64Bits(right);
        return leftBits == rightBits || ((leftBits | rightBits) << 1) == 0L;
    }
}
