namespace RecDiffy.Engine;

internal static class HotPath
{
    // Measured diff loop: no heap allocation when every field is equal.
    public static int CompareExact(ReadOnlySpan<decimal> left, ReadOnlySpan<decimal> right)
    {
        var breaks = 0;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                breaks++;
        }
        return breaks;
    }
}

internal static class Differ
{
    private static readonly string[] Additive = ["Quantity", "Accrued", "Fees", "NetCash"];

    public static DiffProduct Diff(RunState state, MatchGroup group, Dictionary<string, List<string>> attributions, ref int nextAttr)
    {
        var left = Project(state, group.Left);
        var right = Project(state, group.Right);
        var path = state.Profile.Model.Paths.First(p => p.Id == group.Left.Concat(group.Right).First().PathId);
        var specs = path.Diffs.ToDictionary(d => d.Field, StringComparer.Ordinal);
        var outcomes = new List<(string Field, FieldOutcome Outcome, decimal? Delta, List<string> Attributed, FieldDiff Diff)>();
        foreach (var field in state.Profile.Model.Fields.Where(field => field.Role is FieldRole.Key or FieldRole.Economic))
        {
            specs.TryGetValue(field.Name, out var spec);
            var l = left.TryGetValue(field.Name, out var lv) ? lv : Null(field.Type);
            var r = right.TryGetValue(field.Name, out var rv) ? rv : Null(field.Type);
            if (spec?.ApplicableWhen is WhenClause when && !Holds(state, left, when) && !Holds(state, right, when))
            {
                outcomes.Add((field.Name, FieldOutcome.NotCompared, null, [], default));
                continue;
            }
            var packaged = group.Left.Concat(group.Right).Any(item => item.Package);
            if (field.Name == "Fees" && packaged && (l.IsNull || l.Number == 0m) && (r.IsNull || r.Number == 0m))
            {
                outcomes.Add((field.Name, FieldOutcome.Exact, 0m, [], Line(field.Name, FieldOutcome.Exact, Zero(l), Zero(r), new Delta(DeltaKind.Number, 0m, 0), 0)));
                continue;
            }
            if (l.IsNull && r.IsNull)
            {
                outcomes.Add((field.Name, FieldOutcome.Exact, field.Type == FieldType.Decimal ? 0m : null, [], Line(field.Name, FieldOutcome.Exact, l, r, default, 0)));
                continue;
            }
            if (l.IsNull || r.IsNull)
            {
                outcomes.Add((field.Name, FieldOutcome.Break, null, [], Line(field.Name, FieldOutcome.Break, l, r, default, 0)));
                continue;
            }
            if (field.Type == FieldType.Decimal)
            {
                var delta = r.Number - l.Number;
                var outcome = delta == 0m ? FieldOutcome.Exact : Tolerance(state, spec, left, right, delta);
                outcomes.Add((field.Name, outcome, delta, [], Line(field.Name, outcome, l, r, new Delta(DeltaKind.Number, delta, 0), 0)));
            }
            else if (field.Type == FieldType.Date)
            {
                var days = r.Date.DayNumber - l.Date.DayNumber;
                var outcome = days == 0 ? FieldOutcome.Exact : FieldOutcome.Break;
                outcomes.Add((field.Name, outcome, null, [], Line(field.Name, outcome, l, r, new Delta(DeltaKind.Days, 0m, days), 0)));
            }
            else
            {
                var same = l.Interned == r.Interned;
                var outcome = same ? FieldOutcome.Exact : FieldOutcome.Break;
                outcomes.Add((field.Name, outcome, null, [], Line(field.Name, outcome, l, r, default, 0)));
            }
        }

        Attribute(state, left, right, outcomes);
        var diffs = new List<FieldDiff>();
        foreach (var outcome in outcomes.Where(o => o.Outcome != FieldOutcome.NotCompared))
        {
            var attr = 0;
            if (outcome.Attributed.Count > 0)
            {
                attr = nextAttr++;
                attributions[attr.ToString(CultureInfo.InvariantCulture)] = outcome.Attributed;
            }
            var line = outcome.Diff;
            diffs.Add(line with { Outcome = outcome.Outcome, Attribution = new AttributionId(attr) });
        }
        var compared = diffs.Where(d => d.Outcome != FieldOutcome.NotCompared).Select(d => d.Outcome).ToList();
        var rollup = compared.Contains(FieldOutcome.Break) ? FieldOutcome.Break
            : compared.Contains(FieldOutcome.WithinTolerance) ? FieldOutcome.WithinTolerance
            : compared.Count == 0 ? FieldOutcome.NotCompared : FieldOutcome.Exact;
        string? root = null;
        if (rollup == FieldOutcome.Break)
        {
            root = outcomes.FirstOrDefault(o => o.Outcome == FieldOutcome.Break && o.Attributed.Count == 0).Field;
            if (string.IsNullOrEmpty(root))
                root = outcomes.FirstOrDefault(o => o.Outcome == FieldOutcome.Break).Field;
        }
        return new DiffProduct(rollup, root, diffs, left, right);
    }

