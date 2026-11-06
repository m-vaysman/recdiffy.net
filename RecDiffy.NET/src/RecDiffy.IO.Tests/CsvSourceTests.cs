using System.Text;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.IO.Tests;

public class CsvSourceTests
{
    private const string Fields = """
        { "name": "Id", "type": "Text", "role": "Text" },
        { "name": "Name", "type": "Text", "role": "Text" },
        { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
        """;

    private const string Rules = """
        "identify": [ { "rule": "text.copy", "from": "Id", "to": "Id" }, { "rule": "text.copy", "from": "Name", "to": "Name" } ],
        "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
        """;

    private static readonly string[][] Rows = [["Id", "Name", "Amount"], ["1", "Alpha", "10.50"], ["2", "Beta", "-3"]];

    [Theory]
    [InlineData(',')]
    [InlineData('\t')]
    [InlineData('|')]
    [InlineData(';')]
    public void Each_common_delimiter_reads_every_row(char delimiter)
    {
        var path = TestFiles.Csv(TestFiles.Folder(), "data.csv", Rows, delimiter);

        var rows = Probe.Left(FileSources.FromCsv("left", path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Of(delimiter) }), Fields, Rules);

        Assert.Equal(["1|Alpha|10.50", "2|Beta|-3"], rows.Select(Line), StringComparer.Ordinal);
    }

    [Fact]
    public void The_named_delimiters_are_the_characters_they_name()
    {
        Assert.Equal([',', '\t', '|', ';'], new[] { CsvDelimiter.Comma, CsvDelimiter.Tab, CsvDelimiter.Pipe, CsvDelimiter.Semicolon }.Select(delimiter => delimiter.Character!.Value));
        Assert.Null(CsvDelimiter.DetectAutomatically.Character);
        Assert.Throws<ArgumentException>(() => CsvDelimiter.Of('\n'));
    }

    [Fact]
    public void A_headerless_file_is_mapped_by_position_and_its_first_row_is_data()
    {
        var path = TestFiles.Csv(TestFiles.Folder(), "data.csv", Rows.Skip(1), ',');
        const string positional = """
            "identify": [ { "rule": "text.copy", "from": "Column1", "to": "Id" }, { "rule": "text.copy", "from": "Column2", "to": "Name" } ],
            "parse": [ { "rule": "decimal.parse", "from": "Column3", "to": "Amount" } ]
            """;

        var rows = Probe.Left(FileSources.FromCsv("left", path, new CsvOptions { HasHeader = false, Delimiter = CsvDelimiter.Comma }), Fields, positional);

        Assert.Equal(["1|Alpha|10.50", "2|Beta|-3"], rows.Select(Line), StringComparer.Ordinal);
        Assert.Equal(["Column1", "Column2", "Column3"], FileSources.PeekCsv(path, new CsvOptions { HasHeader = false, Delimiter = CsvDelimiter.Comma }, 10, 0).Headers, StringComparer.Ordinal);
    }

    [Fact]
    public void Quoted_fields_keep_embedded_delimiters_quotes_and_line_breaks()
    {
        string[][] rows = [["Id", "Name", "Amount"], ["1", "Smith, John", "1"], ["2", "say \"hi\"", "2"], ["3", "line one\nline two", "3"]];
        var path = TestFiles.Csv(TestFiles.Folder(), "quoted.csv", rows, ',');

        var read = Probe.Left(FileSources.FromCsv("left", path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma }), Fields, Rules);

        Assert.Equal(["Smith, John", "say \"hi\"", "line one\nline two"], read.Select(row => row.Text("Name")), StringComparer.Ordinal);
    }

    [Fact]
    public void A_backslash_escape_can_be_chosen()
    {
        var folder = TestFiles.Folder();
        var path = Path.Combine(folder, "escaped.csv");
        File.WriteAllText(path, "Id,Name,Amount\r\n1,\"say \\\"hi\\\"\",1\r\n");

        var read = Probe.Left(FileSources.FromCsv("left", path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma, Escape = '\\' }), Fields, Rules);

        Assert.Equal("say \"hi\"", Assert.Single(read).Text("Name"));
    }

    // A semicolon file read as comma-delimited stays one column: nothing is detected unless asked for.
    [Fact]
    public void The_delimiter_is_detected_only_when_asked()
    {
        var path = TestFiles.Csv(TestFiles.Folder(), "semicolon.csv", Rows, ';');

        var stated = FileSources.PeekCsv(path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma }, 10, 0);
        var detected = FileSources.PeekCsv(path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.DetectAutomatically }, 10, 0);

        Assert.Equal(["Id;Name;Amount"], stated.Headers, StringComparer.Ordinal);
        Assert.Equal(["Id", "Name", "Amount"], detected.Headers, StringComparer.Ordinal);
    }

    [Fact]
    public void A_utf8_byte_order_mark_is_not_part_of_the_first_header()
    {
        var path = TestFiles.Csv(TestFiles.Folder(), "bom.csv", Rows, ',', new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var peek = FileSources.PeekCsv(path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma }, 10, 0);

        Assert.Equal("Id", peek.Headers[0]);
    }

    [Fact]
    public void The_stated_encoding_is_used()
    {
        var path = TestFiles.Csv(TestFiles.Folder(), "latin1.csv", [["Id", "Name", "Amount"], ["1", "Café", "1"]], ',', Encoding.Latin1);

        var read = Probe.Left(FileSources.FromCsv("left", path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma, Encoding = Encoding.Latin1 }), Fields, Rules);

        Assert.Equal("Café", Assert.Single(read).Text("Name"));
    }

    [Theory]
    [InlineData("Id,,Amount", "column(s) 2 have no name")]
    [InlineData("Id,Name,Name", "'Name' appear more than once")]
    public void A_header_row_that_cannot_name_every_column_is_refused(string header, string reason)
    {
        var folder = TestFiles.Folder();
        var path = Path.Combine(folder, "bad.csv");
        File.WriteAllText(path, header + "\r\n1,2,3\r\n");

        var failure = Assert.Throws<InvalidDataException>(() => Probe.Left(FileSources.FromCsv("left", path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma }), Fields, Rules));

        Assert.Contains(reason, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_source_is_read_again_on_every_run()
    {
        var path = TestFiles.Csv(TestFiles.Folder(), "data.csv", Rows, ',');
        var source = FileSources.FromCsv("left", path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma });

        Assert.Equal(Probe.Left(source, Fields, Rules).Select(Line), Probe.Left(source, Fields, Rules).Select(Line), StringComparer.Ordinal);
    }

    [Fact]
    public void A_stream_source_reads_once_and_leaves_the_stream_to_its_owner()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name,Amount\r\n1,Alpha,10.50\r\n"));
        var source = FileSources.FromCsv("left", stream, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma });

        Assert.Single(Probe.Left(source, Fields, Rules));
        Assert.True(stream.CanRead);
        Assert.Throws<InvalidOperationException>(() => Probe.Left(source, Fields, Rules));
    }

    [Fact]
    public async Task A_cancelled_run_stops_reading()
    {
        var path = TestFiles.Csv(TestFiles.Folder(), "data.csv", Rows, ',');
        var profile = Reconciler.Compile(ProfileDocument.Parse(FixtureV1.Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore()));
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reconciler.RunAsync(
            profile,
            FileSources.FromCsv("left", path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma }),
            Probe.Empty(),
            ReferenceDataSet.Empty,
            cancellationToken: cancel.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => FileSources.PeekCsv(path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma }, 10, 0, cancel.Token));
    }

    private static string Line(CanonicalRow row) => $"{row.Text("Id")}|{row.Text("Name")}|{row.Text("Amount")}";
}
