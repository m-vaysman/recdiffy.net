using System.Security.Cryptography;
using System.Text;

namespace RecDiffy.Engine;

internal static class SideBySideBuilder
{
    public static SideBySideView Build(ReconciliationResult result, RunState state, SideBySideSpec spec)
    {
        var rows = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        foreach (var row in result.Rows.Where(row => row.Disposition != Disposition.Suppressed))
        {
            if (!state.ByRef.TryGetValue((row.Row.Source, row.Row.RowKey), out var mutable))
                continue;
            if (spec.Filter is not null && spec.Filter.Terms.Any(term => WhenEvaluator.Read(mutable, term.Field, state.Pool) != term.Value))
                continue;
            var rowKey = string.Join("|", spec.Rows.Select(field => WhenEvaluator.Read(mutable, field, state.Pool) ?? ""));
            var columnKey = string.Join("|", spec.Columns.Select(field => WhenEvaluator.Read(mutable, field, state.Pool) ?? ""));
            var key = rowKey + "\u001f" + columnKey;
            if (!rows.TryGetValue(key, out var accumulator))
            {
                accumulator = new Accumulator(spec.Rows.Select(field => WhenEvaluator.Read(mutable, field, state.Pool) ?? "").ToArray(), spec.Columns.Select(field => WhenEvaluator.Read(mutable, field, state.Pool) ?? "").ToArray(), spec.Measures);
                rows[key] = accumulator;
            }

            if (row.Disposition is Disposition.LeftOnly)
                accumulator.Add(mutable, Side.Left, unmatched: true);
            else if (row.Disposition is Disposition.RightOnly)
                accumulator.Add(mutable, Side.Right, unmatched: true);
            else if (row.Disposition is Disposition.Matched && row.Row.Source == state.Profile.Model.LeftSource)
                accumulator.Add(mutable, Side.Left, unmatched: false);
            else if (row.Disposition is Disposition.Matched)
                accumulator.Add(mutable, Side.Right, unmatched: false);
        }

        return new SideBySideView(rows.Values.Select(accumulator => accumulator.ToRow()).ToArray());
    }

    private sealed class Accumulator
    {
        private readonly string[] _rows;
        private readonly string[] _columns;
        private readonly decimal[] _left;
        private readonly decimal[] _right;
        private readonly decimal[] _leftOnly;
        private readonly decimal[] _rightOnly;
        private readonly string[] _fields;
        private int _leftCount;
        private int _rightCount;

        public Accumulator(string[] rows, string[] columns, IReadOnlyList<MeasureSpec> measures)
        {
            _rows = rows;
            _columns = columns;
            _fields = measures.Select(measure => measure.Field).ToArray();
            _left = new decimal[measures.Count];
            _right = new decimal[measures.Count];
            _leftOnly = new decimal[measures.Count];
            _rightOnly = new decimal[measures.Count];
        }

        public void Add(MutableRow row, Side side, bool unmatched)
        {
            if (side == Side.Left)
                _leftCount++;
            else
                _rightCount++;
            for (var index = 0; index < _fields.Length; index++)
            {
                if (!row.Values.TryGetValue(_fields[index], out var value) || value.IsNull)
                    continue;
                if (side == Side.Left)
                {
                    _left[index] += value.Number;
                    if (unmatched)
                        _leftOnly[index] += value.Number;
                }
                else
                {
                    _right[index] += value.Number;
                    if (unmatched)
                        _rightOnly[index] += value.Number;
                }
            }
        }

        public SideBySideRow ToRow()
        {
            var measures = new SideBySideMeasure[_fields.Length];
            for (var index = 0; index < _fields.Length; index++)
            {
                var delta = _right[index] - _left[index];
                var matched = delta - _rightOnly[index] + _leftOnly[index];
                measures[index] = new SideBySideMeasure(_fields[index], _left[index], _right[index], delta, -_leftOnly[index], _rightOnly[index], matched);
            }

            return new SideBySideRow(_rows, _columns, _leftCount, _rightCount, measures);
        }
    }
}

internal static class GroupIds
{
    public static GroupId Create(IEnumerable<SourceRef> members)
    {
        var text = string.Join("\n", members.OrderBy(member => member.Source, StringComparer.Ordinal).ThenBy(member => member.RowKey, StringComparer.Ordinal).Select(member => member.Source + "\t" + member.RowKey));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new GroupId(hash[..32]);
    }
}
