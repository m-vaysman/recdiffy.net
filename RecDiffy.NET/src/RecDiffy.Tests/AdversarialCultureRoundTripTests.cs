using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using RecDiffy.Rules;
using Xunit.Abstractions;

namespace RecDiffy.Tests;

// Adversarial pass on main. Spec §3.5: each side's text is read with its declared culture. Text that a culture itself
// writes, a grouped ("N") or plain ("F") number, or a date in a declared format, should read back to the same value when
// that culture is declared. Runs every specific culture this host knows.
public class AdversarialCultureRoundTripTests(ITestOutputHelper output)
{
    private static readonly string[] NumberFormats = ["N2", "F2", "N4", "F0", "N0"];
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d MMMM yyyy", "dd MMM yyyy", "yyyy-MM-dd"];

    [Fact]
    public void Every_culture_reads_back_what_it_writes()
    {
        // A culture whose decimal and group separators are the same character (mn-Mong-MN in this ICU) cannot be read by any parser.
        var cultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures).Append(CultureInfo.InvariantCulture)
            .Where(culture => culture.NumberFormat.NumberDecimalSeparator != culture.NumberFormat.NumberGroupSeparator).ToArray();
        var failures = new ConcurrentDictionary<string, ConcurrentBag<string>>(StringComparer.Ordinal);
        var clock = Stopwatch.StartNew();
        var checks = 0L;
        Parallel.ForEach(cultures, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, culture =>
        {
            var rules = CultureProfile.Rules(culture.Name);
            var number = rules.Build("decimal.parse", new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Raw", ["to"] = "Parsed" });
            var random = new Random(StringComparer.Ordinal.GetHashCode(culture.Name));
            foreach (var index in Enumerable.Range(0, 400))
            {
                var magnitude = (decimal)Math.Pow(10, random.Next(0, 13));
                var value = Math.Round((decimal)random.NextDouble() * magnitude * (random.Next(3) == 0 ? -1 : 1), 4);
                foreach (var format in NumberFormats)
                {
                    var text = value.ToString(format, culture);
                    var expected = Math.Round(value, format[1] - '0', MidpointRounding.AwayFromZero);
                    var row = RowContext.Create("r", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Raw"] = text });
                    number.Apply(ref row);
                    Interlocked.Increment(ref checks);
                    if (row.IsNull("Parsed") || row.Values["Parsed"].Number != Math.Round(expected, 4, MidpointRounding.ToEven))
                        failures.GetOrAdd($"{culture.Name} number {format}", _ => []).Add($"'{text}' -> {(row.IsNull("Parsed") ? string.Join(",", row.Diagnostics.Select(d => d.Code)) : row.Values["Parsed"].Number.ToString(CultureInfo.InvariantCulture))}");
                }
            }

            var strict = CultureDateParsing.Strict(culture);
            foreach (var format in DateFormats)
            {
                var dates = rules.Build("date.parse", new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Raw", ["to"] = "Day" }, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["formats"] = [format] });
                foreach (var offset in Enumerable.Range(0, 60))
                {
                    var date = new DateOnly(2020, 1, 1).AddDays(offset * 37);
                    var text = date.ToString(format, strict);
                    var row = RowContext.Create("r", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Raw"] = text });
                    dates.Apply(ref row);
                    Interlocked.Increment(ref checks);
                    if (row.IsNull("Day") || row.Values["Day"].Date != date)
                        failures.GetOrAdd($"{culture.Name} date {format}", _ => []).Add($"'{text}' for {date:yyyy-MM-dd} -> {(row.IsNull("Day") ? "Invalid" : row.Values["Day"].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
                }
            }
        });
        output.WriteLine($"cultures={cultures.Length} checks={checks} wall={clock.Elapsed.TotalSeconds:F1}s failing (culture, format) pairs={failures.Count}");
        foreach (var pair in failures.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            output.WriteLine($"{pair.Key}: {pair.Value.Count} e.g. {pair.Value.Order(StringComparer.Ordinal).First()}");
        Assert.Empty(failures);
    }
}
