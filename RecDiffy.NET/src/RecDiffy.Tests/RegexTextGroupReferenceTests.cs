using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// text.regex rejects, at compile, a replacement that refers to a group the pattern does not have. .NET would write the
// reference as literal text into every key, so "$2" with one group would quietly break every match (#547).
public class RegexTextGroupReferenceTests
{
    private static readonly RuleRegistry Registry = RuleRegistry.CreateCore();

    [Theory]
    [InlineData(@"^TRD-0*(\d+)$", "$2", "$2")]
    [InlineData(@"^TRD-0*(\d+)$", "${2}", "${2}")]
    [InlineData(@"^TRD-0*(\d+)$", "$10", "$10")]
    [InlineData(@"^TRD-0*(\d+)$", "${id}", "${id}")]
    [InlineData(@"^TRD-0*(?<id>\d+)$", "${ID}", "${ID}")]
    [InlineData(@"^TRD-0*(\d+)$", "$99999999999", "$99999999999")]
    [InlineData(@"^TRD$", "x$1", "$1")]
    public void A_reference_to_a_missing_group_fails_compile_with_InvalidParameter(string pattern, string replacement, string reference)
    {
        var failure = Assert.Throws<ProfileCompileException>(() => Build(pattern, replacement));

        var error = Assert.Single(failure.Errors);
        Assert.Equal("InvalidParameter", error.Code);
        Assert.Equal($"text.regex replacement \"{replacement}\" refers to {reference}, a group the pattern \"{pattern}\" does not have.", error.Message);
    }

    [Fact]
    public void Every_missing_reference_is_reported()
    {
        var failure = Assert.Throws<ProfileCompileException>(() => Build(@"^(\d+)$", "$2-$3-$1"));

        Assert.Equal(2, failure.Errors.Count);
    }

    [Theory]
    [InlineData(@"^TRD-0*(\d+)$", "$1")]
    [InlineData(@"^TRD-0*(\d+)$", "${1}0")]
    [InlineData(@"^TRD-0*(\d+)$", "$0")]
    [InlineData(@"^TRD-0*(?<id>\d+)$", "${id}")]
    [InlineData(@"^TRD-0*(?<id>\d+)$", "$1")]
    [InlineData(@"^(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)$", "$10")]
    [InlineData(@"^TRD$", "$$2")]
    [InlineData(@"^TRD$", "$$${0}")]
    [InlineData(@"^TRD$", "$&$`$'$+$_")]
    [InlineData(@"^TRD$", "cost $ 5")]
    [InlineData(@"^TRD$", "")]
    public void A_reference_to_an_existing_group_or_a_literal_dollar_compiles(string pattern, string replacement) =>
        Assert.IsType<RegexTextRule>(Build(pattern, replacement));

    private static INormalizationRule Build(string pattern, string replacement)
    {
        var call = new RuleCall(
            "text.regex",
            null,
            ImmutableDictionary.CreateRange(StringComparer.Ordinal, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["from"] = "TradeId",
                ["to"] = "Key",
                ["pattern"] = pattern,
                ["replacement"] = replacement,
            }),
            ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);
        var schema = new FieldSchema([new FieldDeclaration("Key", FieldType.Symbol, FieldRole.Key, 0)]);
        return Registry.TryGet("text.regex", out var registration)
            ? registration.CreateWithSettings(call, schema, RuleSettings.Invariant(MidpointMode.ToEven))
            : throw new InvalidOperationException("text.regex is not registered.");
    }
}
