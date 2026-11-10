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

        Assert.Contains(ArgonExcelHoldsTests.Texts(path), text => text == before + '�' + after);
        Assert.Empty(Workbooks.SchemaErrors(path));
    }
}
