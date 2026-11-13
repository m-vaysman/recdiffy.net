namespace RecDiffy.Tests;

// #259: no rule reads RowContext.Instruments, so intake must not read the left side's identifier columns. The guard
// throws if anything asks for one of them, so re-adding the master build fails this test.
public class IntakeInstrumentColumnsTests
{
    private static readonly string[] IdentifierColumns = ["ISIN", "CUSIP", "SEDOL", "Ticker"];

    [Fact]
    public void Normalization_does_not_read_the_identifier_columns()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var left = new RawRecord(new SourceRef("left", "L1", 0), Cells("10.00"));
        var right = new RawRecord(new SourceRef("right", "R1", 0), Cells("10.00"));

        var normalized = Intake.Normalize(profile, [left], [right], ReferenceDataSet.Empty);

        Assert.Equal(10m, normalized.Left[0].Context.Values["Quantity"].Number);
        Assert.Equal(10m, normalized.Right[0].Context.Values["Quantity"].Number);
    }

    private static GuardedCells Cells(string quantity) =>
        new(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = quantity });

    private sealed class GuardedCells(IReadOnlyDictionary<string, object?> inner) : IReadOnlyDictionary<string, object?>
    {
        public int Count => inner.Count;

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<object?> Values => inner.Values;

        public object? this[string key] => TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException(key);

        public bool ContainsKey(string key)
        {
            Guard(key);
            return inner.ContainsKey(key);
        }

        public bool TryGetValue(string key, out object? value)
        {
            Guard(key);
            return inner.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private static void Guard(string key)
        {
            if (IdentifierColumns.Contains(key, StringComparer.Ordinal))
                throw new InvalidOperationException($"Intake read the identifier column '{key}'.");
        }
    }

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "intake.identifier-columns",
          "version": "1.0.0",
          "culture": "",
          "fields": [
            { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 2 }
          ],
          "normalization": {
            "left": { "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] },
            "right": { "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] }
          },
          "paths": [
            {
              "id": "all",
              "route": { "rule": "route.all" },
              "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Quantity" ] } ]
            }
          ]
        }
        """;
}