    private static void Attribute(RunState state, Dictionary<string, CanonicalValue> left, Dictionary<string, CanonicalValue> right, List<(string Field, FieldOutcome Outcome, decimal? Delta, List<string> Attributed, FieldDiff Diff)> outcomes)
    {
        int Index(string field) => outcomes.FindIndex(o => o.Field == field);
        var quantity = Index("Quantity");
        foreach (var name in new[] { "Accrued", "Fees" })
        {
            var index = Index(name);
            if (index < 0 || quantity < 0 || outcomes[index].Outcome is FieldOutcome.Exact or FieldOutcome.NotCompared)
                continue;
            if (outcomes[quantity].Delta is not decimal || outcomes[quantity].Outcome == FieldOutcome.Exact)
                continue;
            if (!left.TryGetValue(name, out var l) || !right.TryGetValue(name, out var r) || l.IsNull || r.IsNull)
                continue;
            if (!left.TryGetValue("Quantity", out var lq) || !right.TryGetValue("Quantity", out var rq) || lq.Number == 0m)
                continue;
            var expected = Rounding.Round(l.Number * rq.Number / lq.Number, state.ScaleOf(name), state.Midpoint);
            if (expected == r.Number)
            {
                var current = outcomes[index];
                current.Attributed = ["Quantity"];
                current.Outcome = Worse(current.Outcome, outcomes[quantity].Outcome);
                outcomes[index] = current;
            }
        }

        var net = Index("NetCash");
        if (net < 0 || outcomes[net].Outcome is FieldOutcome.NotCompared)
            return;
        if (FormulaDelta(state, left, right, out var expectedDelta, out var roots))
        {
            var actual = outcomes[net].Delta ?? 0m;
            if (actual == expectedDelta)
            {
                var current = outcomes[net];
                current.Attributed = roots;
                current.Outcome = roots.Select(root => outcomes[Index(root)].Outcome).Aggregate(FieldOutcome.Exact, Worse);
                outcomes[net] = current;
            }
        }
        else if (Agree(outcomes, "Quantity") && Agree(outcomes, "Price") && Agree(outcomes, "Accrued"))
        {
            var actual = outcomes[net].Delta ?? 0m;
            if (actual != 0m && Index("Fees") >= 0)
            {
                var side = Symbol(state, left, "Side");
                var leftFee = left.TryGetValue("Fees", out var fee) && !fee.IsNull ? fee.Number : 0m;
                var rightFee = side is "SELL" or "REC_FIXED" ? leftFee - actual : leftFee + actual;
                var feeIndex = Index("Fees");
                var feeOutcome = outcomes[feeIndex];
                feeOutcome.Outcome = FieldOutcome.Break;
                feeOutcome.Attributed = [];
                var l = new CanonicalValue(FieldType.Decimal, false, leftFee, default, 0);
                var r = new CanonicalValue(FieldType.Decimal, false, rightFee, default, 0);
                feeOutcome.Diff = Line(Field("Fees", state), FieldOutcome.Break, l, r, new Delta(DeltaKind.Number, rightFee - leftFee, 0), 0);
                outcomes[feeIndex] = feeOutcome;
                var current = outcomes[net];
                current.Attributed = ["Fees"];
                current.Outcome = FieldOutcome.Break;
                outcomes[net] = current;
            }
        }
    }

    private static bool FormulaDelta(RunState state, Dictionary<string, CanonicalValue> left, Dictionary<string, CanonicalValue> right, out decimal delta, out List<string> roots)
    {
        delta = 0m;
        roots = [];
        if (!TryNet(state, left, out var leftNet) || !TryNet(state, right, out var rightNet))
            return false;
        delta = Rounding.Round(rightNet - leftNet, 2, state.Midpoint);
        foreach (var field in new[] { "Quantity", "Price", "Accrued", "Fees" })
        {
            var l = left.TryGetValue(field, out var lv) ? lv : Null(FieldType.Decimal);
            var r = right.TryGetValue(field, out var rv) ? rv : Null(FieldType.Decimal);
            if (l.IsNull && r.IsNull)
                continue;
            if (l.IsNull || r.IsNull || l.Number != r.Number)
                roots.Add(field);
        }
        return true;
    }

