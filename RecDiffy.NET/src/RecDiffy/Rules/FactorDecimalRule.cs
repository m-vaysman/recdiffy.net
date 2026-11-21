using System.Globalization;
using System.Numerics;

namespace RecDiffy.Rules;

// Reads a raw number exactly as decimal.parse does, multiplies it by a constant, then rounds to the field's scale, so a
// side that reports in thousands compares with a side that reports in units (#547). The factor comes before the
// rounding: 0.1 thousand at scale 0 is 100, not 0.
internal sealed class FactorDecimalRule(string from, string to, decimal factor, int scale, MidpointMode midpoint, NumberFormatInfo format) : INormalizationRule
{
    public static readonly IReadOnlyList<string> Required = ["from", "to", "factor"];

    public string RuleId => "decimal.scale";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public static FactorDecimalRule Create(RuleCall call, FieldSchema schema, RuleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(settings);
        ProfileError[] shapeErrors = [.. Required.Where(name => call.Strings.ContainsKey(name) is false).Select(name =>
            new ProfileError("InvalidParameter", $"decimal.scale {name} must be a string or number, not a list or an object."))];
        if (shapeErrors.Length > 0)
            throw new ProfileCompileException(shapeErrors);

        var to = call.Strings["to"];
        var factorText = call.Strings["factor"];
        ProfileError[] errors = [.. TargetErrors(to, schema), .. FactorErrors(factorText)];
        return errors.Length == 0
            ? new FactorDecimalRule(call.Strings["from"], to, ReadFactor(factorText) ?? 1m, schema.Scale(to), settings.Midpoint, settings.Culture.NumberFormat)
            : throw new ProfileCompileException(errors);
    }

    public void Apply(ref RowContext row) => row = Parse(row);

    // A float or double is never converted: any conversion is a choice the user has to make before the value arrives.
    private RowContext Parse(RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var rawText = InvariantText.Of(raw);
        return raw switch
        {
            double or float or Half => Reject(row, "FloatValue", rawText),
            _ => raw.ReadNumber(format).Convert(
                value: number => Scaled(row, number, rawText),
                missing: () => row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, null, null),
                failed: () => Reject(row, "DecimalParse", rawText)),
        };
    }

    // decimal rounds a product it cannot hold exactly, half-even, before the profile midpoint is applied (#551). So the
    // rule multiplies only when the product is certain to be exact: the digits of the number times the digits of the
    // factor, as written, fit in decimal's 96 bits, and their decimal places add up to 28 or fewer. Anything else is a
    // number RecDiffy cannot hold, as a bad number is.
    private RowContext Scaled(RowContext row, decimal number, string? rawText) =>
        (FitsExactly(number, factor) ? CheckedDecimal.Multiply(number, factor) : null) switch
        {
            { } product => WriteRounded(row, Rounding.Round(product, scale, midpoint), rawText),
            null => Reject(row, "DecimalParse", rawText),
        };

    private static readonly BigInteger MaxMantissa = (BigInteger.One << 96) - 1;

    private static bool FitsExactly(decimal number, decimal factor) =>
        number.Scale + factor.Scale <= 28 && Mantissa(number) * Mantissa(factor) <= MaxMantissa;

    // The unsigned 96-bit integer a decimal stores: 0.0050 is 50 at scale 4.
    private static BigInteger Mantissa(decimal value)
    {
        var bits = decimal.GetBits(value);
        return ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
    }

    private RowContext WriteRounded(RowContext row, decimal rounded, string? rawText) =>
        row.Write(to, CanonicalCell.FromDecimal(rounded), RuleId, rawText, rounded.ToString(CultureInfo.InvariantCulture));

    private RowContext Reject(RowContext row, string code, string? rawText) =>
        row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, rawText, null)
            .Diagnose(code, DiagnosticSeverity.Invalid, RuleId, to, rawText);

    private static IEnumerable<ProfileError> TargetErrors(string to, FieldSchema schema) =>
        schema.TryGet(to, out var field) && field.Type is not FieldType.Decimal
            ? [new ProfileError("InvalidParameter", $"decimal.scale writes '{to}', a {field.Type} field. It writes only Decimal fields.")]
            : [];

    private static IEnumerable<ProfileError> FactorErrors(string text) =>
        ReadFactor(text) is null or 0m
            ? [new ProfileError("InvalidParameter", $"decimal.scale factor \"{text}\" must be a nonzero invariant number such as \"1000\" or \"0.001\".")]
            : [];

    // Profile JSON is RecDiffy's own format, so the factor is read strictly invariant: a sign and a decimal point.
    private static decimal? ReadFactor(string text) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
