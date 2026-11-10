using RecDiffy.Rules;

namespace RecDiffy.Tests;

// text.copy, as the registry builds it today (InvariantTextCopyRule): the copy is the trimmed raw text, and a blank cell
// is null.
public class TextCopyInputTests
{
    private static readonly RuleBench Copy = new RuleBench("text.copy").With("from", "Note").With("to", "Memo");

    [Fact]
    public void The_copy_is_the_trimmed_raw_text()
    {
        var row = Copy.Apply(("Note", "  late fill, see desk  "));

        Assert.Equal(CanonicalCell.FromText("late fill, see desk"), row.Values["Memo"]);
        Assert.Equal(new TraceStep("text.copy", "Memo", "late fill, see desk", "late fill, see desk"), Assert.Single(row.Traces));
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void Inner_white_space_and_case_are_kept() =>
        Assert.Equal("A  b\tC", Copy.Apply(("Note", "A  b\tC")).Display("Memo"));

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("spaces")]
    [InlineData("absent")]
    public void A_blank_cell_is_a_silent_null(string kind)
    {
        var row = kind switch
        {
            "null" => Copy.Apply(("Note", null)),
            "empty" => Copy.Apply(("Note", "")),
            "spaces" => Copy.Apply(("Note", " \t ")),
            _ => Copy.Apply(("Other", "x")),
        };

        Assert.True(row.Values["Memo"].IsNull);
        Assert.Equal(FieldType.Text, row.Values["Memo"].Type);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void A_copy_overwrites_an_earlier_value_of_its_field()
    {
        var first = Copy.Apply(("Note", "first"), ("Other", "second"));

        var row = Copy.With("from", "Other").Apply(first);

        Assert.Equal("second", row.Display("Memo"));
        Assert.Equal(2, row.Traces.Count);
    }
}
