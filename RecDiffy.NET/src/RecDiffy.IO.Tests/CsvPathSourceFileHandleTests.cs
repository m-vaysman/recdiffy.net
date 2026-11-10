using System.Text;

namespace RecDiffy.IO.Tests;

public class CsvPathSourceFileHandleTests
{
    private const string Fields = """
        { "name": "Ref", "type": "Text", "role": "Text" },
        { "name": "Client", "type": "Text", "role": "Text" }
        """;

    private const string Rules = """
        "identify": [ { "rule": "text.copy", "from": "Ref", "to": "Ref" }, { "rule": "text.copy", "from": "Client", "to": "Client" } ]
        """;

    [Fact]
    public void A_csv_file_that_cannot_be_decoded_is_closed_when_the_run_fails()
    {
        var path = Undecodable();
        var source = FileSources.FromCsv("left", path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma });

        Assert.Throws<DecoderFallbackException>(() => Probe.Left(source, Fields, Rules));

        using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(reopened.CanWrite);
    }

    [Fact]
    public void A_stream_the_caller_owns_stays_open_when_the_peek_fails()
    {
        using var stream = new MemoryStream([.. "Ref,Client\nT-1,M"u8, 0xFC, .. "ller\n"u8]);

        Assert.Throws<DecoderFallbackException>(() => FileSources.PeekCsv(stream, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma }, sampleSize: 5, seed: 1));

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void A_csv_file_whose_header_is_refused_is_closed()
    {
        var path = Path.Combine(TestFiles.Folder(), "repeated.csv");
        File.WriteAllText(path, "Ref,Ref\nT-1,T-2\n");

        Assert.Throws<InvalidDataException>(() => FileSources.PeekCsv(path, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma }, sampleSize: 5, seed: 1));

        using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(reopened.CanWrite);
    }

    private static string Undecodable()
    {
        var path = Path.Combine(TestFiles.Folder(), "latin.csv");
        File.WriteAllBytes(path, [.. "Ref,Client\nT-1,M"u8, 0xFC, .. "ller\n"u8]);
        return path;
    }
}
