namespace RecDiffy.Tests;

// #361: The floating-point uses the gates permit. A key names exactly one line (with a count), member or method body; a stale or
// duplicate entry fails the gate, so the lists cannot rot. Add an entry only with a reason that is true of the code it excuses.
internal static class FloatingPointAllowlist
{
    private const string FromDoubleKeptForObsoleteRules =
        "Kept only for Numbers.CanBeNumber and DecimalParsing, which serve [Obsolete] rules that no registered rule name reaches; no active rule may call it (#361 audit).";

    private const string CultureDateObsolete = "Used only by the [Obsolete] CultureDateRule (replaced by DeclaredDateRule, then PartyFormatDateRule); ";

    private const string DeclaredDateObsolete = "Used only by the [Obsolete] DeclaredDateRule (replaced by PartyFormatDateRule, #275); ";

    private const string ReadsOaSerial = "reads an OLE Automation date serial supplied as double and range-checks it before DateTime.FromOADate.";

    private const string ActiveOaSerial = "date.parse's PartyFormatDateRule reads an OLE Automation date serial supplied as double and range-checks it before DateTime.FromOADate.";

    // Source lines that use double or float on purpose; the count is how many lines of the file have that exact text.
    internal static readonly IReadOnlyList<FloatingPointLineAllow> Lines =
    [
        new("RecDiffy/Intake/RecordSource/DataReaderSource.Body.cs", "double or float or Half => value,", "Intake forwards floating point untouched so decimal.parse can reject it (#158)."),

        new("RecDiffy/Rules/CultureDateParsing.cs", "double serial when TryFromOADate(serial, out var date) => Parsed.Of(date),", CultureDateObsolete + ReadsOaSerial),
        new("RecDiffy/Rules/CultureDateParsing.cs", "private static bool TryFromOADate(double serial, out DateOnly date)", CultureDateObsolete + ReadsOaSerial),
        new("RecDiffy/Rules/CultureDateParsing.cs", "if (double.IsFinite(serial) is false || serial <= -657435.0d || serial >= 2958466.0d)", CultureDateObsolete + "bounds the double serial to the OLE Automation range."),
        new("RecDiffy/Rules/DateParsing.cs", "double serial when TryFromOADate(serial, out var date) => Parsed.Of(date),", "Used only by the [Obsolete] DateParseRule (replaced by CultureDateRule, #158); " + ReadsOaSerial),
        new("RecDiffy/Rules/DateParsing.cs", "private static bool TryFromOADate(double serial, out DateOnly date)", "Used only by the [Obsolete] DateParseRule (replaced by CultureDateRule, #158); " + ReadsOaSerial),
        new("RecDiffy/Rules/DateParsing.cs", "if (double.IsFinite(serial) is false || serial <= -657435.0d || serial >= 2958466.0d)", "Used only by the [Obsolete] DateParseRule (replaced by CultureDateRule, #158); bounds the double serial to the OLE Automation range."),
        new("RecDiffy/Rules/DeclaredDateParsing.cs", "double serial when FromOADate(serial) is { } date => Parsed.Of(date),", DeclaredDateObsolete + ReadsOaSerial),
        new("RecDiffy/Rules/DeclaredDateParsing.cs", "private static DateOnly? FromOADate(double serial) =>", DeclaredDateObsolete + ReadsOaSerial),
        new("RecDiffy/Rules/DeclaredDateParsing.cs", "double.IsFinite(serial) && serial is > -657435.0d and < 2958466.0d", DeclaredDateObsolete + "bounds the double serial to the OLE Automation range."),
        new("RecDiffy/Rules/PartyFormatDateRule.cs", "double serial when FromOADate(serial) is { } date => WriteDate(row, date, rawText),", ActiveOaSerial),
        new("RecDiffy/Rules/PartyFormatDateRule.cs", "private static DateOnly? FromOADate(double serial) =>", ActiveOaSerial),
        new("RecDiffy/Rules/PartyFormatDateRule.cs", "double.IsFinite(serial) && serial is > -657435.0d and < 2958466.0d", "date.parse's PartyFormatDateRule bounds the double serial to the OLE Automation range."),

        new("RecDiffy/Rules/CultureDecimalRule.cs", "double or float or Half => Reject(row, \"FloatValue\", rawText),", "decimal.parse matches floating types only to reject them; it never converts (#158)."),

        new("RecDiffy/Rules/NumberRead.cs", "double value when Rounding.TryFromDouble(value, out var number) => NumberRead.Of(number),", "Numbers.CanBeNumber is called only by the [Obsolete] ParseDecimalRule and AbsoluteQuantityRule, which no registered rule name reaches; no active rule may call it (#361 audit)."),
        new("RecDiffy/Rules/NumberRead.cs", "float value when Rounding.TryFromDouble(value, out var number) => NumberRead.Of(number),", "Same as the double line: it widens a float to double for the [Obsolete] rules only, so 0.1f reads as 0.10000000149011612m (#361 audit)."),
        new("RecDiffy/Rules/DecimalParsing.cs", "double value when Rounding.TryFromDouble(value, out var number) => Parsed.Of(number),", "DecimalParsing is [Obsolete] and called only by the [Obsolete] DecimalParseRule, which no registered rule name reaches (#361 audit)."),
        new("RecDiffy/Rules/DecimalParsing.cs", "float value when Rounding.TryFromDouble(value, out var number) => Parsed.Of(number),", "DecimalParsing is [Obsolete] and called only by the [Obsolete] DecimalParseRule, which no registered rule name reaches (#361 audit)."),
        new("RecDiffy/Schema/Rounding.cs", "public static bool TryFromDouble(double value, out decimal number)", FromDoubleKeptForObsoleteRules),
        new("RecDiffy/Schema/Rounding.cs", "return double.IsFinite(value)", "Body of Rounding.TryFromDouble. " + FromDoubleKeptForObsoleteRules),

        new("RecDiffy.RulePacks.Trading/CultureQuantityRule.cs", "double or float or Half => Unread(row, earlier, \"FloatValue\", rawText),", "The [Obsolete] CultureQuantityRule (replaced by ScaledQuantityRule, #170) matches floating types only to leave them unread; it never converts."),
        new("RecDiffy.RulePacks.Trading/ScaledQuantityRule.cs", "double or float or Half => Unread(row, earlier, \"FloatValue\", rawText),", "quantity.absolute matches floating types only to leave them unread; it never converts."),
        new("RecDiffy.RulePacks.Trading/CultureSideResolveRule.cs", "row.Raw.TryGetValue(column, out var raw) && raw is not (double or float or Half)", "side.resolve refuses to read a floating quantity so the sign is never derived from one."),
    ];

