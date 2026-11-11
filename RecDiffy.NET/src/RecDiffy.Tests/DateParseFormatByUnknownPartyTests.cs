namespace RecDiffy.Tests;

// date.parse with formatBy needs the party's format only to read text. A blank cell is a silent null and a typed date
// reads with no format, as they do when the party's format has no year and when no format is declared. When the party
// has no reference-data format, DeclaredDateParsing.Apply rejects the cell before it looks at it, so both are DateParse (#275).
public class DateParseFormatByUnknownPartyTests
{
    private static readonly RuleBench Parse = new RuleBench("date.parse").With("from", "Raw").With("to", "Day").With("formatBy", "Party");

    [Theory]
    [InlineData("GAMMA")]
    [InlineData("")]
    public void A_blank_cell_is_a_silent_null_whatever_the_party(string party)
    {
        var row = Parse.Apply(DateParseInputTests.PartyRow(party, null));

        Assert.True(row.Values["Day"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("GAMMA")]
    [InlineData("")]
    public void A_typed_date_reads_whatever_the_party(string party)
    {
        var row = Parse.Apply(DateParseInputTests.PartyRow(party, new DateOnly(2025, 6, 11)));

        Assert.Equal(new DateOnly(2025, 6, 11), row.Values["Day"].Date);
        Assert.Empty(row.Diagnostics);
    }
}
