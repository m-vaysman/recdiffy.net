using System.Xml;

namespace RecDiffy.Excel.Writing;

// XML 1.0 cannot carry most control characters or an unpaired surrogate; Excel also caps a cell at 32,767 characters.
internal static class CellText
{
    public const int MaxLength = 32_767;

    public static string Clean(string text)
    {
        var valid = Enumerable.Range(0, text.Length).All(index => IsXmlCharacter(text, index))
            ? text
            : new string([.. text.Select((character, index) => IsXmlCharacter(text, index) ? character : '\uFFFD')]);
        return valid.Length <= MaxLength ? valid : valid[..CutAt(valid)];
    }

    private static int CutAt(string valid) => char.IsHighSurrogate(valid[MaxLength - 1]) ? MaxLength - 1 : MaxLength;

    private static bool IsXmlCharacter(string text, int index) => text[index] switch
    {
        var high when char.IsHighSurrogate(high) => char.IsSurrogatePair(text, index),
        var low when char.IsLowSurrogate(low) => index > 0 && char.IsSurrogatePair(text, index - 1),
        var character => XmlConvert.IsXmlChar(character),
    };
}
