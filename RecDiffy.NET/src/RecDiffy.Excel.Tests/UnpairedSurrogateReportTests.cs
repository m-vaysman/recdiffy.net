using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

// An unpaired UTF-16 surrogate is not an XML 1.0 character, like a control character. The writer replaces it
// with U+FFFD and keeps the rest of the text (#256).
// The surrogate is passed as a number: test data that holds a lone surrogate does not survive serialization.
public class UnpairedSurrogateReportTests
{
    [Theory]
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