    private static bool TryNet(RunState state, Dictionary<string, CanonicalValue> row, out decimal net)
    {
        net = 0m;
        if (!row.TryGetValue("AssetClass", out var asset) || !row.TryGetValue("Side", out var side) ||
            !row.TryGetValue("Quantity", out var quantity) || !row.TryGetValue("Price", out var price))
            return false;
        var name = state.Pool.Symbol(asset.Interned);
        decimal? principal = name switch
        {
            "Bond" => Rounding.Round(quantity.Number * price.Number / 100m, 2, state.Midpoint),
            "Equity" or "Forward" => Rounding.Round(quantity.Number * price.Number, 2, state.Midpoint),
            _ => null,
        };
        if (principal is null)
            return false;
        var accrued = row.TryGetValue("Accrued", out var a) && !a.IsNull ? a.Number : 0m;
        var fees = row.TryGetValue("Fees", out var f) && !f.IsNull ? f.Number : 0m;
        var sideName = state.Pool.Symbol(side.Interned);
        net = sideName is "SELL" or "REC_FIXED" ? principal.Value + accrued - fees : principal.Value + accrued + fees;
        return true;
    }

    private static bool Agree(List<(string Field, FieldOutcome Outcome, decimal? Delta, List<string> Attributed, FieldDiff Diff)> outcomes, string field)
    {
        var found = outcomes.FirstOrDefault(o => o.Field == field);
        return found.Field == field && found.Outcome is FieldOutcome.Exact or FieldOutcome.NotCompared;
    }

    private static FieldOutcome Tolerance(RunState state, DiffFieldModel? spec, Dictionary<string, CanonicalValue> left, Dictionary<string, CanonicalValue> right, decimal delta)
    {
        if (spec?.Absolute is not decimal absolute)
            return FieldOutcome.Break;
        if (spec.ToleranceWhen is WhenClause when && !(Holds(state, left, when) && Holds(state, right, when)))
            return FieldOutcome.Break;
        return decimal.Abs(delta) <= absolute ? FieldOutcome.WithinTolerance : FieldOutcome.Break;
    }

    private static bool Holds(RunState state, Dictionary<string, CanonicalValue> row, WhenClause when)
    {
        var text = row.TryGetValue(when.Field, out var value) && !value.IsNull ? state.Pool.Format(value) : null;
        if (value.Type == FieldType.Symbol)
            text = state.Pool.Symbol(value.Interned);
        if (value.Type == FieldType.Identifier)
            text = state.Pool.Id(value.Interned).Value;
        if (when.EqualsValue is not null)
            return text == when.EqualsValue;
        if (when.In is not null)
            return text is not null && when.In.Contains(text, StringComparer.Ordinal);
        return true;
    }

    private static Dictionary<string, CanonicalValue> Project(RunState state, List<MatchItem> items)
    {
        if (items.Count == 1)
            return new Dictionary<string, CanonicalValue>(items[0].Row.Values, StringComparer.Ordinal);
        var result = new Dictionary<string, CanonicalValue>(StringComparer.Ordinal);
        foreach (var field in state.Profile.Model.Fields)
        {
            if (Additive.Contains(field.Name))
            {
                decimal sum = 0m;
                var any = false;
                foreach (var item in items)
                {
                    if (item.Row.Values.TryGetValue(field.Name, out var value) && !value.IsNull)
                    {
                        sum += value.Number;
                        any = true;
                    }
                }
                result[field.Name] = any
                    ? new CanonicalValue(FieldType.Decimal, false, Rounding.Round(sum, field.Scale, state.Midpoint), default, 0)
                    : Null(field.Type);
            }
            else if (items[0].Row.Values.TryGetValue(field.Name, out var first))
                result[field.Name] = first;
        }
        return result;
    }

    private static FieldDiff Line(string field, FieldOutcome outcome, CanonicalValue left, CanonicalValue right, Delta delta, int attr) =>
        new(new FieldId(field), outcome, left, right, delta, default, new AttributionId(attr));

    private static FieldDiff Line(FieldModel field, FieldOutcome outcome, CanonicalValue left, CanonicalValue right, Delta delta, int attr) =>
        Line(field.Name, outcome, left, right, delta, attr);

    private static FieldModel Field(string name, RunState state) => state.Profile.Model.Fields.First(field => field.Name == name);

    private static CanonicalValue Null(FieldType type) => new(type, true, 0m, default, 0);

    private static CanonicalValue Zero(CanonicalValue value) => value.IsNull ? new CanonicalValue(FieldType.Decimal, false, 0m, default, 0) : value;

    private static string Symbol(RunState state, Dictionary<string, CanonicalValue> row, string field) =>
        row.TryGetValue(field, out var value) && !value.IsNull ? state.Pool.Symbol(value.Interned) : "";

    private static FieldOutcome Worse(FieldOutcome left, FieldOutcome right) => (int)left > (int)right ? left : right;

    internal sealed record DiffProduct(FieldOutcome Outcome, string? Root, List<FieldDiff> Diffs, Dictionary<string, CanonicalValue> Left, Dictionary<string, CanonicalValue> Right);
}
