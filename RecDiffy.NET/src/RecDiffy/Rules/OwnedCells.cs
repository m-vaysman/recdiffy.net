using System.Collections;

namespace RecDiffy.Rules;

// One row's raw cells on intake's path (#395). The source built the dictionary for this one row and kept no reference
// to it, so the row keeps that dictionary instead of copying it into a second one. Only reads are exposed: a rule that
// holds RowContext.Raw cannot cast it back to a dictionary and write to it.
internal sealed class OwnedCells(IReadOnlyDictionary<string, object?> cells) : IReadOnlyDictionary<string, object?>
{
    public int Count => cells.Count;

    public IEnumerable<string> Keys => cells.Keys;

    public IEnumerable<object?> Values => cells.Values;

    public object? this[string key] => cells[key];

    public bool ContainsKey(string key) => cells.ContainsKey(key);

    public bool TryGetValue(string key, out object? value) => cells.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => cells.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
