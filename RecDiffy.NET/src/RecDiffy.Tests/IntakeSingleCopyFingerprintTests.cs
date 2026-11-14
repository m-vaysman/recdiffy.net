namespace RecDiffy.Tests;

// #393: intake builds a row's fingerprint only when another row has the same key. The fingerprint reads every cell as
// text, so a cell that counts its own ToString calls shows whether one was built. No rule reads the Note column.
public class IntakeSingleCopyFingerprintTests
{
    [Fact]
    public void A_row_that_is_the_only_copy_of_its_key_is_not_fingerprinted()
    {
        var note = new CountingCell("n");

        var (left, _) = Intake.Normalize(Profile(), [Row("A", 0, "1", note)], [], ReferenceDataSet.Empty);

        var row = Assert.Single(left);
        Assert.Equal(0, note.Reads);
        Assert.False(row.Quarantined);
        Assert.Equal([new SourceRef("left", "A", 0)], row.Members);
        Assert.Empty(row.Context.Diagnostics);
        Assert.Equal(1m, row.Context.Values["Quantity"].Number);
    }

    [Fact]
    public void Rows_that_share_a_key_are_still_fingerprinted_and_collapsed()
    {
        var first = new CountingCell("n");
        var second = new CountingCell("n");

        var (left, _) = Intake.Normalize(Profile(), [Row("A", 0, "1", first), Row("A", 1, "1", second)], [], ReferenceDataSet.Empty);

        var row = Assert.Single(left);
        Assert.True(first.Reads > 0);
        Assert.True(second.Reads > 0);
        Assert.False(row.Quarantined);
        Assert.Equal([new SourceRef("left", "A", 0), new SourceRef("left", "A", 1)], row.Members);
        Assert.Equal("DuplicateRowCollapsed", Assert.Single(row.Context.Diagnostics).Code);
    }

    [Fact]
    public void Rows_that_share_a_key_and_differ_are_still_quarantined()
    {
        var (left, _) = Intake.Normalize(Profile(), [Row("A", 0, "1", new CountingCell("n")), Row("A", 1, "1", new CountingCell("other"))], [], ReferenceDataSet.Empty);

        Assert.Equal(2, left.Count);
        Assert.All(left, row => Assert.True(row.Quarantined));
        Assert.All(left, row => Assert.Contains(row.Context.Diagnostics, diagnostic => diagnostic.Code == "DuplicateConflict"));
    }

    // Unique keys and shared keys in one file keep the order they had before: groups in first-seen key order.
    [Fact]
    public void A_file_of_unique_and_repeated_keys_keeps_its_group_order()
    {
        RawRecord[] rows =
        [
            Row("B", 0, "2", "x"),
            Row("A", 1, "1", "x"),
            Row("C", 2, "3", "x"),
            Row("A", 3, "1", "x"),
        ];

        var (left, _) = Intake.Normalize(Profile(), rows, [], ReferenceDataSet.Empty);

        Assert.Equal(["B", "A", "C"], left.Select(row => row.Members[0].RowKey), StringComparer.Ordinal);
        Assert.Equal([1, 2, 1], left.Select(row => row.Members.Count));
    }

    private static RawRecord Row(string key, int ordinal, string quantity, object note) => new(
        new SourceRef("left", key, ordinal),
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["Quantity"] = quantity, ["Note"] = note });

    private sealed class CountingCell(string text)
    {
        public int Reads { get; private set; }

        public override string ToString()
        {
            Reads++;
            return text;
        }
    }

    private static CompiledProfile Profile() => Reconciler.Compile(ProfileDocument.Parse("""
        {
          "formatVersion": 1,
          "profileId": "single-copy",
          "version": "1.0.0",
          "culture": "",
          "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
          "normalization": { "left": { "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] } },
          "paths": [ { "id": "only", "route": { "rule": "route.all" } } ]
        }
        """), RuleRegistry.CreateCore());
}
