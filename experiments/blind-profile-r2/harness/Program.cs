using System.Text;
using RecDiffy;
using RecDiffy.IO;
using RecDiffy.RulePacks.Trading;

namespace BlindDiscovery;

internal static class Program
{
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
                "headers" => HeadersOnly(args[1..]),
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

    private static int HeadersOnly(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        foreach (var file in args)
            Console.WriteLine($"{file}: {string.Join(" | ", Headers(file))}");
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

        if (Open(args[0], args[1], args[2]) is not { } opened)
            return 2;

        var csv = Csv();
        var result = Reconciler.Run(
            opened.Compiled,
            FileSources.FromCsv("left", args[1], csv),
            FileSources.FromCsv("right", args[2], csv),
            ReferenceDataSet.Empty);
        Console.WriteLine(RunDigest.Format(result, [.. opened.Document.Fields.Select(field => field.Name)]));
        return 0;
    }

    private static (ProfileDocument Document, CompiledProfile Compiled)? Open(string profilePath, string leftPath, string rightPath)
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
            return (document, compiled);
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

    private static IReadOnlyList<string> Headers(string path) => FileSources.PeekCsv(path, Csv(), 0, 0).Headers;

    private static CsvOptions Csv() => new() { HasHeader = true, Delimiter = CsvDelimiter.Comma };

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private const string Usage =
        """
        Blind profile discovery harness, round 2.

        headers <file> [files...]
        validate <profile.json> <left> <right>
        run <profile.json> <left> <right>

        headers prints each file's column headers and nothing else.
        validate parses the profile, compiles it with the core rules plus the trading pack, and checks headers.
        run does that, then prints a one-run digest: summary, composition, diagnostics, per-field break stats with up to
        three engine-reported left/right values, and up to three canonical example rows per unmatched alignment.
        CSV is comma-separated with a header row.
        Exit 0 on success, 1 on usage, 2 when a file or profile is rejected.
        """;
}
