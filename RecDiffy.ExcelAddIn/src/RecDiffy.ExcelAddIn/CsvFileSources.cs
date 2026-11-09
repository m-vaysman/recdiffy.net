using System.IO;
using System.Text;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.IO;

namespace RecDiffy.ExcelAddIn;

public sealed class CsvFileSources : ICsvSources
{
    public Task<CsvPeek> PeekAsync(CsvSource source, CancellationToken cancellationToken) =>
        Task.Run(() => Peek(source, cancellationToken), cancellationToken);

    public static CsvPeek Peek(CsvSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            using var file = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var peek = FileSources.PeekCsv(file, Options(source), SourcePreview.SampleSize, SourcePreview.Seed, cancellationToken);
            return new CsvPeek.Read(new SourcePreview(peek.Headers, [.. peek.Rows.Select(row => row.Values)], peek.RowCount));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException or FormatException)
        {
            return new CsvPeek.Rejected(Reason(source, exception));
        }
    }

    public static CsvOptions Options(CsvSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CsvOptions
        {
            HasHeader = source.HasHeader,
            Delimiter = source.Delimiter is { } character ? CsvDelimiter.Of(character) : CsvDelimiter.DetectAutomatically,
            Quote = source.Quote,
            Escape = source.Escape,
            Encoding = EncodingOf(source.Encoding),
        };
    }

    public static Encoding EncodingOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return CodePagesEncodingProvider.Instance.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            ?? Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public static string Reason(CsvSource source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(exception);
        var file = Path.GetFileName(source.Path);
        return exception switch
        {
            FileNotFoundException or DirectoryNotFoundException => $"{file} wasn't found. Choose the file again.",
            UnauthorizedAccessException => $"RecDiffy isn't allowed to read {file}.",
            DecoderFallbackException => $"{file} has bytes that aren't valid {EncodingChoice.For(source.Encoding).Label}. Choose the file's encoding.",
            InvalidDataException => exception.Message,
            FormatException => $"{file} can't be read as CSV with these settings: {exception.Message}",
            _ => $"RecDiffy couldn't read {file}: {exception.Message}",
        };
    }
}
