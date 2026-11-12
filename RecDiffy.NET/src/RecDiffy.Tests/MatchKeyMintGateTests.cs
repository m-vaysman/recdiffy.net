using System.Reflection;

namespace RecDiffy.Tests;

public class MatchKeyMintGateTests
{
    [Fact]
    public void FromEncoded_and_new_MatchKey_appear_only_in_MatchKey_and_MatchKeys()
    {
        var sources = EngineSources();
        Assert.NotEmpty(sources);

        var leaks = sources
            .Where(file => file.RelativePath is not ("RecDiffy/Matching/MatchKey.cs" or "RecDiffy/Matching/MatchKeys.cs"))
            .Where(file =>
                file.Content.Contains("FromEncoded(", StringComparison.Ordinal)
                || file.Content.Contains("new MatchKey(", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Empty(leaks);
    }

    [Fact]
    public void Every_instance_constructor_is_private()
    {
        var constructors = typeof(MatchKey).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor => Assert.True(constructor.IsPrivate));
    }

    [Fact]
    public void KeyText_exists_and_is_private()
    {
        var keyText = typeof(MatchKeys).GetMethod("KeyText", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(keyText);
        Assert.True(keyText.IsPrivate);
    }

    [Fact]
    public void MatchKey_has_no_conversion_operators()
    {
        var conversions = typeof(MatchKey)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit")
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(conversions);
    }

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
}
