using System.Text;

namespace RecDiffy.IO.Tests;

public class CsvPeekFileHandleTests
{
    [Fact(Skip = "🔴 #207: PeekCsv(path) leaves the file open when the CSV reader can't be created.")]
    public void A_csv_file_that_cannot_be_decoded_is_closed_when_the_peek_fails()
    {
        var path = Path.Combine(TestFiles.Folder(), "latin.csv");
        File.WriteAllBytes(path, [.. "Ref,Client\nT-1,M"u8, 0xFC, .. "ller\n"u8]);
        var options = new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma };

        Assert.Throws<DecoderFallbackException>(() => FileSources.PeekCsv(path, options, sampleSize: 5, seed: 1));

        using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(reopened.CanWrite);
    }
}
