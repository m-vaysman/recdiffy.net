using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

// An unpaired UTF-16 surrogate is not an XML 1.0 character, like a control character. The writer should replace it
// with U+FFFD and keep the rest of the text. Today Clean lets every surrogate through, and saving throws (#256).
// The surrogate is passed as a number: test data that holds a lone surrogate does not survive serialization.
public class UnpairedSurrogateReportTests
{
    [Theory(Skip = "#256: OpenXmlWorkbookWriter.Clean lets an unpaired surrogate through, and saving the workbook throws ArgumentException.")]
    [InlineData("A", 0xD800, "B")]
    [InlineData("A", 0xDC00, "B")]
    [InlineData("AB", 0xD800, "")]
    public void An_unpaired_surrogate_in_cell_text_is_replaced(string before, int surrogate, string after)
    {
        var path = ArgonExcelHoldsTests.Write(before + (char)surrogate + after);

        var texts = ArgonExcelHoldsTests.Texts(path).ToArray();
        var errors = Workbooks.SchemaErrors(path);

        // A lone surrogate in a failure message aborts the test run when the Allure writer encodes it, so the
        // messages carry counts, not cell text.
        Assert.True(texts.Contains(before + '\uFFFD' + after, StringComparer.Ordinal), $"No cell holds the cleaned text. Cells that still hold a surrogate: {texts.Count(text => text.Any(char.IsSurrogate))}.");
        Assert.True(errors.Count == 0, $"The workbook has {errors.Count} schema errors.");
    }
}
