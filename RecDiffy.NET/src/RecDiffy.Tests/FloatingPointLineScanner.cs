using System.Text;
using System.Text.RegularExpressions;

namespace RecDiffy.Tests;

// #361: Finds floating-point code in C# source text. It masks the whole text once, then scans the code that is left.
// The capitalised type names are matched in type positions only (see TypeNames below); the signature and IL gates catch every real use.
internal static class FloatingPointLineScanner
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    // Capitalised floating-point types; the unqualified forms below skip nameof(...), member access and calls such as rows.Single().
    private const string TypeNames =
        "Double|Single|Half|NFloat|Complex|Vector2|Vector3|Vector4|Vector|Matrix3x2|Matrix4x4|Plane|Quaternion|Vector64|Vector128|Vector256|Vector512";

    private static readonly Regex Keyword = new(@"\b(?:double|float)\b", Options, RegexTimeout);

    // Not after a dot, not inside nameof(...) and not a call: Single x, (Single)x, Single.Parse(s), List<Single>.
    private static readonly Regex UnqualifiedType = new(
        @"(?<![\w.])(?<!nameof\(\s*)(?:" + TypeNames + @")\b(?!\s*(?:<[^;{}]*?>\s*)?\()",
        Options,
        RegexTimeout);

    // A namespace-qualified name is a type: System.Single, System.Numerics.Complex.
    private static readonly Regex QualifiedType = new(
        @"\bSystem\.(?:Numerics\.|Runtime\.InteropServices\.|Runtime\.Intrinsics\.)?(?:" + TypeNames + @")\b",
        Options,
        RegexTimeout);

    // A constructor call is not a method call: new Double().
    private static readonly Regex NewType = new(@"\bnew\s+(?:" + TypeNames + @")\s*\(", Options, RegexTimeout);

    private static readonly Regex ConvertCall = new(@"\bConvert\.To(?:Double|Single)\b", Options, RegexTimeout);

    // Any numeric literal: hex and binary forms first (so 0x1F stays an integer), then digit separators, a leading dot, an exponent
    // and a suffix. A literal may follow ".." (a spread or range) but not a single dot (a member access).
    private static readonly Regex NumberLiteral = new(
        @"(?<!\w)(?<!(?<!\.)\.)(?:0[xX][0-9a-fA-F_]+[uUlL]*|0[bB][01_]+[uUlL]*|(?<body>(?:\d[\d_]*(?:\.\d[\d_]*)?|\.\d[\d_]*)(?<exp>[eE][+-]?\d[\d_]*)?)(?<suffix>[a-zA-Z]*))",
        Options,
        RegexTimeout);

    // True when the text, with comments and string and char literals removed, uses a floating-point type or literal.
    public static bool Matches(string text) => Scan(text).Count > 0;

    // The lines (1-based number, original text) whose code uses a floating-point type or literal.
    public static IReadOnlyList<FloatingPointLine> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var masked = Mask(text).Split('\n');
        var original = text.Split('\n');

        return
        [
            .. masked
                .Select((code, index) => (Code: code, Number: index + 1, Text: original[index].TrimEnd('\r')))
                .Where(line => UsesFloatingPoint(line.Code))
                .Select(line => new FloatingPointLine(line.Number, line.Text)),
        ];
    }

    // Replaces comments and string and char literals with spaces and keeps every newline; code in interpolation holes stays.
    internal static string Mask(string text) => new Masker(text).Run();

    private static bool UsesFloatingPoint(string code) =>
        Keyword.IsMatch(code)
        || ConvertCall.IsMatch(code)
        || UnqualifiedType.IsMatch(code)
        || QualifiedType.IsMatch(code)
        || NewType.IsMatch(code)
        || NumberLiteral.Matches(code).Any(IsFloatingLiteral);

    // A hex or binary literal has no body group, so it is an integer; 1.5m is decimal; d and f suffixes, a point or an exponent make a float.
    private static bool IsFloatingLiteral(Match match) =>
        match.Groups["body"].Success
        && match.Groups["suffix"].Value switch
        {
            "m" or "M" => false,
            "d" or "D" or "f" or "F" => true,
            _ => match.Groups["body"].Value.Contains('.', StringComparison.Ordinal) || match.Groups["exp"].Success,
        };

    private readonly record struct StringStart(int Dollars, bool Verbatim, int PrefixLength, int Quotes);

    // One pass over the text; the mutation is local to this class and the result is a plain string.
    private sealed class Masker(string text)
    {
        private readonly StringBuilder _output = new(text.Length);
        private int _index;

        public string Run()
        {
            Code(inHole: false, braces: 1);
            return _output.ToString();
        }

        private char At(int offset) => _index + offset < text.Length ? text[_index + offset] : '\0';

        private void Keep()
        {
            _output.Append(text[_index]);
            _index++;
        }

        private void Blank()
        {
            _output.Append(text[_index] is '\n' or '\r' ? text[_index] : ' ');
            _index++;
        }

        private void Blank(int count)
        {
            for (var n = 0; n < count && _index < text.Length; n++)
                Blank();
        }

        private int RunLength(char value) => RunLengthFrom(_index, value);

        private int RunLengthFrom(int start, char value)
        {
            var end = start;
            while (end < text.Length && text[end] == value)
                end++;
            return end - start;
        }

        // Scans code. In a hole it returns at the closing brace, or at the format part after a top-level colon.
        private void Code(bool inHole, int braces)
        {
            var depth = 0;
            while (_index < text.Length)
            {
                var c = text[_index];

                switch (c)
                {
                    case '/' when At(1) is '/':
                        LineComment();
                        break;
                    case '/' when At(1) is '*':
                        BlockComment();
                        break;
                    case '\'':
                        CharLiteral();
                        break;
                    case '}' when inHole && depth == 0:
                        return;
                    case ':' when inHole && depth == 0 && At(1) is not ':':
                        FormatSpec(braces);
                        return;
                    case ':' when inHole && At(1) is ':':
                        Keep();
                        Keep();
                        break;
                    default:
                        if (StringStartAt() is { } start)
                        {
                            StringLiteral(start);
                            break;
                        }

                        depth += c switch
                        {
                            '(' or '[' or '{' => 1,
                            ')' or ']' or '}' => -1,
                            _ => 0,
                        };
                        Keep();
                        break;
                }
            }
        }

        private void LineComment()
        {
            while (_index < text.Length && text[_index] is not ('\n' or '\r'))
                Blank();
        }

        private void BlockComment()
        {
            Blank(2);
            while (_index < text.Length && (text[_index] == '*' && At(1) is '/') is false)
                Blank();
            Blank(2);
        }

        private void CharLiteral()
        {
            Blank();
            while (_index < text.Length && text[_index] is not ('\n' or '\r'))
            {
                var closes = text[_index] is '\'';
                var escapes = text[_index] is '\\';
                Blank();
                if (escapes && At(0) is not ('\n' or '\r' or '\0'))
                    Blank();
                if (closes)
                    return;
            }
        }

        // The format part of a hole ends at as many closing braces as the string's dollar signs: one in $"..", two in $$"""..""".
        private void FormatSpec(int braces)
        {
            while (_index < text.Length && RunLength('}') < braces)
                Blank();
        }

        // A string literal starts here when optional $ and @ prefixes are followed by a quote.
        private StringStart? StringStartAt()
        {
            if (text[_index] is not ('$' or '@' or '"'))
                return null;

            var quoteIndex = _index;
            while (quoteIndex < text.Length && text[quoteIndex] is '$' or '@')
                quoteIndex++;

            if (quoteIndex >= text.Length || text[quoteIndex] is not '"')
                return null;

            var prefix = text.AsSpan(_index, quoteIndex - _index);
            return new StringStart(prefix.Count('$'), prefix.Contains('@'), prefix.Length, RunLengthFrom(quoteIndex, '"'));
        }

        private void StringLiteral(StringStart start)
        {
            Blank(start.PrefixLength);

            switch (start)
            {
                case { Verbatim: false, Quotes: >= 3 }:
                    RawString(start.Dollars, start.Quotes);
                    break;
                case { Verbatim: false, Quotes: 2 }:
                    Blank(2);
                    break;
                default:
                    RegularString(start.Dollars > 0, start.Verbatim);
                    break;
            }
        }

        // A regular or verbatim string. A verbatim string has no backslash escapes and doubles its quote instead.
        private void RegularString(bool interpolated, bool verbatim)
        {
            Blank();
            while (_index < text.Length)
            {
                switch (text[_index])
                {
                    case '"' when verbatim && At(1) is '"':
                        Blank(2);
                        break;
                    case '"':
                        Blank();
                        return;
                    case '\\' when verbatim is false:
                        Blank(2);
                        break;
                    case '\n' or '\r' when verbatim is false:
                        return;
                    case '{' when interpolated && At(1) is '{':
                        Blank(2);
                        break;
                    case '}' when interpolated && At(1) is '}':
                        Blank(2);
                        break;
                    case '{' when interpolated:
                        Hole(1);
                        break;
                    default:
                        Blank();
                        break;
                }
            }
        }

        // A raw string ends only at a run of quotes as long as its opening run; n dollar signs make n braces open a hole.
        private void RawString(int dollars, int quotes)
        {
            Blank(quotes);
            while (_index < text.Length)
            {
                switch (text[_index])
                {
                    case '"':
                        var run = RunLength('"');
                        Blank(run);
                        if (run >= quotes)
                            return;
                        break;
                    case '{' when dollars > 0 && RunLength('{') >= dollars:
                        Blank(RunLength('{') - dollars);
                        Hole(dollars);
                        break;
                    default:
                        Blank();
                        break;
                }
            }
        }

        // Blanks the opening braces, scans the hole as code, then blanks the closing braces.
        private void Hole(int braces)
        {
            Blank(braces);
            Code(inHole: true, braces);
            Blank(Math.Min(braces, RunLength('}')));
        }
    }
}

internal sealed record FloatingPointLine(int Number, string Text);
