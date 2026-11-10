using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class SheetNamesTests
{
    [Fact]
    public void The_name_follows_the_pattern_with_the_time_the_data_was_read() =>
        Assert.Equal("RecDiffy 2026-11-10 14.32", SheetNames.Base(new DateTimeOffset(2026, 11, 10, 14, 32, 59, TimeSpan.FromHours(-5))));

    [Fact]
    public void A_free_name_is_kept() =>
        Assert.Equal("RecDiffy 2026-11-10 14.32", SheetNames.Unique("RecDiffy 2026-11-10 14.32", ["Sheet1"]));

    [Fact]
    public void A_taken_name_gets_the_next_free_number_ignoring_case()
    {
        Assert.Equal("RecDiffy 2026-11-10 14.32 (2)", SheetNames.Unique("RecDiffy 2026-11-10 14.32", ["recdiffy 2026-11-10 14.32"]));
        Assert.Equal(
            "RecDiffy 2026-11-10 14.32 (3)",
            SheetNames.Unique("RecDiffy 2026-11-10 14.32", ["RecDiffy 2026-11-10 14.32", "RecDiffy 2026-11-10 14.32 (2)"]));
    }

    [Fact]
    public void Characters_Excel_refuses_in_a_sheet_name_are_replaced() =>
        Assert.Equal("a-b-c-d-e-f-g-h", SheetNames.Unique("a:b\\c/d?e*f[g]h", []));

    [Theory]
    [InlineData("'quoted'", "quoted")]
    [InlineData("   ", "RecDiffy")]
    [InlineData("''", "RecDiffy")]
    public void A_name_neither_starts_nor_ends_with_an_apostrophe_and_is_never_empty(string wanted, string expected) =>
        Assert.Equal(expected, SheetNames.Unique(wanted, []));

    [Fact]
    public void A_long_name_is_cut_to_31_characters_leaving_room_for_its_number()
    {
        var wanted = new string('x', 40);

        var first = SheetNames.Unique(wanted, []);
        var second = SheetNames.Unique(wanted, [first]);

        Assert.Equal(new string('x', 31), first);
        Assert.Equal($"{new string('x', 27)} (2)", second);
        Assert.Equal(SheetNames.MaxLength, second.Length);
    }

    [Fact]
    public void Excel_reserves_History_so_it_is_never_used() =>
        Assert.Equal("History (2)", SheetNames.Unique("History", []));

    [Fact]
    public void Each_page_gets_its_own_name_in_order()
    {
        Assert.Equal(
            ["RecDiffy 2026-11-10 14.32", "RecDiffy 2026-11-10 14.32 (2)", "RecDiffy 2026-11-10 14.32 (3)"],
            SheetNames.ForPages("RecDiffy 2026-11-10 14.32", 3, ["Sheet2"]));
    }
}
