using System.Text;
using RecDiffy;
using RecDiffy.IO;
using RecDiffy.RulePacks.Trading;

namespace BlindDiscovery;

internal static class Program
{
    private const int SampleCap = 12;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0] switch
            {
                "peek" => Peek(args[1..]),
                "validate" => Validate(args[1..]),
                "run" => Run(args[1..]),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int Peek(string[] args)
    {
        var files = new List<string>();
        var sample = 6;
        var seed = 1;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--sample" when index + 1 < args.Length && int.TryParse(args[index + 1], out var requested):
                    sample = requested;
                    index++;
                    break;
                case "--seed" when index + 1 < args.Length && int.TryParse(args[index + 1], out var requestedSeed):
                    seed = requestedSeed;
                    index++;
                    break;
                case "--sample" or "--seed":
                    Console.Error.WriteLine(Usage);
                    return 1;
                default:
                    files.Add(args[index]);
                    break;
            }
        }

        if (files.Count == 0 || sample < 0)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var shown = Math.Min(sample, SampleCap);
        if (sample > SampleCap)
            Console.WriteLine($"sample capped at {SampleCap.ToInvariant()}");

        for (var index = 0; index < files.Count; index++)
        {
            if (index > 0)
                Console.WriteLine();
            Console.Write(PeekReport.Format(files[index], shown, seed));
        }

        return 0;
    }

    private static int Validate(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        return Open(args[0], args[1], args[2]) is null ? 2 : 0;
    }

    private static int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        if (Open(args[0], args[1], args[2]) is not { } compiled)
            return 2;

        var csv = Csv();
        var result = Reconciler.Run(
            compiled,
            FileSources.FromCsv("left", args[1], csv),
            FileSources.FromCsv("right", args[2], csv),
            ReferenceDataSet.Empty);
        Console.WriteLine(RunDigest.Format(result));
        return 0;
    }

    private static CompiledProfile? Open(string profilePath, string leftPath, string rightPath)
    {
        var json = File.ReadAllText(profilePath);
        if (Parse(json) is not { } document)
            return null;

        Console.WriteLine("parse ok");
        CompiledProfile compiled;
        try
        {
            compiled = Reconciler.Compile(document, TradingRulePack.AddTo(RuleRegistry.CreateCore()));
        }
        catch (ProfileCompileException ex)
        {
            Console.WriteLine("compile fail");
            foreach (var error in ex.Errors)
                Console.WriteLine($"  {error.Code}: {error.Message}");
            return null;
        }

        Console.WriteLine("compile ok");
        var missing = ProfileCheck.AgainstHeaders(
            document,
            Headers(leftPath),
            Headers(rightPath),
            TradingColumns.Parameters);
        if (missing.Count == 0)
        {
            Console.WriteLine("headers ok");
            return compiled;
        }

        Console.WriteLine("headers fail");
        foreach (var column in missing)
            Console.WriteLine($"  {column.Message} ({column.Location})");
        return null;
    }

    private static ProfileDocument? Parse(string json)
    {
        try
        {
            return ProfileDocument.Parse(json);
        }
        catch (Exception ex) when (ex is ProfileCompileException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            Console.WriteLine("parse fail");
            if (ex is ProfileCompileException compile)
            {
                foreach (var error in compile.Errors)
                    Console.WriteLine($"  {error.Code}: {error.Message}");
            }
            else
            {
                Console.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }
    }

    internal static TabularPeek Load(string path, int sample, int seed) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" or ".xls" or ".xlsb" => FileSources.PeekExcel(
                path,
                new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) },
                sample,
                seed),
            _ => FileSources.PeekCsv(path, Csv(), sample, seed),
        };

    private static IReadOnlyList<string> Headers(string path) => Load(path, 0, 0).Headers;

    private static CsvOptions Csv() => new() { HasHeader = true, Delimiter = CsvDelimiter.Comma };

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private const string Usage =
        """
        Blind profile discovery harness.

        peek <file> [files...] [--sample N] [--seed N]
        validate <profile.json> <left> <right>
        run <profile.json> <left> <right>

        peek prints headers, the row count, a seeded sample of at most 12 rows, and full-file column stats.
        validate parses the profile, compiles it with the core rules plus the trading pack, and checks headers.
        run does that, then prints a one-run digest.
        CSV is comma-separated with a header row. Excel workbooks are read from the first sheet.
        Exit 0 on success, 1 on usage, 2 when a file or profile is rejected.
        """;
}
