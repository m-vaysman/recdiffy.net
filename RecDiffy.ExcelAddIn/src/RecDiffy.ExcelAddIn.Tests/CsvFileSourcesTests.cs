using System.IO;
using System.Text;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.IO;

namespace RecDiffy.ExcelAddIn.Tests;

public sealed class CsvFileSourcesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("recdiffy-csv-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_csv_file_previews_its_headers_its_rows_and_its_row_count()
    {
        var path = Write("trades.csv", "Ref,Quantity,Price\nT-1,10,1.5\nT-2,20,2.5\n", Encoding.UTF8);

        var preview = Assert.IsType<CsvPeek.Read>(CsvFileSources.Peek(CsvSource.At(path), CancellationToken.None)).Preview;

        Assert.Equal(["Ref", "Quantity", "Price"], preview.Headers);
        Assert.Equal([["T-1", "10", "1.5"], ["T-2", "20", "2.5"]], preview.Rows);
        Assert.Equal(2, preview.RowCount);
    }

    [Fact]
    public void A_large_file_is_counted_whole_and_previewed_by_a_small_sample()
    {
        var path = Write("large.csv", "Ref,Quantity\n" + string.Concat(Enumerable.Range(1, 10_000).Select(row => $"T-{row},{row}\n")), Encoding.UTF8);

        var preview = Assert.IsType<CsvPeek.Read>(CsvFileSources.Peek(CsvSource.At(path), CancellationToken.None)).Preview;

        Assert.Equal(10_000, preview.RowCount);
        Assert.Equal(SourcePreview.SampleSize, preview.Rows.Count);
    }

    [Fact]
    public void A_semicolon_file_in_windows_1252_is_read_with_those_options()
    {
        var path = Write("broker.csv", "Ref;Client\nT-1;Müller\n", CsvFileSources.EncodingOf("windows-1252"));
        var source = CsvSource.At(path) with { Delimiter = ';', Encoding = "windows-1252" };

        var preview = Assert.IsType<CsvPeek.Read>(CsvFileSources.Peek(source, CancellationToken.None)).Preview;

        Assert.Equal([["T-1", "Müller"]], preview.Rows);
    }

    [Fact]
    public void Bytes_that_are_not_valid_in_the_chosen_encoding_are_rejected_with_the_encoding_named()
    {
        var path = Path.Combine(_folder, "latin.csv");
        File.WriteAllBytes(path, [.. "Ref,Client\nT-1,M"u8, 0xFC, .. "ller\n"u8]);

        var rejected = Assert.IsType<CsvPeek.Rejected>(CsvFileSources.Peek(CsvSource.At(path), CancellationToken.None));

        Assert.Equal("latin.csv has bytes that aren't valid UTF-8. Choose the file's encoding.", rejected.Reason);
    }

    [Fact]
    public void A_rejected_file_is_closed_again_so_other_programs_can_change_it()
    {
        var path = Path.Combine(_folder, "latin.csv");
        File.WriteAllBytes(path, [.. "Ref,Client\nT-1,M"u8, 0xFC, .. "ller\n"u8]);
        Assert.IsType<CsvPeek.Rejected>(CsvFileSources.Peek(CsvSource.At(path), CancellationToken.None));

        using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.True(reopened.CanWrite);
    }

    [Fact]
    public void A_file_another_program_has_open_can_still_be_previewed()
    {
        var path = Write("open.csv", "Ref\nT-1\n", Encoding.UTF8);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.IsType<CsvPeek.Read>(CsvFileSources.Peek(CsvSource.At(path), CancellationToken.None));
    }

    [Fact]
    public void Without_a_header_row_the_columns_are_numbered_and_every_row_is_data()
    {
        var path = Write("plain.csv", "T-1,10\nT-2,20\n", Encoding.UTF8);

        var preview = Assert.IsType<CsvPeek.Read>(CsvFileSources.Peek(CsvSource.At(path) with { HasHeader = false }, CancellationToken.None)).Preview;

        Assert.Equal(["Column1", "Column2"], preview.Headers);
        Assert.Equal(2, preview.RowCount);
    }

    [Fact]
    public void A_repeated_column_name_is_rejected_with_the_same_message_as_RecDiffy_IO()
    {
        var path = Write("repeated.csv", "Ref,Ref\nT-1,T-2\n", Encoding.UTF8);

        var rejected = Assert.IsType<CsvPeek.Rejected>(CsvFileSources.Peek(CsvSource.At(path), CancellationToken.None));

        Assert.Contains("'Ref' appear more than once", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_is_rejected_with_its_name() =>
        Assert.Equal(
            "gone.csv wasn't found. Choose the file again.",
            Assert.IsType<CsvPeek.Rejected>(CsvFileSources.Peek(CsvSource.At(Path.Combine(_folder, "gone.csv")), CancellationToken.None)).Reason);

    [Fact]
    public void A_file_another_program_holds_open_is_rejected_not_thrown()
    {
        var path = Write("locked.csv", "Ref\nT-1\n", Encoding.UTF8);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var rejected = Assert.IsType<CsvPeek.Rejected>(CsvFileSources.Peek(CsvSource.At(path), CancellationToken.None));

        Assert.StartsWith("RecDiffy couldn't read locked.csv:", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_offered_encoding_resolves_and_rejects_invalid_bytes()
    {
        var encodings = EncodingChoice.All.Select(choice => CsvFileSources.EncodingOf(choice.Name)).ToArray();

        Assert.Equal([65001, 1200, 1201, 1252, 28591], encodings.Select(encoding => encoding.CodePage));
        Assert.All(encodings, encoding => Assert.IsType<DecoderExceptionFallback>(encoding.DecoderFallback));
    }

    [Fact]
    public void The_options_carry_every_setting_and_a_missing_delimiter_means_detection()
    {
        var options = CsvFileSources.Options(new CsvSource("x.csv", HasHeader: false, Delimiter: null, Quote: '\'', Escape: '\\', Encoding: "utf-16"));

        Assert.False(options.HasHeader);
        Assert.Equal(CsvDelimiter.DetectAutomatically, options.Delimiter);
        Assert.Equal(('\'', '\\', 1200), (options.Quote, options.Escape, options.Encoding.CodePage));
    }

    private string Write(string name, string text, Encoding encoding)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text, encoding);
        return path;
    }
}
