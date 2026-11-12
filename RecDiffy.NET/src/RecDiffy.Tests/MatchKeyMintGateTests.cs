using System.Reflection;

namespace RecDiffy.Tests;

public class MatchKeyMintGateTests
{
    private static readonly string[] AllowedMintFiles =
    [
        "RecDiffy/Matching/MatchKey.cs",
        "RecDiffy/Matching/MatchKeys.cs",
    ];

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
    public void MatchKey_is_minted_only_in_MatchKey_or_MatchKeys()
    {
        var sources = EngineSources();
        Assert.NotEmpty(sources);

        var mintLeaks = sources
            .Where(file => !AllowedMintFiles.Contains(file.RelativePath, StringComparer.Ordinal))
            .Where(file =>
                file.Content.Contains("FromEncoded(", StringComparison.Ordinal)
                || file.Content.Contains("new MatchKey(", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Empty(mintLeaks);
    }

    [Fact]
    public void MatchKey_constructors_are_private_KeyText_is_private_and_no_conversion_operators_exist()
    {
        var constructors = typeof(MatchKey).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotEmpty(constructors);
        Assert.All(constructors, ctor => Assert.True(ctor.IsPrivate));

        var keyText = typeof(MatchKeys).GetMethod("KeyText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(keyText);
        Assert.True(keyText.IsPrivate);

        var conversions = typeof(MatchKey)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit")
            .ToArray();
        Assert.Empty(conversions);
    }
}
