using System.Text.Json.Serialization;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record ReconciliationSetup(SideSetup Left, SideSetup Right, ProfileDocument Profile)
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyOrder(-1)]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool Equals(ReconciliationSetup? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Left == other.Left
        && Right == other.Right
        && string.Equals(Profile.ToJson(indented: false), other.Profile.ToJson(indented: false), StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, Left, Right, Profile.ToJson(indented: false));
}

public sealed record SideSetup(string Alias, SourceSetup Source);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NoSource), "none")]
[JsonDerivedType(typeof(WorkbookSource), "workbook")]
[JsonDerivedType(typeof(CsvSource), "csv")]
public abstract record SourceSetup;

public sealed record NoSource : SourceSetup;

public sealed record WorkbookSource(string Workbook, string? Path, string Sheet, string Address) : SourceSetup;

public sealed record CsvSource(string Path, bool HasHeader, char? Delimiter, char Quote, char Escape, string Encoding) : SourceSetup
{
    public static CsvSource At(string path) => new(path, HasHeader: true, Delimiter: ',', Quote: '"', Escape: '"', Encoding: "utf-8");
}
