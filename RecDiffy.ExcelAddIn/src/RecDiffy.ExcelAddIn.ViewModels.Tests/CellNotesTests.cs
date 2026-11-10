using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class CellNotesTests
{
    [Theory]
    [InlineData("  T-1 ", "2 spaces before the text and a space after it. The run trims white space, so it compares 'T-1'.")]
    [InlineData("  T-1\t", "2 spaces before the text and a tab after it. The run trims white space, so it compares 'T-1'.")]
    [InlineData("T-1\t", "A tab after the text. The run trims white space, so it compares 'T-1'.")]
    [InlineData("\u00A0T-1", "A non-breaking space (U+00A0) before the text. The run trims white space, so it compares 'T-1'.")]
    [InlineData("T-1\r\n", "A line break after the text. The run trims white space, so it compares 'T-1'.")]
    [InlineData("T-1\n\n", "2 line breaks after the text. The run trims white space, so it compares 'T-1'.")]
    [InlineData(" \tT 1 ", "A space and a tab before the text and a space after it. The run trims white space, so it compares 'T 1'.")]
    [InlineData("\u2003T-1", "An em space (U+2003) before the text. The run trims white space, so it compares 'T-1'.")]
    public void White_space_at_the_start_or_end_is_named_and_the_note_says_the_run_trims_it(string value, string note) =>
        Assert.Equal(note, CellNotes.For(value));

    [Theory]
    [InlineData("T-1\u200B", "A zero-width space (U+200B) after the text. The run keeps it, so this value won't match 'T-1'.")]
    [InlineData("\uFEFFT-1", "A byte order mark (U+FEFF) before the text. The run keeps it, so this value won't match 'T-1'.")]
    [InlineData("T-1\u200B\u200B", "2 zero-width spaces (U+200B) after the text. The run keeps them, so this value won't match 'T-1'.")]
    [InlineData("\u200CT-1\u200D", "A zero-width non-joiner (U+200C) before the text and a zero-width joiner (U+200D) after it. The run keeps them, so this value won't match 'T-1'.")]
    [InlineData("T-1\u2060", "A word joiner (U+2060) after the text. The run keeps it, so this value won't match 'T-1'.")]
    [InlineData("T-1\u00AD", "A soft hyphen (U+00AD) after the text. The run keeps it, so this value won't match 'T-1'.")]
    public void Invisible_characters_at_the_start_or_end_are_named_and_the_note_says_the_value_wont_match(string value, string note) =>
        Assert.Equal(note, CellNotes.For(value));

    [Theory]
    [InlineData("T-1\u200B ", "A zero-width space (U+200B) and a space after the text. The run removes white space but not invisible characters, so this value won't match 'T-1'.")]
    [InlineData(" \uFEFFT-1", "A space and a byte order mark (U+FEFF) before the text. The run removes white space but not invisible characters, so this value won't match 'T-1'.")]
    public void White_space_beside_invisible_characters_still_leaves_a_value_that_wont_match(string value, string note) =>
        Assert.Equal(note, CellNotes.For(value));

    [Theory]
    [InlineData("   ", "Only white space: 3 spaces. The run reads this cell as empty.")]
    [InlineData(" \t", "Only white space: a space and a tab. The run reads this cell as empty.")]
    [InlineData("\u00A0", "Only white space: a non-breaking space (U+00A0). The run reads this cell as empty.")]
    public void A_cell_of_only_white_space_is_noted_as_empty_for_the_run(string value, string note) =>
        Assert.Equal(note, CellNotes.For(value));

    [Theory]
    [InlineData("\u200B", "Nothing visible: a zero-width space (U+200B). The run keeps invisible characters, so this cell isn't empty.")]
    [InlineData(" \uFEFF", "Nothing visible: a space and a byte order mark (U+FEFF). The run keeps invisible characters, so this cell isn't empty.")]
    public void A_cell_with_nothing_visible_but_an_invisible_character_is_noted_as_not_empty(string value, string note) =>
        Assert.Equal(note, CellNotes.For(value));

    [Theory]
    [InlineData("T-1")]
    [InlineData("T 1")]
    [InlineData("T\u200B1")]
    [InlineData("")]
    public void A_clean_cell_or_one_with_hidden_characters_only_inside_the_text_gets_no_note(string value) =>
        Assert.Null(CellNotes.For(value));

    [Fact]
    public void Number_date_time_logical_and_empty_cells_get_no_note()
    {
        object?[] values = [10.5, 10m, 3L, new DateTime(2026, 11, 10), new DateTime(2026, 11, 10, 9, 30, 0), new TimeOnly(9, 30), true, null];

        Assert.All(values, value => Assert.Null(CellNotes.For(value)));
    }

    [Fact]
    public void Every_white_space_character_is_named_and_trimmed_by_the_run()
    {
        var whiteSpace = Enumerable.Range(char.MinValue, char.MaxValue + 1).Select(code => (char)code).Where(char.IsWhiteSpace).ToArray();

        Assert.Equal(25, whiteSpace.Length);
        Assert.All(whiteSpace, character =>
        {
            Assert.Equal("T-1", $"T-1{character}".Trim());
            Assert.EndsWith(" after the text. The run trims white space, so it compares 'T-1'.", CellNotes.For($"T-1{character}"), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData('\u200B')]
    [InlineData('\u200C')]
    [InlineData('\u200D')]
    [InlineData('\u2060')]
    [InlineData('\uFEFF')]
    [InlineData('\u00AD')]
    public void Each_invisible_character_is_kept_by_the_run_and_noted(char character)
    {
        Assert.False(char.IsWhiteSpace(character));
        Assert.NotEqual("T-1", $"T-1{character}".Trim(), StringComparer.Ordinal);
        Assert.EndsWith(" after the text. The run keeps it, so this value won't match 'T-1'.", CellNotes.For($"T-1{character}"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_preview_cell_is_found_by_its_row_and_column()
    {
        SourceTable table = new(["Ref", "Quantity", "Trade date"], [["T-1\u200B", 10.5, new DateTime(2026, 11, 10)], [" T-2", 7m, null]]);
        var preview = SourcePreview.Of(table);

        Assert.Equal("A zero-width space (U+200B) after the text. The run keeps it, so this value won't match 'T-1'.", CellNotes.At(preview, 0, 0));
        Assert.Equal("A space before the text. The run trims white space, so it compares 'T-2'.", CellNotes.At(preview, 1, 0));
        Assert.Null(CellNotes.At(preview, 0, 1));
        Assert.Null(CellNotes.At(preview, 0, 2));
        Assert.Null(CellNotes.At(preview, 1, 2));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 3)]
    public void A_row_or_column_outside_the_preview_gets_no_note(int row, int column)
    {
        var preview = SourcePreview.Of(new SourceTable(["Ref"], [[" T-1"], [" T-2"]]));

        Assert.Null(CellNotes.At(preview, row, column));
    }

    [Fact]
    public void No_preview_gets_no_note() => Assert.Null(CellNotes.At(null, 0, 0));

    [Fact]
    public void A_csv_preview_holds_every_cell_as_text_so_padded_numbers_and_dates_are_noted()
    {
        var preview = new SourcePreview(["Ref", "Quantity", "Trade date"], [["T-1", " 10.5", "2026-11-10\u200B"]], 1);

        Assert.Null(CellNotes.At(preview, 0, 0));
        Assert.Equal("A space before the text. The run trims white space, so it compares '10.5'.", CellNotes.At(preview, 0, 1));
        Assert.Equal("A zero-width space (U+200B) after the text. The run keeps it, so this value won't match '2026-11-10'.", CellNotes.At(preview, 0, 2));
    }
}