    // Fields, methods and constructors whose declared type holds double, float or Half, keyed by type, name and parameter types.
    internal static readonly IReadOnlyList<FloatingPointMemberAllow> Members =
    [
        new("RecDiffy.Rounding.TryFromDouble(System.Double, System.Decimal&)", FromDoubleKeptForObsoleteRules),
        new("RecDiffy.Rules.DateParsing.TryFromOADate(System.Double, System.DateOnly&)", "Used only by the [Obsolete] DateParseRule; its parameter is the OLE Automation serial, a double."),
        new("RecDiffy.Rules.CultureDateParsing.TryFromOADate(System.Double, System.DateOnly&)", CultureDateObsolete + "its parameter is the OLE Automation serial, a double."),
        new("RecDiffy.Rules.DeclaredDateParsing.FromOADate(System.Double)", DeclaredDateObsolete + "its parameter is the OLE Automation serial, a double."),
        new("RecDiffy.Rules.PartyFormatDateRule.FromOADate(System.Double)", "date.parse's PartyFormatDateRule; its parameter is the OLE Automation serial, a double."),
    ];

    // Method bodies whose IL uses floating point, keyed by the full method.
    internal static readonly IReadOnlyList<FloatingPointIlAllow> IlMethods =
    [
        new("RecDiffy.DataReaderSource.CellOf(System.Object)", "Type-tests double, float and Half to pass them through unchanged so decimal.parse can reject them (#158); nothing converts."),
        new("RecDiffy.Rules.CultureDecimalRule.Parse(RecDiffy.Rules.RowContext)", "decimal.parse type-tests double, float and Half only to reject them."),
        new("RecDiffy.RulePacks.Trading.ScaledQuantityRule.Absolute(RecDiffy.Rules.RowContext)", "quantity.absolute type-tests double, float and Half only to leave them unread."),
        new("RecDiffy.RulePacks.Trading.CultureQuantityRule.Absolute(RecDiffy.Rules.RowContext)", "The [Obsolete] CultureQuantityRule type-tests double, float and Half only to leave them unread."),
        new("RecDiffy.RulePacks.Trading.CultureSideResolution.Raw(RecDiffy.Rules.RowContext, System.String, System.Globalization.NumberFormatInfo)", "side.resolve type-tests double, float and Half to refuse a floating quantity."),

        new("RecDiffy.Rules.PartyFormatDateRule.Apply(RecDiffy.Rules.RowContext&)", "date.parse reads a double as an OLE Automation date serial; the result is a date, never money or quantity."),
        new("RecDiffy.Rules.PartyFormatDateRule.FromOADate(System.Double)", "date.parse's OLE Automation serial reader: range-checks the double, then DateTime.FromOADate; the result is a date."),

        new("RecDiffy.Rules.CultureDateParsing.Read(System.Object, System.Collections.Generic.IReadOnlyList`1[System.String], System.Globalization.CultureInfo)", CultureDateObsolete + "reads a double as an OLE Automation date serial."),
        new("RecDiffy.Rules.CultureDateParsing.TryFromOADate(System.Double, System.DateOnly&)", CultureDateObsolete + "range-checks the serial, then DateTime.FromOADate."),
        new("RecDiffy.Rules.DeclaredDateParsing.Read(System.Object, System.Collections.Generic.IReadOnlyList`1[System.String], System.Globalization.CultureInfo)", DeclaredDateObsolete + "reads a double as an OLE Automation date serial."),
        new("RecDiffy.Rules.DeclaredDateParsing.FromOADate(System.Double)", DeclaredDateObsolete + "range-checks the serial, then DateTime.FromOADate."),
        new("RecDiffy.Rules.DateParsing.Read(System.Object, System.Collections.Generic.IReadOnlyList`1[System.String])", "Used only by the [Obsolete] DateParseRule (replaced by CultureDateRule, #158); reads a double as an OLE Automation date serial."),
        new("RecDiffy.Rules.DateParsing.TryFromOADate(System.Double, System.DateOnly&)", "Used only by the [Obsolete] DateParseRule (replaced by CultureDateRule, #158); range-checks the serial, then DateTime.FromOADate."),

        new("RecDiffy.Rounding.TryFromDouble(System.Double, System.Decimal&)", FromDoubleKeptForObsoleteRules),
        new("RecDiffy.Rules.Numbers.CanBeNumber(System.Object)", "Calls Rounding.TryFromDouble and widens a float to double; called only by the [Obsolete] ParseDecimalRule and AbsoluteQuantityRule, which no registered rule name reaches; no active rule may call it (#361 audit)."),
        new("RecDiffy.Rules.DecimalParsing.Read(System.Object)", "DecimalParsing is [Obsolete] and called only by the [Obsolete] DecimalParseRule; it calls Rounding.TryFromDouble and widens a float to double (#361 audit)."),
    ];
}

internal sealed record FloatingPointLineAllow(string RelativePath, string LineText, string Reason, int Count = 1);

internal sealed record FloatingPointMemberAllow(string Key, string Reason);

internal sealed record FloatingPointIlAllow(string Method, string Reason);
