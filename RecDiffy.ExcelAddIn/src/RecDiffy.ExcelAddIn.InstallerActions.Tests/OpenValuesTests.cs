using RecDiffy.ExcelAddIn.InstallerActions;

namespace RecDiffy.ExcelAddIn.InstallerActions.Tests;

public sealed class OpenValuesTests
{
    private const string Folder = @"C:\Users\pat\AppData\Local\Programs\RecDiffy\";
    private const string AddIn = Folder + ExcelBitness.AddIn64;
    private const string Solver = @"/R ""C:\Program Files\Microsoft Office\root\Office16\Library\SOLVER\SOLVER.XLAM""";
    private const string Other = @"/R ""C:\Tools\Other.xll""";

    [Fact]
    public void With_no_add_ins_RecDiffy_becomes_OPEN()
    {
        var current = Values();

        var changes = OpenValues.Changes(current, OpenValues.WithAddIn(OpenValues.Ordered(current), AddIn));

        Assert.Equal([Pair("OPEN", $"/R \"{AddIn}\"")], changes.Writes);
        Assert.Empty(changes.Deletes);
    }

    [Fact]
    public void RecDiffy_goes_after_the_add_ins_already_there()
    {
        var current = Values(("OPEN", Solver), ("OPEN1", Other));

        var changes = OpenValues.Changes(current, OpenValues.WithAddIn(OpenValues.Ordered(current), AddIn));

        Assert.Equal([Pair("OPEN2", $"/R \"{AddIn}\"")], changes.Writes);
        Assert.Empty(changes.Deletes);
    }

    [Fact]
    public void Registering_twice_changes_nothing()
    {
        var current = Values(("OPEN", Solver), ("OPEN1", $"/R \"{AddIn}\""));

        var changes = OpenValues.Changes(current, OpenValues.WithAddIn(OpenValues.Ordered(current), AddIn));

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Removing_RecDiffy_closes_the_gap_so_Excel_still_loads_the_rest()
    {
        var current = Values(("OPEN", Solver), ("OPEN1", $"/R \"{AddIn}\""), ("OPEN2", Other));

        var changes = OpenValues.Changes(current, OpenValues.WithoutFolder(OpenValues.Ordered(current), Folder));

        Assert.Equal([Pair("OPEN1", Other)], changes.Writes);
        Assert.Equal(["OPEN2"], changes.Deletes);
    }

    [Fact]
    public void Removing_finds_either_bitness_in_the_install_folder_whatever_its_case()
    {
        var current = Values(("OPEN", $"/R \"{Folder.ToUpperInvariant()}{ExcelBitness.AddIn32}\""));

        var changes = OpenValues.Changes(current, OpenValues.WithoutFolder(OpenValues.Ordered(current), Folder));

        Assert.Empty(changes.Writes);
        Assert.Equal(["OPEN"], changes.Deletes);
    }

    [Fact]
    public void Values_are_read_in_Excel_s_order_and_other_values_are_left_alone()
    {
        var current = Values(("OPEN10", "ten"), ("OPEN2", "two"), ("open", "zero"), ("Options", "not an add-in"), ("OPENX", "not an add-in"));

        Assert.Equal(["zero", "two", "ten"], OpenValues.Ordered(current));
    }

    [Fact]
    public void A_gap_left_by_someone_else_is_closed_when_RecDiffy_is_added()
    {
        var current = Values(("OPEN", Solver), ("OPEN2", Other));

        var changes = OpenValues.Changes(current, OpenValues.WithAddIn(OpenValues.Ordered(current), AddIn));

        Assert.Equal([Pair("OPEN1", Other), Pair("OPEN2", $"/R \"{AddIn}\"")], changes.Writes);
        Assert.Empty(changes.Deletes);
    }

    [Theory]
    [InlineData(0, "OPEN")]
    [InlineData(1, "OPEN1")]
    [InlineData(12, "OPEN12")]
    public void Names_follow_Excel_s_pattern(int index, string name) => Assert.Equal(name, OpenValues.NameAt(index));

    private static Dictionary<string, string> Values(params (string Name, string Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.OrdinalIgnoreCase);

    private static KeyValuePair<string, string> Pair(string name, string value) => new(name, value);
}
