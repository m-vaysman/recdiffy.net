using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

// #116 "untested but holds" item 8, at the edges of the 32,767-character cut that ArgonExcelHoldsTests leaves out.
// The writer replaces each character XML 1.0 cannot carry with U+FFFD, then cuts the text, and never ends a cell on
// half of a surrogate pair.
public class CellTextLimitTests
{
    private const int Limit = 32_767;

    // Tab and line feed are XML 1.0 characters, so they stay. The control character past the cut goes with the cut.
    [Fact]
    public void A_control_character_on_the_last_place_a_cell_holds_is_replaced_and_the_rest_is_cut()
    {
        var text = "A\t\n" + new string('A', Limit - 4) + "\u001F" + "\u000B" + new string('B', 100);

        var path = ArgonExcelHoldsTests.Write(text);

        Assert.Contains(ArgonExcelHoldsTests.Texts(path), cell => cell == "A\t\n" + new string('A', Limit - 4) + "�");
        Assert.Empty(Workbooks.SchemaErrors(path));
    }

    [Fact]
    public void A_cell_of_exactly_the_limit_keeps_its_length_and_loses_its_control_characters()
    {
        var text = "\u0002" + new string('A', Limit - 2) + "\u0002";

        var path = ArgonExcelHoldsTests.Write(text);

        Assert.Contains(ArgonExcelHoldsTests.Texts(path), cell => cell == "�" + new string('A', Limit - 2) + "�");
        Assert.Empty(Workbooks.SchemaErrors(path));
    }

    // The control character is replaced first. The cut then stops before the high surrogate on the last place, so
    // the cell holds one character less than the limit.
    [Fact]
    public void A_control_character_beside_a_surrogate_pair_on_the_limit_is_replaced_and_the_pair_is_dropped()
    {
        var text = new string('A', Limit - 2) + "\u0001" + "\U0001F600" + new string('B', 100);

        var path = ArgonExcelHoldsTests.Write(text);

        Assert.Contains(ArgonExcelHoldsTests.Texts(path), cell => cell == new string('A', Limit - 2) + "�");
        Assert.Empty(Workbooks.SchemaErrors(path));
    }
}
