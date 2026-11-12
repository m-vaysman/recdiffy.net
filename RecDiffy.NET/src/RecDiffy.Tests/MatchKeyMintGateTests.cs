using System.Reflection;

namespace RecDiffy.Tests;

// #350 Phase 1 (D5): a MatchKey can only be minted inside Matching/MatchKey.cs and Matching/MatchKeys.cs. The source scan
// catches a new call site; the reflection checks catch a widened constructor or a conversion operator.
public class MatchKeyMintGateTests
{
    private static readonly string[] MintingFiles =
    [
        "RecDiffy/Matching/MatchKey.cs",
        "RecDiffy/Matching/MatchKeys.cs",
    ];

    private static readonly string[] MintTokens = ["FromEncoded(", "new MatchKey("];

    private static IReadOnlyList<(string RelativePath, string Content)> EngineSources()
    {
        string[] roots =
        [
            Path.Combine(ProfileCorpus.Source, "RecDiffy"),
            Path.Combine(ProfileCorpus.Source, "RecDiffy.RulePacks.Trading"),
        ];

        return
        [
            .. roots
                .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(file => (
                    RelativePath: Path.GetRelativePath(ProfileCorpus.Source, file).Replace('\\', '/'),
                    Content: File.ReadAllText(file))),
        ];
    }

    [Fact]
    public void Only_MatchKey_and_MatchKeys_mint_a_key()
    {
        var sources = EngineSources();
        Assert.NotEmpty(sources);

        var mintingSites = sources
            .Where(file => MintingFiles.Contains(file.RelativePath, StringComparer.Ordinal) is false)
            .Where(file => MintTokens.Any(token => file.Content.Contains(token, StringComparison.Ordinal)))
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Empty(mintingSites);
    }

    [Fact]
    public void Every_instance_constructor_of_MatchKey_is_private()
    {
        var constructors = typeof(MatchKey)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor => Assert.True(constructor.IsPrivate, constructor.ToString()));
    }

    [Fact]
    public void MatchKeys_KeyText_is_private()
    {
        var keyText = typeof(MatchKeys).GetMethod("KeyText", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(keyText);
        Assert.True(keyText.IsPrivate);
    }

    [Fact]
    public void MatchKey_declares_no_conversion_operators()
    {
        var conversions = typeof(MatchKey)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit")
            .Select(method => method.ToString())
            .ToArray();

        Assert.Empty(conversions);
    }
}
