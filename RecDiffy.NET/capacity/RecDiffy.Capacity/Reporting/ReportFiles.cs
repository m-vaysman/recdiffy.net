using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecDiffy.Capacity;

// Source-generated so the report also serializes under Native AOT, where reflection-based System.Text.Json is off.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(CapacityReport))]
internal sealed partial class CapacityJsonContext : JsonSerializerContext;

internal static class ReportFiles
{
    public const string ResultsName = "results.json";
    public const string MarkdownName = "report.md";
    public const string HtmlName = "report.html";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static (string Results, string Markdown, string Html) Write(string folder, CapacityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var results = Path.Combine(folder, ResultsName);
        File.WriteAllText(results, JsonSerializer.Serialize(report, CapacityJsonContext.Default.CapacityReport), Utf8);

        var json = File.ReadAllText(results);
        var markdown = Path.Combine(folder, MarkdownName);
        File.WriteAllText(markdown, MarkdownReport.Render(json), Utf8);
        var html = Path.Combine(folder, HtmlName);
        var page = HtmlReport.Render(json);
        if (!string.Equals(page, HtmlReport.Render(json), StringComparison.Ordinal))
            throw new InvalidOperationException("The HTML report is not deterministic: one results.json gave two pages.");
        File.WriteAllText(html, page, Utf8);
        return (results, markdown, html);
    }
}

internal static class JsonView
{
    public static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    public static JsonElement Field(JsonElement element, string name) => element.GetProperty(name);

    public static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? "";

    public static string? OptionalText(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind is JsonValueKind.Null ? null : element.GetProperty(name).GetString();

    public static double Number(JsonElement element, string name) => element.GetProperty(name).GetDouble();

    public static long Integer(JsonElement element, string name) => element.GetProperty(name).GetInt64();

    public static bool? OptionalBool(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind is JsonValueKind.Null ? null : element.GetProperty(name).GetBoolean();

    public static IReadOnlyList<JsonElement> Items(JsonElement element) => [.. element.EnumerateArray()];
}

internal static class Format
{
    public static string Seconds(double value) => value.ToInvariant("0.000");

    public static string Percent(double value) => value.ToInvariant("0.0");

    public static string Decimal(double value, string format) => value.ToInvariant(format);

    public static string Megabytes(long bytes) => (bytes / 1024.0 / 1024.0).ToInvariant("0.0");

    public static string Gigabytes(long bytes) => (bytes / 1024.0 / 1024.0 / 1024.0).ToInvariant("0.0");

    public static string Count(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Join(IEnumerable<string> values, string separator) => string.Join(separator, values);
}
