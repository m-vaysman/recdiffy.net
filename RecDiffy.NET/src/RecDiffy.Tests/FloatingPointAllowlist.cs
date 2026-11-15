namespace RecDiffy.Tests;

// #361: Allowlist of floating-point uses that the FloatGuard permits. Every entry is an exact (key, text) pair with
// a reason. A stale entry (no longer matching any scanned line or reflected member) fails the gate, so the list cannot rot.
internal static class FloatingPointAllowlist
{
    // Source lines that use float/double on purpose. Each reason explains why the use is safe.
    internal static readonly IReadOnlyList<FloatingPointLineAllow> Lines =
    [
        // Intake passes ADO.NET floating point through unconverted so decimal.parse reports it as FloatValue (#158).
        new("RecDiffy/Intake/RecordSource/DataReaderSource.Body.cs", "double or float or Half => value,", "Intake forwards floating point untouched so the parse rules can reject it (#158)."),

        // Excel/automation date serials (OLE Automation dates) come in as double and are read exactly, then rejected outside the valid range.
        new("RecDiffy/Rules/CultureDateParsing.cs", "double serial when TryFromOADate(serial, out var date) => Parsed.Of(date),", "Reads an OLE Automation date serial supplied as double; non-serial text goes through the culture formats instead."),
        new("RecDiffy/Rules/CultureDateParsing.cs", "private static bool TryFromOADate(double serial, out DateOnly date)", "The OLE date serial is defined as a double; the helper validates its finite range before DateTime.FromOADate."),
        new("RecDiffy/Rules/CultureDateParsing.cs", "if (double.IsFinite(serial) is false || serial <= -657435.0d || serial >= 2958466.0d)", "Bounding the double serial against the exact OLE Automation valid range before FromOADate."),
        new("RecDiffy/Rules/DateParsing.cs", "double serial when TryFromOADate(serial, out var date) => Parsed.Of(date),", "Obsolete date parser; reads the OLE Automation date serial as double (replaced by CultureDateParsing, #158)."),
        new("RecDiffy/Rules/DateParsing.cs", "private static bool TryFromOADate(double serial, out DateOnly date)", "Obsolete date helper; validates the double serial range before DateTime.FromOADate."),
        new("RecDiffy/Rules/DateParsing.cs", "if (double.IsFinite(serial) is false || serial <= -657435.0d || serial >= 2958466.0d)", "Obsolete date helper; bounds the double serial to the OLE Automation range."),
        new("RecDiffy/Rules/DeclaredDateParsing.cs", "double serial when FromOADate(serial) is { } date => Parsed.Of(date),", "Reads an OLE Automation date serial as double for the declared-format date parser."),
        new("RecDiffy/Rules/DeclaredDateParsing.cs", "private static DateOnly? FromOADate(double serial) =>", "The OLE date serial arrives as double; FromOADate guards its finite range."),
        new("RecDiffy/Rules/DeclaredDateParsing.cs", "double.IsFinite(serial) && serial is > -657435.0d and < 2958466.0d", "Bounds the double serial to the OLE Automation valid range before FromOADate."),
        new("RecDiffy/Rules/PartyFormatDateRule.cs", "double serial when FromOADate(serial) is { } date => WriteDate(row, date, rawText),", "The date.parse rule reads an OLE Automation date serial supplied as double."),
        new("RecDiffy/Rules/PartyFormatDateRule.cs", "private static DateOnly? FromOADate(double serial) =>", "The OLE date serial arrives as double; FromOADate guards its finite range."),
        new("RecDiffy/Rules/PartyFormatDateRule.cs", "double.IsFinite(serial) && serial is > -657435.0d and < 2958466.0d", "Bounds the double serial to the OLE Automation valid range before FromOADate."),

        // The current decimal.parse rule rejects float/double outright, never converts them.
        new("RecDiffy/Rules/CultureDecimalRule.cs", "double or float or Half => Reject(row, \"FloatValue\", rawText),", "decimal.parse matches floating types only to reject them; it never converts (#158)."),

        // NumberRead and DecimalParsing accept double/float only by handing them to Rounding.TryFromDouble, which is decimal-only after conversion.
        new("RecDiffy/Rules/NumberRead.cs", "double value when Rounding.TryFromDouble(value, out var number) => NumberRead.Of(number),", "NumberRead converts a double to decimal only through Rounding.TryFromDouble; a non-finite or out-of-range value fails. See #361 audit."),
        new("RecDiffy/Rules/NumberRead.cs", "float value when Rounding.TryFromDouble(value, out var number) => NumberRead.Of(number),", "NumberRead converts a float to decimal only through Rounding.TryFromDouble; a non-finite value fails. See #361 audit."),
        new("RecDiffy/Rules/DecimalParsing.cs", "double value when Rounding.TryFromDouble(value, out var number) => Parsed.Of(number),", "Obsolete DecimalParsing converts a double to decimal only through Rounding.TryFromDouble. See #361 audit."),
        new("RecDiffy/Rules/DecimalParsing.cs", "float value when Rounding.TryFromDouble(value, out var number) => Parsed.Of(number),", "Obsolete DecimalParsing converts a float to decimal only through Rounding.TryFromDouble. See #361 audit."),
        new("RecDiffy/Schema/Rounding.cs", "public static bool TryFromDouble(double value, out decimal number)", "Rounding.TryFromDouble is the single decimal-only boundary for a double; it round-trips before decimal.TryParse."),
        new("RecDiffy/Schema/Rounding.cs", "return double.IsFinite(value)", "TryFromDouble rejects non-finite doubles and only then parses to decimal."),

        // Trading rule pack: the same two patterns, reject-not-convert for quantities, and a double/float/Half guard before reading.
        new("RecDiffy.RulePacks.Trading/CultureQuantityRule.cs", "double or float or Half => Unread(row, earlier, \"FloatValue\", rawText),", "Obsolete quantity rule matches floating types only to leave them unread, never converts (#170)."),
        new("RecDiffy.RulePacks.Trading/ScaledQuantityRule.cs", "double or float or Half => Unread(row, earlier, \"FloatValue\", rawText),", "quantity.absolute matches floating types only to leave them unread, never converts."),
        new("RecDiffy.RulePacks.Trading/CultureSideResolveRule.cs", "row.Raw.TryGetValue(column, out var raw) && raw is not (double or float or Half)", "side.resolve refuses to read a floating quantity so the sign is never derived from one."),
    ];

    // Members whose declared type legitimately contains double/float/Half, keyed by "DeclaringTypeName.MemberName".
    internal static readonly IReadOnlyList<FloatingPointMemberAllow> Members =
    [
        new("Rounding.TryFromDouble", "The single decimal-only boundary that converts a double to decimal; the Lines entries depend on it."),
        new("DateParsing.TryFromOADate", "Obsolete OLE Automation serial reader; a double is the serial's declared type."),
        new("CultureDateParsing.TryFromOADate", "OLE Automation serial reader; a double is the serial's declared type."),
        new("DeclaredDateParsing.FromOADate", "OLE Automation serial reader; a double is the serial's declared type."),
        new("PartyFormatDateRule.FromOADate", "OLE Automation serial reader; a double is the serial's declared type."),
    ];
}

internal sealed record FloatingPointLineAllow(string RelativePath, string LineText, string Reason);

internal sealed record FloatingPointMemberAllow(string Key, string Reason);