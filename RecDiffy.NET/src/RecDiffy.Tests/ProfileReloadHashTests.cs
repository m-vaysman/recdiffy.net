using System.Globalization;

namespace RecDiffy.Tests;

public class ProfileReloadHashTests
{
    [Fact(Skip = "🔴 #206: the parser adds rowKey \"\" for a side that declares only a reading, so the hash changes after a JSON round trip.")]
    public void A_built_profile_keeps_its_hash_through_json_when_a_side_declares_a_culture_but_no_row_key()
    {
        var built = ProfileBuilder.Create("p", "1")
            .Key("Sku", FieldType.Symbol)
            .Culture(Side.Left, CultureInfo.GetCultureInfo("de-DE"))
            .Path("all", path => path.NaturalKey("P1", "Sku"))
            .Build(RuleRegistry.CreateCore());
        Assert.True(built.Succeeded);
        var document = built.Document!;

        var reread = ProfileDocument.Parse(document.ToJson());

        Assert.Equal(
            Reconciler.Compile(document, RuleRegistry.CreateCore()).Hash,
            Reconciler.Compile(reread, RuleRegistry.CreateCore()).Hash);
    }
}
