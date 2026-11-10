using System.Globalization;

namespace RecDiffy.Tests;

// #206. The writer puts every side that declares a reading under "sources", and the parser reads a side with no rowKey
// as "" (the row ordinal). A built profile records the same "", so it compiles to one hash before and after JSON.
public class ProfileBuilderSourceKeyTests
{
    public static TheoryData<string> Readings => ["culture left", "culture right", "strict left", "culture both", "culture and strict left"];

    [Theory]
    [MemberData(nameof(Readings))]
    public void A_built_profile_keeps_its_hash_through_json_whatever_reading_a_side_declares(string reading)
    {
        var document = Built(reading switch
        {
            "culture left" => builder => builder.Culture(Side.Left, CultureInfo.GetCultureInfo("de-DE")),
            "culture right" => builder => builder.Culture(Side.Right, CultureInfo.GetCultureInfo("sv-SE")),
            "strict left" => builder => builder.StrictDateFormats(Side.Left, false),
            "culture both" => builder => builder.Culture(Side.Left, CultureInfo.GetCultureInfo("de-DE")).Culture(Side.Right, CultureInfo.GetCultureInfo("en-US")),
            "culture and strict left" => builder => builder.Culture(Side.Left, CultureInfo.GetCultureInfo("fr-FR")).StrictDateFormats(Side.Left, false),
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading, null),
        });

        Assert.Equal(Hash(document), Hash(ProfileDocument.Parse(document.ToJson())));
    }

    // A side with a reading and no row key reads rows by ordinal, the same as before: its recorded key is "".
    [Fact]
    public void A_side_with_a_reading_and_no_row_key_records_the_ordinal_key()
    {
        var document = Built(builder => builder.Culture(Side.Left, CultureInfo.GetCultureInfo("de-DE")));

        Assert.Equal("", Assert.Contains("left", document.SourceKeys));
        Assert.False(document.SourceKeys.ContainsKey("right"));
    }

    // A declared row key is kept as it is, with or without a reading on that side.
    [Fact]
    public void A_declared_row_key_is_kept()
    {
        var document = Built(builder => builder.Culture(Side.Left, CultureInfo.GetCultureInfo("de-DE")).RowKeys("Id", "Ref"));

        Assert.Equal("Id", document.SourceKeys["left"]);
        Assert.Equal("Ref", document.SourceKeys["right"]);
        Assert.Equal(Hash(document), Hash(ProfileDocument.Parse(document.ToJson())));
    }

    // A profile with no side reading declares no sources, before and after JSON.
    [Fact]
    public void A_profile_with_no_side_reading_declares_no_sources()
    {
        var document = Built(builder => builder);

        Assert.Empty(document.SourceKeys);
        Assert.Equal(Hash(document), Hash(ProfileDocument.Parse(document.ToJson())));
    }

    private static ProfileDocument Built(Func<ProfileBuilder, ProfileBuilder> configure)
    {
        var built = configure(ProfileBuilder.Create("p", "1").Key("Sku", FieldType.Symbol))
            .Path("all", path => path.NaturalKey("P1", "Sku"))
            .Build(RuleRegistry.CreateCore());
        Assert.True(built.Succeeded);
        return built.Document!;
    }

    private static string Hash(ProfileDocument document) => Reconciler.Compile(document, RuleRegistry.CreateCore()).Hash;
}
