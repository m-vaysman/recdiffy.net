using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private const string IncludeExactMatches = "includeExactMatches";
    private const string OfferExplanation = "offerExplanation";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _path = path ?? throw new ArgumentNullException(nameof(path));

    public static string ForWindowsUser() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RecDiffy", "settings.json");

    public AddInSettings Load()
    {
        try
        {
            return File.Exists(_path) ? Parse(File.ReadAllText(_path)) : AddInSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AddInSettings.Default;
        }
    }

    public string? Save(AddInSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(_path)) ?? throw new IOException($"{_path} has no folder.");
            Directory.CreateDirectory(folder);
            var draft = Path.Combine(folder, $"{Path.GetFileName(_path)}.tmp");
            File.WriteAllText(draft, Format(settings));
            File.Move(draft, _path, overwrite: true);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    public static AddInSettings Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonNode.Parse(json) is JsonObject settings
                ? new AddInSettings(
                    Flag(settings, IncludeExactMatches, AddInSettings.Default.IncludeExactMatches),
                    Flag(settings, OfferExplanation, AddInSettings.Default.OffersExplanation))
                : AddInSettings.Default;
        }
        catch (JsonException)
        {
            return AddInSettings.Default;
        }
    }

    public static string Format(AddInSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new JsonObject
        {
            [IncludeExactMatches] = settings.IncludeExactMatches,
            [OfferExplanation] = settings.OffersExplanation,
        }.ToJsonString(Indented);
    }

    private static bool Flag(JsonObject settings, string name, bool fallback) =>
        settings[name] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;
}
