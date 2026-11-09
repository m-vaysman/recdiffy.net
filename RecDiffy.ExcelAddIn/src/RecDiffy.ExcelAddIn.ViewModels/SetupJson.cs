using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecDiffy.ExcelAddIn.ViewModels;

public static class SetupJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new ProfileDocumentConverter() },
    };

    public static string Write(ReconciliationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return JsonSerializer.Serialize(setup, Options);
    }

    public static ReconciliationSetup Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ReconciliationSetup>(json, Options) ?? throw new JsonException("A reconciliation setup can't be null.");
    }

    private sealed class ProfileDocumentConverter : JsonConverter<ProfileDocument>
    {
        public override ProfileDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return ProfileDocument.Parse(document.RootElement.GetRawText());
        }

        public override void Write(Utf8JsonWriter writer, ProfileDocument value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToJson(indented: false));
    }
}
