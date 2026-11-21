using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// Adversarial tests for text.regex (#547): each asserts what the semantics promise, not what the code happens to do.
public class RegexTextRuleAdversarialTests
{
    private static readonly RuleRegistry Registry = RuleRegistry.CreateCore();

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "regex-adversarial",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "TradeId" }, "right": { "rowKey": "TradeId" } },
          "fields": [
            { "name": "Trade", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "text.regex", "from": "TradeId", "to": "Trade", "pattern": "^TRD-0*(\\d+)$", "replacement": "$1", "passthrough": false } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "text.regex", "from": "TradeId", "to": "Trade", "pattern": "^trd(\\d+)$", "replacement": "$1", "ignoreCase": true, "passthrough": false } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Trade" ] } ] } ]
        }
        """;

    private const string LeftRule = "{ \"rule\": \"text.regex\", \"from\": \"TradeId\", \"to\": \"Trade\", \"pattern\": \"^TRD-0*(\\\\d+)$\", \"replacement\": \"$1\", \"passthrough\": false }";

    private static readonly ImmutableDictionary<string, string> Base = Args(
        ("from", "Src"), ("to", "Key"), ("pattern", @"^TRD-0*(\d+)$"), ("replacement", "$1"));

    // ---------- Edge inputs ----------

    [Fact]
    public void A_pattern_that_matches_the_empty_string_replaces_at_every_position()
    {
        var rule = Base.SetItem("pattern", "x*").SetItem("replacement", "-");

        Assert.Equal("-a-b-c-", Apply(rule, ("Src", "abc")).Display("Key"));
    }

    [Fact]
    public void An_empty_pattern_with_an_empty_replacement_leaves_the_text_unchanged()
    {
        var rule = Base.SetItem("pattern", "").SetItem("replacement", "");

        Assert.Equal("abc", Apply(rule, ("Src", "abc")).Display("Key"));
    }

    [Theory]
    [InlineData("$$", "a$b")]
    [InlineData("$0", "ab")]
    [InlineData("[$0]", "[ab]")]
    [InlineData("$9", "$9")]
    [InlineData("${nope}", "${nope}")]
    [InlineData("${1}x", "ax")]
    public void Replacement_substitutions_follow_dotnet_replace(string replacement, string expected)
    {
        var rule = Base.SetItem("pattern", "^(a)b$").SetItem("replacement", replacement == "$$" ? "$1$$b" : replacement);

        Assert.Equal(expected, Apply(rule, ("Src", "ab")).Display("Key"));
    }

    [Fact]
    public void Named_groups_substitute_by_name()
    {
        var rule = Base.SetItem("pattern", @"^TRD-0*(?<n>\d+)$").SetItem("replacement", "N${n}");

        Assert.Equal("N42", Apply(rule, ("Src", "TRD-0042")).Display("Key"));
    }

    [Fact]
    public void A_partial_match_replaces_only_the_matched_part()
    {
        var rule = Base.SetItem("pattern", "-").SetItem("replacement", "");

        Assert.Equal("ab12", Apply(rule, ("Src", "a-b-12")).Display("Key"));
    }

    [Fact]
    public void Every_match_is_replaced_not_only_the_first()
    {
        var rule = Base.SetItem("pattern", "0").SetItem("replacement", "o");

        Assert.Equal("o1o2o", Apply(rule, ("Src", "01020")).Display("Key"));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_rewrite_that_leaves_only_whitespace_is_a_silent_null(string replacement)
    {
        var rule = Base.SetItem("pattern", "^N/A$").SetItem("replacement", replacement).SetItem("passthrough", "false");

        var row = Apply(rule, ("Src", "N/A"));

        Assert.True(row.Values["Key"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void A_rewrite_that_leaves_nothing_is_null_for_a_text_target_too()
    {
        var rule = Base.SetItem("to", "Note").SetItem("pattern", "^N/A$").SetItem("replacement", "");

        var row = Apply(rule, ("Src", "N/A"));

        Assert.True(row.Values["Note"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void The_rewritten_text_is_trimmed()
    {
        var rule = Base.SetItem("pattern", "^k(.*)$").SetItem("replacement", "  $1  ");

        Assert.Equal("v", Apply(rule, ("Src", "kv")).Display("Key"));
    }

    [Fact]
    public void Case_is_applied_after_the_rewrite_on_a_replacement_literal()
    {
        var rule = Base.SetItem("pattern", "^(.+)$").SetItem("replacement", "id-$1").SetItem("case", "upper");

        Assert.Equal("ID-ABC", Apply(rule, ("Src", "abc")).Display("Key"));
    }

    [Fact]
    public void Passthrough_false_with_case_still_reports_the_unfolded_raw_text()
    {
        var rule = Base.SetItem("passthrough", "false").SetItem("case", "upper");

        var diagnostic = Assert.Single(Apply(rule, ("Src", "abc")).Diagnostics);

        Assert.Equal("abc", diagnostic.RawText);
    }

    [Fact]
    public void An_embedded_newline_does_not_match_an_anchored_single_line_pattern()
    {
        var rule = Base.SetItem("pattern", "^B$").SetItem("replacement", "x");

        Assert.Equal("A\nB", Apply(rule, ("Src", "A\nB")).Display("Key"));
    }

    [Fact]
    public void Dot_does_not_cross_a_newline()
    {
        var rule = Base.SetItem("pattern", "^A.B$").SetItem("replacement", "x");

        Assert.Equal("A\nB", Apply(rule, ("Src", "A\nB")).Display("Key"));
    }

    [Fact]
    public void A_trailing_newline_is_trimmed_before_matching()
    {
        Assert.Equal("5", Apply(Base, ("Src", "TRD-5\n")).Display("Key"));
    }

    [Fact]
    public void A_surrogate_pair_literal_in_the_pattern_matches()
    {
        var rule = Base.SetItem("pattern", "^😀(\\d)$").SetItem("replacement", "$1");

        Assert.Equal("1", Apply(rule, ("Src", "😀1")).Display("Key"));
    }

    [Fact]
    public void A_surrogate_pair_in_a_passed_through_cell_is_kept_intact()
    {
        Assert.Equal("😀x", Apply(Base, ("Src", "😀x")).Display("Key"));
    }

    [Fact]
    public void A_very_long_cell_is_processed()
    {
        var cell = new string('1', 200_000);
        var rule = Base.SetItem("pattern", @"^(\d+)$");

        Assert.Equal(cell, Apply(rule, ("Src", cell)).Display("Key"));
    }

    [Fact]
    public void A_pathological_pattern_on_a_long_cell_finishes_without_a_timeout()
    {
        var cell = new string('a', 50_000) + "!";
        var rule = Base.SetItem("pattern", "^(a+)+$").SetItem("replacement", "x");

        Assert.Equal(cell, Apply(rule, ("Src", cell)).Display("Key"));
    }

    [Fact]
    public void An_integer_cell_is_read_as_its_digits()
    {
        var rule = Base.SetItem("pattern", @"^(\d+)$");

        Assert.Equal("42", Apply(rule, ("Src", 42)).Display("Key"));
    }

    [Fact]
    public void A_double_cell_is_read_invariant_under_a_comma_culture()
    {
        var rule = Base.SetItem("pattern", @"^(\d+)\.(\d+)$").SetItem("replacement", "$1_$2");

        var text = WithCulture("de-DE", () => Apply(rule, ("Src", 1.5d)).Display("Key"));

        Assert.Equal("1_5", text);
    }

    [Fact]
    public void A_decimal_cell_is_read_invariant_under_a_comma_culture()
    {
        var rule = Base.SetItem("pattern", @"^(\d+)\.(\d+)$").SetItem("replacement", "$1_$2");

        var text = WithCulture("de-DE", () => Apply(rule, ("Src", 12.5m)).Display("Key"));

        Assert.Equal("12_5", text);
    }

    [Fact]
    public void A_date_cell_is_read_invariant_regardless_of_the_current_culture()
    {
        var rule = Base.SetItem("pattern", "^(.*)$");
        var date = new DateTime(2024, 3, 9, 14, 5, 6, DateTimeKind.Unspecified);

        var de = WithCulture("de-DE", () => Apply(rule, ("Src", date)).Display("Key"));
        var us = WithCulture("en-US", () => Apply(rule, ("Src", date)).Display("Key"));

        Assert.Equal(us, de);
    }

    [Fact]
    public void A_DBNull_cell_is_a_silent_null()
    {
        var row = Apply(Base.SetItem("passthrough", "false"), ("Src", DBNull.Value));

        Assert.True(row.Values["Key"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void A_boolean_cell_is_read_as_text_and_passed_through()
    {
        Assert.Equal("True", Apply(Base, ("Src", true)).Display("Key"));
    }

    // ---------- Culture ----------

    [Fact]
    public void IgnoreCase_matches_the_dotted_and_dotless_i_the_same_under_every_current_culture()
    {
        var rule = Base.SetItem("pattern", "^id-(\\d+)$").SetItem("ignoreCase", "true");

        var tr = WithCulture("tr-TR", () => Apply(rule, ("Src", "ID-5")).Display("Key"));
        var en = WithCulture("en-US", () => Apply(rule, ("Src", "ID-5")).Display("Key"));

        Assert.Equal("5", tr);
        Assert.Equal("5", en);
    }

    [Fact]
    public void IgnoreCase_under_tr_TR_does_not_treat_the_dotted_capital_I_as_a_plain_i()
    {
        var rule = Base.SetItem("pattern", "^id$").SetItem("replacement", "hit").SetItem("ignoreCase", "true");

        var tr = WithCulture("tr-TR", () => Apply(rule, ("Src", "ID")).Display("Key"));
        var en = WithCulture("en-US", () => Apply(rule, ("Src", "ID")).Display("Key"));

        Assert.Equal(en, tr);
    }

    [Theory]
    [InlineData("upper", "TITLE")]
    [InlineData("lower", "title")]
    public void Case_folding_is_invariant_under_tr_TR(string textCase, string expected)
    {
        var rule = Base.SetItem("case", textCase);
        var cell = textCase == "upper" ? "title" : "TITLE";

        var text = WithCulture("tr-TR", () => Apply(rule, ("Src", cell)).Display("Key"));

        Assert.Equal(expected, text);
    }

    // ---------- Compile ----------

    [Theory]
    [InlineData("(?>a)b")]
    [InlineData("(?<=a)b")]
    [InlineData("a(?=b)")]
    [InlineData("a(?!b)")]
    [InlineData(@"(a)\1")]
    [InlineData("(?(a)b|c)")]
    [InlineData(@"\Gabc")]
    [InlineData("(")]
    [InlineData("[z-a]")]
    [InlineData("*a")]
    [InlineData("(?r)abc")]
    public void An_unsupported_or_invalid_pattern_fails_compile_with_InvalidParameter(string pattern)
    {
        var failure = Assert.Throws<ProfileCompileException>(() => Build(Base.SetItem("pattern", pattern)));

        Assert.Contains(failure.Errors, error => error.Code == "InvalidParameter");
    }

    [Theory]
    [InlineData("pattern")]
    [InlineData("replacement")]
    [InlineData("from")]
    [InlineData("to")]
    public void A_required_string_parameter_given_as_a_JSON_array_is_a_profile_error_not_a_crash(string name)
    {
        var json = Profile.Replace($"\"{name}\": \"" + ValueOf(name) + "\"", $"\"{name}\": [\"x\"]", StringComparison.Ordinal);

        Assert.NotEqual(Profile, json, StringComparer.Ordinal);
        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));
        Assert.Contains(new ProfileError("InvalidParameter", $"text.regex {name} must be a string, not a list or an object."), failure.Errors);
    }

    [Theory]
    [InlineData("pattern")]
    [InlineData("replacement")]
    public void A_required_string_parameter_given_as_a_JSON_object_is_a_profile_error(string name)
    {
        var json = Profile.Replace($"\"{name}\": \"" + ValueOf(name) + "\"", $"\"{name}\": {{ \"a\": \"b\" }}", StringComparison.Ordinal);

        Assert.NotEqual(Profile, json, StringComparer.Ordinal);
        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));
        Assert.Contains(failure.Errors, error => error.Code is "InvalidParameter" or "MissingParameter");
    }

    [Fact]
    public void A_JSON_null_pattern_is_a_missing_parameter()
    {
        var json = Profile.Replace("\"pattern\": \"^TRD-0*(\\\\d+)$\"", "\"pattern\": null", StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.Contains(new ProfileError("MissingParameter", "left.text.regex is missing 'pattern'."), failure.Errors);
    }

    [Theory]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false", "\"replacement\": \"$1\", \"passthrough\": 1")]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false", "\"replacement\": \"$1\", \"passthrough\": \"False\"")]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false", "\"replacement\": \"$1\", \"passthrough\": null, \"ignoreCase\": 0")]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false", "\"replacement\": \"$1\", \"passthrough\": false, \"case\": \"UPPER\"")]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false", "\"replacement\": \"$1\", \"passthrough\": false, \"case\": true")]
    public void A_flag_or_case_that_is_not_exactly_a_legal_value_fails_compile(string original, string replacement)
    {
        var json = Profile.Replace(original, replacement, StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.Contains(failure.Errors, error => error.Code == "InvalidParameter");
    }

    [Fact]
    public void A_JSON_boolean_ignoreCase_and_a_string_true_mean_the_same()
    {
        var asString = Profile.Replace("\"ignoreCase\": true", "\"ignoreCase\": \"true\"", StringComparison.Ordinal);

        var result = Run(Compile(asString), [("TRD-1", "1")], [("TRD1", "1")]);

        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    [Fact]
    public void A_target_that_is_not_declared_fails_compile_naming_the_field()
    {
        var json = Profile.Replace("\"to\": \"Trade\", \"pattern\": \"^TRD", "\"to\": \"Nowhere\", \"pattern\": \"^TRD", StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.Contains(failure.Errors, error => error.Code == "UnknownField");
    }

    [Theory]
    [InlineData("Identifier")]
    [InlineData("Decimal")]
    [InlineData("Date")]
    public void A_target_of_any_type_but_symbol_or_text_fails_compile_with_InvalidParameter(string type)
    {
        var json = Profile.Replace("{ \"name\": \"Trade\", \"type\": \"Symbol\"", $"{{ \"name\": \"Trade\", \"type\": \"{type}\"", StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.Contains(failure.Errors, error => error.Code == "InvalidParameter" && error.Message.StartsWith("text.regex ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_text_target_used_as_a_match_key_is_rejected_as_TextKey()
    {
        var json = Profile.Replace("{ \"name\": \"Trade\", \"type\": \"Symbol\", \"role\": \"Key\" }", "{ \"name\": \"Trade\", \"type\": \"Text\", \"role\": \"Text\" }", StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.Contains(failure.Errors, error => error.Code == "TextKey");
    }

    [Fact]
    public void Two_text_regex_rules_writing_one_field_are_an_AmbiguousWrite()
    {
        var json = Profile.Replace(LeftRule, LeftRule + ", " + LeftRule, StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.Contains(failure.Errors, error => error.Code == "AmbiguousWrite");
    }

    [Fact]
    public void Compile_is_deterministic_and_every_argument_changes_the_hash()
    {
        var first = Compile(Profile).Hash;

        Assert.Equal(first, Compile(Profile).Hash, StringComparer.Ordinal);
        Assert.NotEqual(first, Compile(Profile.Replace("\"ignoreCase\": true, \"passthrough\": false", "\"ignoreCase\": true, \"passthrough\": true", StringComparison.Ordinal)).Hash, StringComparer.Ordinal);
        Assert.NotEqual(first, Compile(Profile.Replace("\"ignoreCase\": true", "\"ignoreCase\": false", StringComparison.Ordinal)).Hash, StringComparer.Ordinal);
        Assert.NotEqual(first, Compile(Profile.Replace("\"ignoreCase\": true", "\"ignoreCase\": true, \"case\": \"upper\"", StringComparison.Ordinal)).Hash, StringComparer.Ordinal);
        Assert.NotEqual(first, Compile(Profile.Replace("\"replacement\": \"$1\", \"ignoreCase\"", "\"replacement\": \"${1}\", \"ignoreCase\"", StringComparison.Ordinal)).Hash, StringComparer.Ordinal);
    }

    // ---------- Engine interplay ----------

    [Fact]
    public void Keys_that_rewrite_to_blank_on_both_sides_never_match_each_other()
    {
        var json = Profile
            .Replace("\"replacement\": \"$1\", \"passthrough\": false }", "\"replacement\": \"\", \"passthrough\": false }", StringComparison.Ordinal)
            .Replace("\"replacement\": \"$1\", \"ignoreCase\"", "\"replacement\": \"\", \"ignoreCase\"", StringComparison.Ordinal);

        var result = Run(Compile(json), [("TRD-1", "1")], [("trd1", "1")]);

        Assert.DoesNotContain(result.Entries, entry => entry.Alignment == Alignment.Matched);
    }

    [Fact]
    public void A_blank_trade_id_on_both_sides_does_not_match()
    {
        var result = Run(Compile(Profile.Replace("\"passthrough\": false", "\"passthrough\": true", StringComparison.Ordinal)), [("", "1")], [(" ", "1")]);

        Assert.DoesNotContain(result.Entries, entry => entry.Alignment == Alignment.Matched);
    }

    [Fact]
    public void The_result_does_not_depend_on_row_order()
    {
        var left = new[] { ("TRD-000001", "10"), ("TRD-000002", "20"), ("TRD-000003", "30"), ("junk", "1") };
        var right = new[] { ("trd1", "10"), ("TRD2", "21"), ("Trd4", "40"), ("zzz", "2") };

        var forward = Run(Compile(Profile), left, right);
        var backward = Run(Compile(Profile), [.. left.Reverse()], [.. right.Reverse()]);

        Assert.Equal(Summary(forward), Summary(backward));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void The_same_profile_gives_the_same_result_under_any_current_culture(string culture)
    {
        var left = new[] { ("TRD-000001", "10"), ("TRD-000002", "20"), ("TRD-000009", "9") };
        var right = new[] { ("TRD1", "10"), ("trd2", "20"), ("TRD9", "9"), ("ID-7", "1") };
        var baseline = WithCulture("en-US", () => Summary(Run(Compile(Profile), left, right)));

        var other = WithCulture(culture, () => Summary(Run(Compile(Profile), left, right)));

        Assert.Equal(baseline, other);
    }

    [Fact]
    public void A_when_clause_lets_two_text_regex_rules_write_one_field_by_row_kind()
    {
        var json = Profile
            .Replace("{ \"name\": \"Quantity\"", "{ \"name\": \"Kind\", \"type\": \"Text\", \"role\": \"Text\" }, { \"name\": \"Quantity\"", StringComparison.Ordinal)
            .Replace(LeftRule, LeftRule.Replace("}", ", \"when\": { \"field\": \"Kind\", \"equals\": \"A\" } }", StringComparison.Ordinal) + ", " + LeftRule.Replace("}", ", \"when\": { \"field\": \"Kind\", \"equals\": \"B\" } }", StringComparison.Ordinal), StringComparison.Ordinal);

        var outcome = Record.Exception(() => Compile(json));

        Assert.True(outcome is null || (outcome is ProfileCompileException failure && failure.Errors.All(error => error.Code != "AmbiguousWrite")));
    }

    [Fact]
    public void Unanchored_unicode_digits_in_a_key_do_not_collide_with_ascii_digits_under_an_ascii_pattern()
    {
        var rule = Base.SetItem("pattern", @"^TRD-0*([0-9]+)$").SetItem("passthrough", "false");

        var ascii = Apply(rule, ("Src", "TRD-12"));
        var arabic = Apply(rule, ("Src", "TRD-١٢"));

        Assert.Equal("12", ascii.Display("Key"));
        Assert.True(arabic.Values["Key"].IsNull);
        Assert.Equal("PatternNoMatch", Assert.Single(arabic.Diagnostics).Code);
    }

    // ---------- Helpers ----------

    private static string ValueOf(string name) => name switch
    {
        "pattern" => "^TRD-0*(\\\\d+)$",
        "replacement" => "$1",
        "from" => "TradeId",
        _ => "Trade",
    };

    private static string Summary(IReconciliationResult result) =>
        string.Join(
            "|",
            result.Rows
                .Select(row => $"{row.Row.RowKey}:{row.Disposition}:{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal))}")
                .Order(StringComparer.Ordinal))
        + "#"
        + string.Join("|", result.Entries.Select(entry => $"{entry.Alignment}:{entry.Outcome}").Order(StringComparer.Ordinal));

    private static T WithCulture<T>(string name, Func<T> action)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static ImmutableDictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static INormalizationRule Build(IReadOnlyDictionary<string, string> strings)
    {
        var call = new RuleCall(
            "text.regex",
            null,
            strings,
            ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);
        var schema = new FieldSchema(
        [
            new FieldDeclaration("Key", FieldType.Symbol, FieldRole.Key, 0),
            new FieldDeclaration("Note", FieldType.Text, FieldRole.Text, 0),
        ]);
        return Registry.TryGet("text.regex", out var registration)
            ? registration.CreateWithSettings(call, schema, RuleSettings.Invariant(MidpointMode.ToEven))
            : throw new InvalidOperationException("text.regex is not registered.");
    }

    private static RowContext Apply(IReadOnlyDictionary<string, string> strings, params (string Column, object? Value)[] cells)
    {
        var next = RowContext.Create("L1", Side.Left, cells.ToDictionary(cell => cell.Column, cell => cell.Value, StringComparer.Ordinal));
        Build(strings).Apply(ref next);
        return next;
    }

    private static CompiledProfile Compile(string json) =>
        Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore());

    private static IReconciliationResult Run(CompiledProfile profile, (string TradeId, string Qty)[] left, (string TradeId, string Qty)[] right)
    {
        var map = new SourceMap<(string TradeId, string Qty)>().Text("TradeId", row => row.TradeId).Text("Qty", row => row.Qty);
        return Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", left, map),
            RecordSources.FromObjects("right", right, map),
            ReferenceDataSet.Empty);
    }
}
