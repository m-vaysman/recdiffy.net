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
        new("RecDiffy.Rounding.TryFromDouble(System.Double, System.Decimal&) : System.Boolean", FromDoubleKeptForObsoleteRules),
        new("RecDiffy.Rules.DateParsing.TryFromOADate(System.Double, System.DateOnly&) : System.Boolean", "Used only by the [Obsolete] DateParseRule; its parameter is the OLE Automation serial, a double."),
        new("RecDiffy.Rules.CultureDateParsing.TryFromOADate(System.Double, System.DateOnly&) : System.Boolean", CultureDateObsolete + "its parameter is the OLE Automation serial, a double."),
        new("RecDiffy.Rules.DeclaredDateParsing.FromOADate(System.Double) : System.Nullable`1[System.DateOnly]", DeclaredDateObsolete + "its parameter is the OLE Automation serial, a double."),
        new("RecDiffy.Rules.PartyFormatDateRule.FromOADate(System.Double) : System.Nullable`1[System.DateOnly]", "date.parse's PartyFormatDateRule; its parameter is the OLE Automation serial, a double."),
    ];

    // Method bodies whose IL uses floating point. Each entry pins the exact uses and how many of each, so new floating-point code
    // inside a listed method fails the gate, and so does removing a use. A local of a floating-point type is not pinned: it differs
    // between Debug and Release builds, and it is allowed only inside a listed method.
    internal static readonly IReadOnlyList<FloatingPointIlAllow> IlMethods =
    [
        new("RecDiffy.DataReaderSource.CellOf(System.Object)", "Type-tests double, float and Half to pass them through unchanged so decimal.parse can reject them (#158); nothing converts.", TypeTests()),
        new("RecDiffy.Rules.CultureDecimalRule.Parse(RecDiffy.Rules.RowContext)", "decimal.parse type-tests double, float and Half only to reject them.", TypeTests()),
        new("RecDiffy.RulePacks.Trading.ScaledQuantityRule.Absolute(RecDiffy.Rules.RowContext)", "quantity.absolute type-tests double, float and Half only to leave them unread.", TypeTests()),
        new("RecDiffy.RulePacks.Trading.CultureQuantityRule.Absolute(RecDiffy.Rules.RowContext)", "The [Obsolete] CultureQuantityRule type-tests double, float and Half only to leave them unread.", TypeTests()),
        new("RecDiffy.RulePacks.Trading.CultureSideResolution.Raw(RecDiffy.Rules.RowContext, System.String, System.Globalization.NumberFormatInfo)", "side.resolve type-tests double, float and Half to refuse a floating quantity.", TypeTests()),

        new("RecDiffy.Rules.PartyFormatDateRule.Apply(RecDiffy.Rules.RowContext&)", "date.parse reads a double as an OLE Automation date serial; the result is a date, never money or quantity.", SerialCaller("RecDiffy.Rules.PartyFormatDateRule.FromOADate(System.Double)")),
        new("RecDiffy.Rules.PartyFormatDateRule.FromOADate(System.Double)", "date.parse's OLE Automation serial reader: range-checks the double, then DateTime.FromOADate; the result is a date.", SerialReader()),

        new("RecDiffy.Rules.CultureDateParsing.Read(System.Object, System.Collections.Generic.IReadOnlyList`1[System.String], System.Globalization.CultureInfo)", CultureDateObsolete + "reads a double as an OLE Automation date serial.", SerialCaller("RecDiffy.Rules.CultureDateParsing.TryFromOADate(System.Double, System.DateOnly&)")),
        new("RecDiffy.Rules.CultureDateParsing.TryFromOADate(System.Double, System.DateOnly&)", CultureDateObsolete + "range-checks the serial, then DateTime.FromOADate.", SerialReader()),
        new("RecDiffy.Rules.DeclaredDateParsing.Read(System.Object, System.Collections.Generic.IReadOnlyList`1[System.String], System.Globalization.CultureInfo)", DeclaredDateObsolete + "reads a double as an OLE Automation date serial.", SerialCaller("RecDiffy.Rules.DeclaredDateParsing.FromOADate(System.Double)")),
        new("RecDiffy.Rules.DeclaredDateParsing.FromOADate(System.Double)", DeclaredDateObsolete + "range-checks the serial, then DateTime.FromOADate.", SerialReader()),
        new("RecDiffy.Rules.DateParsing.Read(System.Object, System.Collections.Generic.IReadOnlyList`1[System.String])", "Used only by the [Obsolete] DateParseRule (replaced by CultureDateRule, #158); reads a double as an OLE Automation date serial.", SerialCaller("RecDiffy.Rules.DateParsing.TryFromOADate(System.Double, System.DateOnly&)")),
        new("RecDiffy.Rules.DateParsing.TryFromOADate(System.Double, System.DateOnly&)", "Used only by the [Obsolete] DateParseRule (replaced by CultureDateRule, #158); range-checks the serial, then DateTime.FromOADate.", SerialReader()),

        new("RecDiffy.Rounding.TryFromDouble(System.Double, System.Decimal&)", FromDoubleKeptForObsoleteRules, [new("calls System.Double.IsFinite(System.Double)"), new("calls System.Double.ToString(System.String, System.IFormatProvider)")]),
        new("RecDiffy.Rules.Numbers.CanBeNumber(System.Object)", "Reads a boxed double or float into a decimal through Rounding.TryFromDouble, widening a float to double; a conversion door that only the [Obsolete] ParseDecimalRule and AbsoluteQuantityRule call (#361 audit).", BoxedFloatReader()),
        new("RecDiffy.Rules.DecimalParsing.Read(System.Object)", "DecimalParsing is [Obsolete] and called only by the [Obsolete] DecimalParseRule; it reads a boxed double or float into a decimal through Rounding.TryFromDouble, widening a float to double (#361 audit).", BoxedFloatReader()),
    ];

    // Methods that turn a boxed double or float into a decimal and show no floating-point type in their own signature, so a new caller
    // would escape the other gates. Only the listed callers may call or take the address of a door.
    internal static readonly IReadOnlyList<FloatingPointConversionDoor> ConversionDoors =
    [
        new("RecDiffy.Rules.Numbers.CanBeNumber(System.Object)", "Converts a boxed double or float to a decimal (a float is widened to double first); the signature shows no floating-point type.",
        [
            new("RecDiffy.Rules.ParseDecimalRule.Parse(RecDiffy.Rules.RowContext)", "The [Obsolete] ParseDecimalRule (replaced by CultureDecimalRule, #158); no registered rule name builds it."),
            new("RecDiffy.RulePacks.Trading.AbsoluteQuantityRule.Absolute(RecDiffy.Rules.RowContext)", "The [Obsolete] AbsoluteQuantityRule (replaced by CultureQuantityRule, #158); no registered rule name builds it."),
        ]),
        new("RecDiffy.Rules.DecimalParsing.Apply(RecDiffy.Rules.RowContext, System.String, System.String, System.Int32, RecDiffy.MidpointMode, System.String)", "Reads the raw cell through DecimalParsing.Read, which converts a boxed double or float; the signature shows no floating-point type.",
        [
            new("RecDiffy.Rules.DecimalParseRule.Apply(RecDiffy.Rules.RowContext&)", "The [Obsolete] DecimalParseRule (replaced by ParseDecimalRule, #150); no registered rule name builds it."),
        ]),
        new("RecDiffy.Rounding.TryFromDouble(System.Double, System.Decimal&)", "Parses a double's round-trip text as a decimal; kept only for the [Obsolete] rules (#361 audit).",
        [
            new("RecDiffy.Rules.Numbers.CanBeNumber(System.Object)", "Numbers.CanBeNumber is itself a door that only [Obsolete] rules call."),
            new("RecDiffy.Rules.DecimalParsing.Read(System.Object)", "DecimalParsing is [Obsolete] and called only by the [Obsolete] DecimalParseRule."),
        ]),
    ];

    // isinst of double, float and Half: a type test that passes the value on or rejects it.
    private static FloatingPointIlUse[] TypeTests() =>
        [new("isinst System.Double"), new("isinst System.Single"), new("isinst System.Half")];

    // The OLE Automation serial reader: range-check a double, then DateTime.FromOADate.
    private static FloatingPointIlUse[] SerialReader() =>
    [
        new("calls System.Double.IsFinite(System.Double)"),
        new("calls System.DateTime.FromOADate(System.Double)"),
        new("uses the floating-point opcode ldc.r8 -657435"),
        new("uses the floating-point opcode ldc.r8 2958466"),
    ];

    // The caller of a serial reader: it tests for a boxed double, unboxes it and passes it on.
    private static FloatingPointIlUse[] SerialCaller(string reader) =>
        [new("isinst System.Double"), new("unbox.any System.Double"), new("calls " + reader)];

    // A boxed double or a boxed float, tested, unboxed and widened to double for Rounding.TryFromDouble.
    private static FloatingPointIlUse[] BoxedFloatReader() =>
    [
        new("isinst System.Double"),
        new("unbox.any System.Double"),
        new("isinst System.Single"),
        new("unbox.any System.Single"),
        new("uses the floating-point opcode conv.r8"),
    ];
}

internal sealed record FloatingPointLineAllow(string RelativePath, string LineText, string Reason, int Count = 1);

internal sealed record FloatingPointMemberAllow(string Key, string Reason);

internal sealed record FloatingPointIlUse(string What, int Count = 1);

internal sealed record FloatingPointIlAllow(string Method, string Reason, IReadOnlyList<FloatingPointIlUse> Uses);

internal sealed record FloatingPointDoorCaller(string Method, string Reason);

internal sealed record FloatingPointConversionDoor(string Method, string Reason, IReadOnlyList<FloatingPointDoorCaller> Callers);
