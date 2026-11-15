using System.Text;
using System.Text.RegularExpressions;

namespace RecDiffy.Tests;

// #361: Shared line scanner for the floating-point gate. It masks comments and string/char literals, then flags the
// floating-point tokens that money and quantity paths must never use. Exposed to the gate test and its negative tests.
internal static partial class FloatingPointLineScanner
{
    private static readonly Regex WholeWord = new(
        @"\b(?:double|float|Double|Single|Half)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    private static readonly Regex ConvertCall = new(
        @"\bConvert\.To(?:Double|Single)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    private static readonly Regex SuffixLiteral = new(
        @"\b\d+(?:\.\d+)?[dDfF]\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    // A numeric literal with a decimal point but no decimal (m/M) suffix reads as a double.
    private static readonly Regex DecimalPointLiteral = new(
        @"\b\d+\.\d+(?![\w.m])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    // True when the line, stripped of comments and string/char literals, uses a floating-point type or literal.
    public static bool Matches(string line)
    {
        var code = MaskCommentsAndStrings(line);
        return WholeWord.IsMatch(code)
            || ConvertCall.IsMatch(code)
            || SuffixLiteral.IsMatch(code)
            || DecimalPointLiteral.IsMatch(code);
    }

    // Masks the characters of comments and string/char literals with spaces, preserving newlines so line numbers and
    // column positions survive. A decimal literal like 1.5m stays visible only when it is not a float form.
    internal static string MaskCommentsAndStrings(string text)
    {
        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                {
                    result.Append(' ');
                    i++;
                }
                continue;
            }

            if (c == '/' && next == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    result.Append(text[i] is '\n' or '\r' ? text[i] : ' ');
                    i++;
                }
                result.Append(' ');
                result.Append(' ');
                i = Math.Min(i + 2, text.Length);
                continue;
            }

            if (c == '"')
            {
                if (next == '"' && i + 2 < text.Length && text[i + 2] == '"')
                {
                    i = MaskRawString(text, i, result);
                    continue;
                }
                i = MaskQuoted(text, i, '"', result);
                continue;
            }

            if (c == '\'')
            {
                i = MaskQuoted(text, i, '\'', result);
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    private static int MaskRawString(string text, int start, StringBuilder result)
    {
        int i = start + 3;
        result.Append(' ', 3);
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                result.Append(' ');
                return i + 1;
            }

            result.Append(text[i] is '\n' or '\r' ? text[i] : ' ');
            i++;
        }

        return i;
    }

    private static int MaskQuoted(string text, int start, char quote, StringBuilder result)
    {
        int i = start + 1;
        result.Append(' ');
        bool escaped = false;
        while (i < text.Length)
        {
            char c = text[i];
            if (escaped)
            {
                result.Append(' ');
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\')
            {
                result.Append(' ');
                escaped = true;
                i++;
                continue;
            }

            if (c == quote)
            {
                result.Append(' ');
                return i + 1;
            }

            result.Append(' ');
            i++;
        }

        return i;
    }
}