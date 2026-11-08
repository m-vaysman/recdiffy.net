namespace RecDiffy.IO.Tests;

public class WorkbookNumberTests
{
    // A workbook stores the double's shortest exact text. That text, noise included, is the decimal the engine gets.
    [Theory]
    [InlineData(2940000d, "2940000")]
    [InlineData(1.094d, "1.094")]
    [InlineData(-0.5d, "-0.5")]
    [InlineData(0d, "0")]
    [InlineData(0.032600000000000004d, "0.032600000000000004")]
    [InlineData(0.30000000000000004d, "0.30000000000000004")]
    [InlineData(1e-5d, "0.00001")]
    public void A_stored_number_becomes_the_exact_decimal_it_was_written_as(double stored, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), WorkbookNumbers.Exact(stored));

    // These stay a double, so decimal.parse reports FloatValue instead of the engine choosing a value.
    [Theory]
    [InlineData(1e30d)]
    [InlineData(1e-30d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_number_no_decimal_holds_exactly_stays_a_double(double stored) =>
        Assert.Null(WorkbookNumbers.Exact(stored));
}
