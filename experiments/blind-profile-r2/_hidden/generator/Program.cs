using System.Globalization;
using System.Text;

static class Gen
{
    static readonly CultureInfo US = CultureInfo.GetCultureInfo("en-US");
    static readonly CultureInfo DE = CultureInfo.GetCultureInfo("de-DE");
    static readonly CultureInfo FR = CultureInfo.GetCultureInfo("fr-FR");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string OutDir = "";

    record Sec(string Isin, string Ticker, string Ccy, decimal Base);
    record Trade(int N, Sec S, bool Buy, decimal Qty, decimal Px, string Party, string Acct, DateOnly TD, DateOnly SD)
    {
        public decimal Net => Math.Round(Qty * Px, 2, MidpointRounding.AwayFromZero);
    }

    const string Alnum = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
    static readonly string[] Parties = ["ACME", "BNPX", "CITI", "DBKX", "GSCO", "HSBC", "JPMS", "MSCO", "NOMU", "UBSW", "SOCG", "BARC"];
    static readonly string[] Desks = ["EQ-NY", "EQ-LDN", "FI-NY", "FI-LDN", "DERIV", "PB-1", "PB-2"];
    static readonly string[] Comments = ["", "", "", "block, see ticket", "late confirm", "client \"A\" request", "", "manual, amended", "auto"];

    static void Main(string[] args)
    {
        OutDir = args[0];
        P01(); P02(); P03(); P04(); P05(); P06(); P07(); P08(); P09(); P10();
    }

    static string F(decimal v, int dp, CultureInfo c, bool group) => v.ToString((group ? "N" : "F") + dp, c);
    static string D(DateOnly d, string fmt, CultureInfo c) => d.ToString(fmt, c);
    static string Thou(decimal v) => v.ToString("0.#####", Inv);

    static char Check(string s)
    {
        var digits = new StringBuilder();
        foreach (var ch in s)
            digits.Append(char.IsDigit(ch) ? ch.ToString() : (ch - 'A' + 10).ToString(Inv));
        int sum = 0; bool dbl = true;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (dbl) { d *= 2; if (d > 9) d -= 9; }
            sum += d; dbl = !dbl;
        }
        return (char)('0' + (10 - sum % 10) % 10);
    }

    static List<Sec> Pool(Random r, int n)
    {
        string[] cc = ["US", "GB", "DE", "FR", "JP", "CH", "NL", "CA", "AU", "SE"];
        string[] ccy = ["USD", "GBP", "EUR", "EUR", "JPY", "CHF", "EUR", "CAD", "AUD", "SEK"];
        var list = new List<Sec>(); var seen = new HashSet<string>();
        while (list.Count < n)
        {
            int k = r.Next(cc.Length);
            var body = cc[k] + new string(Enumerable.Range(0, 9).Select(_ => Alnum[r.Next(Alnum.Length)]).ToArray());
            var isin = body + Check(body);
            if (!seen.Add(isin)) continue;
            var tick = new string(Enumerable.Range(0, r.Next(3, 5)).Select(_ => (char)('A' + r.Next(26))).ToArray());
            list.Add(new Sec(isin, tick, ccy[k], Math.Round(5m + (decimal)r.NextDouble() * 895m, 2)));
        }
        return list;
    }

    static DateOnly Bd(Random r)
    {
        var d = new DateOnly(2024, 1, 2).AddDays(r.Next(0, 270));
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    static DateOnly Settle(DateOnly d, int n = 2)
    {
        for (int i = 0; i < n; i++)
        {
            d = d.AddDays(1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        }
        return d;
    }

    static decimal Qty(Random r) => r.Next(1, 400) * new[] { 1, 5, 10, 25, 50, 100 }[r.Next(6)];
    static decimal Px(Random r, Sec s) => Math.Round(s.Base * (decimal)(0.9 + 0.2 * r.NextDouble()), 4);

    static Trade NewTrade(Random r, List<Sec> pool, int n)
    {
        var s = pool[r.Next(pool.Count)];
        var td = Bd(r);
        return new Trade(n, s, r.Next(2) == 0, Qty(r), Px(r, s), Parties[r.Next(Parties.Length)], "ACC-" + r.Next(1000, 9999).ToString(Inv), td, Settle(td));
    }

    static List<Trade> Trades(Random r, List<Sec> pool, int count, int start = 100000)
    {
        var list = new List<Trade>(); int n = start;
        for (int i = 0; i < count; i++) { n += r.Next(1, 9); list.Add(NewTrade(r, pool, n)); }
        return list;
    }

    static void Shuffle<T>(List<T> l, Random r)
    {
        for (int i = l.Count - 1; i > 0; i--) { int j = r.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
    }

    static string Csv(string v) => v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    static void Write(int pair, string side, string[] headers, List<string[]> rows)
    {
        var dir = Path.Combine(OutDir, $"p{pair:00}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.Append(string.Join(',', headers.Select(Csv))).Append('\n');
        foreach (var row in rows)
        {
            if (row.Length != headers.Length) throw new Exception($"p{pair} {side} width");
            sb.Append(string.Join(',', row.Select(Csv))).Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, side + ".csv"), sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"p{pair:00} {side} {rows.Count}");
    }

    static List<decimal> Split(Random r, decimal total, int k, decimal step)
    {
        long units = (long)(total / step);
        if (k > units) k = (int)units;
        var cuts = new SortedSet<long>();
        while (cuts.Count < k - 1) cuts.Add(r.NextInt64(1, units));
        var parts = new List<decimal>(); long prev = 0;
        foreach (var c in cuts) { parts.Add((c - prev) * step); prev = c; }
        parts.Add((units - prev) * step);
        return parts;
    }

    static int PickK(Random r, int[] weights)
    {
        int t = weights.Sum(), x = r.Next(t), acc = 0;
        for (int i = 0; i < weights.Length; i++) { acc += weights[i]; if (x < acc) return i + 1; }
        return 1;
    }

    // P01: renamed/reordered columns, en-US vs de-DE, B/S vs BUY/SELL
    static void P01()
    {
        var r = new Random(1001); var pool = Pool(r, 300); var ts = Trades(r, pool, 1000);
        var L = new List<string[]>(); var R = new List<string[]>();
        foreach (var t in ts)
        {
            var id = "T" + t.N.ToString("000000", Inv);
            L.Add([id, t.Acct, t.S.Isin, t.Buy ? "B" : "S", F(t.Qty, 0, US, true), F(t.Px, 4, US, false), t.S.Ccy, D(t.TD, "yyyy-MM-dd", Inv), F(t.Net, 2, US, true), Desks[r.Next(Desks.Length)], Comments[r.Next(Comments.Length)]]);
            R.Add([id, D(t.TD, "dd.MM.yyyy", Inv), t.Buy ? "BUY" : "SELL", F(t.Qty, 0, DE, true), F(t.Px, 4, DE, false), F(t.Net, 2, DE, true), t.S.Ccy, t.S.Isin, new[] { "XNYS", "XLON", "XETR", "OTC" }[r.Next(4)], "CONFIRMED"]);
        }
        Shuffle(R, r);
        Write(1, "left", ["Trade ID", "Account", "ISIN", "Buy/Sell", "Quantity", "Price", "Currency", "Trade Date", "Net Amount", "Desk", "Comment"], L);
        Write(1, "right", ["Confirm Ref", "Dealt On", "Dir", "Qty", "Px", "Total", "CCY", "ISIN", "Venue", "Status"], R);
    }

    // P02: id regex, case, unit scales
    static void P02()
    {
        var r = new Random(1002); var pool = Pool(r, 400); var ts = Trades(r, pool, 1000, 1000);
        var L = new List<string[]>(); var R = new List<string[]>();
        foreach (var t in ts)
        {
            L.Add(["TRD-" + t.N.ToString("0000000", Inv), t.S.Isin, t.Buy ? "B" : "S", t.Qty.ToString("0", Inv), F(t.Px, 4, Inv, false), F(t.Net, 2, Inv, false), D(t.TD, "yyyyMMdd", Inv), t.S.Ccy, t.Acct]);
            string isinR = t.S.Isin.ToLowerInvariant();
            isinR = r.Next(3) switch { 0 => isinR.Insert(2, "-"), 1 => isinR.Insert(2, " ").Insert(7, " "), _ => isinR };
            R.Add(["trd/" + t.N.ToString(Inv), isinR, t.Buy ? "BOT" : "SLD", Thou(t.Qty / 1000m), (t.Px * 100m).ToString("F2", Inv), Thou(t.Net / 1000m), D(t.TD, "d-MMM-yyyy", US), t.S.Ccy.ToLowerInvariant(), "Y"]);
        }
        Shuffle(R, r);
        Write(2, "left", ["TradeId", "Isin", "Side", "Shares", "Px", "Net", "TradeDate", "Ccy", "Book"], L);
        Write(2, "right", ["Ref", "Security", "Action", "Size (000s)", "Px (minor)", "Proceeds (000s)", "Dealt", "ccy", "Allocated"], R);
    }

    // P03: cash entries: sign via D/C, minor units, percent, ids
    static void P03()
    {
        var r = new Random(1003);
        var L = new List<string[]>(); var R = new List<string[]>();
        string[] ccy = ["USD", "EUR", "GBP", "CHF", "JPY"];
        string[] memos = ["Wire in", "Wire out", "Fee, monthly", "Coupon", "Margin call", "Interest", "FX settle", "Div"];
        int n = 400000;
        for (int i = 0; i < 1000; i++)
        {
            n += r.Next(1, 40);
            int year = 2024;
            var date = Bd(r);
            decimal amt = Math.Round((decimal)Math.Pow(10, 2 + 4 * r.NextDouble()) * (r.Next(2) == 0 ? 1 : -1), 2);
            if (amt == 0) amt = 10.01m;
            decimal pct = Math.Round((decimal)(r.NextDouble() * 9), 2);
            decimal rate = pct / 100m;
            string c = ccy[r.Next(ccy.Length)];
            string memo = memos[r.Next(memos.Length)];
            var seq = n.ToString("000000", Inv);
            L.Add(["GL-" + year + "-" + seq, D(date, "yyyy-MM-dd", Inv), F(amt, 2, US, true), c, F(rate, 4, US, false), Parties[r.Next(Parties.Length)], memo]);
            decimal minor = Math.Abs(amt) * 100m;
            R.Add([D(date, "M/d/yyyy", US), year + "/" + seq, minor.ToString("0", Inv), amt < 0 ? "D" : "C", c, F(pct, 2, US, false), memo]);
        }
        Shuffle(R, r);
        Write(3, "left", ["Entry ID", "Post Date", "Amount", "Currency", "Rate", "Counterparty", "Memo"], L);
        Write(3, "right", ["Value Date", "Bank Ref", "Minor Units", "D/C", "Ccy", "Rate %", "Narrative"], R);
    }

    // P04: many fills -> one order
    static void P04()
    {
        var r = new Random(1004); var pool = Pool(r, 350);
        var L = new List<string[]>(); var R = new List<string[]>();
        int n = 5000, fill = 700000;
        for (int i = 0; i < 600; i++)
        {
            n += r.Next(1, 7);
            var t = NewTrade(r, pool, n);
            int k = PickK(r, [60, 25, 10, 3, 2]);
            decimal total = Math.Ceiling(Math.Max(t.Qty, k * 10m) / 10m) * 10m;
            var parts = Split(r, total, k, 10m);
            decimal orderVal = 0; var fr = new List<string[]>();
            foreach (var p in parts)
            {
                fill += r.Next(1, 5);
                decimal px = Math.Round(t.Px * (decimal)(0.995 + 0.01 * r.NextDouble()), 4);
                decimal amt = Math.Round(p * px, 2, MidpointRounding.AwayFromZero);
                orderVal += amt;
                fr.Add(["F" + fill.ToString(Inv), "ord" + t.N.ToString(Inv), t.S.Isin, t.Buy ? "B" : "S", p.ToString("0", Inv), amt.ToString("F2", FR), D(t.TD, "dd/MM/yyyy", Inv), new[] { "XNAS", "XNYS", "BATS", "ARCX" }[r.Next(4)], px.ToString("F4", FR)]);
            }
            R.AddRange(fr);
            L.Add(["ORD-" + t.N.ToString("0000000", Inv), t.S.Isin, t.Buy ? "Buy" : "Sell", F(total, 0, US, true), F(orderVal, 2, US, true), D(t.TD, "yyyy-MM-dd", Inv), t.Acct]);
        }
        Shuffle(R, r);
        Write(4, "left", ["Order No", "Security", "Side", "Order Qty", "Order Value", "Trade Date", "Account"], L);
        Write(4, "right", ["Fill ID", "Parent Order", "ISIN", "B/S", "Fill Qty", "Fill Amount", "Exec Date", "Venue", "Fill Px"], R);
    }

    // P05: one block -> many allocations (many on the LEFT), lots, thousands, mixed date formats
    static void P05()
    {
        var r = new Random(1005); var pool = Pool(r, 350);
        var L = new List<string[]>(); var R = new List<string[]>();
        int blk = 0, al = 90000;
        string[] funds = ["Alpha", "Beta", "Gamma", "Delta", "Omega", "Sigma", "Kappa", "Theta"];
        while (L.Count < 1000)
        {
            blk += r.Next(1, 5);
            var t = NewTrade(r, pool, blk);
            int k = PickK(r, [25, 30, 20, 15, 6, 4]);
            decimal total = Math.Max(k, r.Next(k, 120)) * 100m;
            var parts = Split(r, total, k, 100m);
            decimal val = 0; var lf = new List<string[]>();
            string refL = "BLK-2024-" + blk.ToString("0000", Inv);
            foreach (var p in parts)
            {
                al += r.Next(1, 4);
                decimal amt = Math.Round(p * t.Px, 2, MidpointRounding.AwayFromZero);
                val += amt;
                string dl = r.Next(2) == 0 ? D(t.TD, "yyyy-MM-dd", Inv) : D(t.TD, "dd/MM/yyyy", Inv);
                lf.Add(["AL" + al.ToString(Inv), refL, funds[r.Next(funds.Length)], t.S.Isin, t.Buy ? "B" : "S", F(p, 0, US, true), F(amt, 2, US, true), dl]);
            }
            L.AddRange(lf);
            R.Add([D(t.TD, "yyyyMMdd", Inv), blk + "/2024", t.Buy ? "buy" : "sell", (total / 100m).ToString("0", Inv), Thou(val / 1000m), t.S.Isin]);
        }
        Shuffle(R, r);
        Write(5, "left", ["Alloc ID", "Block Ref", "Fund", "ISIN", "Side", "Alloc Qty", "Alloc Amount", "Trade Date"], L);
        Write(5, "right", ["Dealt", "Block", "Dir", "Lots", "Block Value (k)", "Instr"], R);
    }

    // P06: shared reference for some rows, natural key for the rest
    static void P06()
    {
        var r = new Random(1006); var pool = Pool(r, 400); var ts = Trades(r, pool, 1000, 100000);
        var keys = new HashSet<string>();
        var L = new List<string[]>(); var R = new List<string[]>();
        Trade? prevBoth = null;
        foreach (var t0 in ts)
        {
            var t = t0;
            int mode = r.Next(100);
            if (mode < 60 && prevBoth is not null && r.Next(100) < 14)
                t = t0 with { S = prevBoth.S, Buy = prevBoth.Buy, Qty = prevBoth.Qty, Px = prevBoth.Px, TD = prevBoth.TD };
            else
            {
                var nk = $"{t.S.Isin}|{t.Buy}|{t.Qty}|{t.TD}|{t.Px}";
                if (!keys.Add(nk)) throw new Exception("dup natural key");
            }
            if (mode < 60) prevBoth = t;
            bool lref = mode < 95, rref = mode < 60 || (mode >= 85 && mode < 95);
            L.Add([lref ? "TR-" + t.N.ToString(Inv) : "", t.S.Isin, t.Buy ? "BUY" : "SELL", F(t.Qty, 0, US, true), F(t.Px, 4, US, false), D(t.TD, "dd MMM yyyy", US), F(t.Net, 2, US, true)]);
            R.Add([rref ? t.N.ToString(Inv) : "", t.S.Isin, t.Buy ? "B" : "S", F(t.Qty, 0, DE, true), F(t.Px, 4, DE, false), D(t.TD, "yyyy.MM.dd", Inv), F(t.Net, 2, DE, true)]);
        }
        Shuffle(R, r);
        Write(6, "left", ["Our Ref", "ISIN", "Side", "Qty", "Price", "Trade Date", "Net"], L);
        Write(6, "right", ["Cpty Ref", "Security", "Action", "Size", "Px", "Trade Dt", "Proceeds"], R);
    }

    // P07: packages (swap legs) mixed with plain trades
    static void P07()
    {
        var r = new Random(1007);
        var L = new List<string[]>(); var R = new List<string[]>();
        string[] normalProd = ["EQ", "BOND", "ETF"]; string[] swapProd = ["IRS", "XCCY", "TRS"];
        int pkg = 0, n = 20000;
        for (int i = 0; i < 560; i++)
        {
            n += r.Next(1, 6);
            string prod = normalProd[r.Next(3)];
            decimal notional = r.Next(10, 5000) * 1000m;
            decimal net = Math.Round((decimal)(r.NextDouble() - 0.4) * notional / 10m, 2);
            var mat = Bd(r).AddDays(400);
            string deal = "TR-" + n.ToString(Inv);
            L.Add([deal, "", "", prod, F(notional, 0, US, true), F(net, 2, US, true), D(mat, "yyyy-MM-dd", Inv), Parties[r.Next(Parties.Length)]]);
            R.Add([deal.ToLowerInvariant(), prod, F(notional, 0, DE, true), F(net, 2, DE, true), D(mat, "dd.MM.yyyy", Inv)]);
        }
        for (int i = 0; i < 220; i++)
        {
            pkg += r.Next(1, 4); n += r.Next(1, 6);
            string prod = swapProd[r.Next(3)];
            decimal notional = r.Next(1, 300) * 100000m;
            decimal pay = Math.Round((decimal)r.NextDouble() * notional / 50m, 2);
            decimal rec = Math.Round((decimal)r.NextDouble() * notional / 50m, 2);
            var mat = Bd(r).AddDays(800);
            string deal = "SW-" + n.ToString("00000", Inv); string link = "PKG-" + pkg.ToString("0000", Inv);
            string party = Parties[r.Next(Parties.Length)];
            L.Add([deal, link, "P", prod, F(notional, 0, US, true), F(pay, 2, US, true), D(mat, "yyyy-MM-dd", Inv), party]);
            L.Add([deal, link, "R", prod, F(notional, 0, US, true), F(rec, 2, US, true), D(mat, "yyyy-MM-dd", Inv), party]);
            R.Add([deal.ToLowerInvariant(), prod, F(notional, 0, DE, true), F(pay - rec, 2, DE, true), D(mat, "dd.MM.yyyy", Inv)]);
        }
        Shuffle(L, r); Shuffle(R, r);
        Write(7, "left", ["Deal", "Pkg Link", "Leg", "Product", "Notional", "Leg Amount", "Maturity", "Party"], L);
        Write(7, "right", ["Trade Ref", "Product Type", "Notional", "Net Cash", "Maturity Date"], R);
    }

    // P08: lifecycle (NEW / AMEND / CANCEL) on left, final state on right
    static void P08()
    {
        var r = new Random(1008); var pool = Pool(r, 400);
        var L = new List<string[]>(); var R = new List<string[]>();
        int n = 100000, ev = 0;
        string Ev() { ev += r.Next(1, 4); return "E" + ev.ToString("000000", Inv); }
        string[] Row(string id, string type, string target, Trade t, decimal q, decimal px, string deal) =>
            [id, type, target, t.Party, deal, t.S.Isin, t.Buy ? "B" : "S", F(q, 0, US, true), F(px, 4, US, false), D(t.TD, "yyyy-MM-dd", Inv)];
        var scen = new List<int>();
        scen.AddRange(Enumerable.Repeat(0, 600)); scen.AddRange(Enumerable.Repeat(1, 120));
        scen.AddRange(Enumerable.Repeat(2, 40)); scen.AddRange(Enumerable.Repeat(3, 80));
        Shuffle(scen, r);
        foreach (var s in scen)
        {
            n += r.Next(1, 7);
            var t = NewTrade(r, pool, n);
            string deal = "D" + n.ToString(Inv);
            decimal wq = t.Qty + r.Next(1, 5) * 10m, wp = Math.Round(t.Px * 1.01m, 4);
            string id1 = Ev();
            switch (s)
            {
                case 0:
                    L.Add(Row(id1, "NEW", "", t, t.Qty, t.Px, deal)); break;
                case 1:
                    L.Add(Row(id1, "NEW", "", t, wq, t.Px, deal));
                    L.Add(Row(Ev(), "AMEND", id1, t, t.Qty, t.Px, deal)); break;
                case 2:
                    {
                        L.Add(Row(id1, "NEW", "", t, wq, wp, deal));
                        string id2 = Ev(); L.Add(Row(id2, "AMEND", id1, t, wq, t.Px, deal));
                        L.Add(Row(Ev(), "AMEND", id2, t, t.Qty, t.Px, deal)); break;
                    }
                default:
                    L.Add(Row(id1, "NEW", "", t, t.Qty, t.Px, deal));
                    L.Add(Row(Ev(), "CANCEL", id1, t, t.Qty, t.Px, deal)); continue;
            }
            R.Add(["D-" + n.ToString(Inv), t.S.Isin, t.Buy ? "BUY" : "SELL", F(t.Qty, 0, DE, true), F(t.Px, 4, DE, false), D(t.TD, "yyyyMMdd", Inv)]);
        }
        Shuffle(L, r); Shuffle(R, r);
        Write(8, "left", ["Event ID", "Event Type", "Replaces", "Party", "Deal Ref", "ISIN", "Side", "Qty", "Price", "Trade Date"], L);
        Write(8, "right", ["Trade No", "Instrument", "B/S", "Quantity", "Price", "Booked On"], R);
    }

    // P09: routed asset classes, bond prices in 32nds, composite keys, no common id
    static void P09()
    {
        var r = new Random(1009); var pool = Pool(r, 500);
        var L = new List<string[]>(); var R = new List<string[]>();
        var eq = new HashSet<string>(); var bd = new HashSet<string>();
        int made = 0;
        while (made < 1000)
        {
            var t = NewTrade(r, pool, made + 1);
            bool bond = r.Next(100) < 38;
            if (!bond)
            {
                if (!eq.Add($"{t.S.Isin}|{t.Buy}|{t.Qty}|{t.TD}")) continue;
                L.Add([t.Acct, t.S.Isin, "EQ", t.Buy ? "B" : "S", F(t.Qty, 0, US, true), F(t.Px, 4, US, false), "", D(t.TD, "yyyy-MM-dd", Inv), D(t.SD, "yyyy-MM-dd", Inv), Desks[r.Next(Desks.Length)], "TR" + r.Next(10, 99).ToString(Inv)]);
                R.Add([t.S.Isin, "Equity", t.Buy ? "BUY" : "SELL", F(t.Qty, 0, DE, true), F(t.Px, 4, DE, false), "", D(t.TD, "yyyyMMdd", Inv), D(t.SD, "dd-MM-yy", Inv), Parties[r.Next(Parties.Length)]]);
            }
            else
            {
                decimal face = r.Next(1, 500) * 10000m;
                int whole = r.Next(80, 125), sixtyfourths = r.Next(0, 64);
                decimal px = whole + sixtyfourths / 64m;
                string s32 = sixtyfourths % 2 == 0 ? $"{whole}-{sixtyfourths / 2:00}" : $"{whole}-{(sixtyfourths - 1) / 2:00}+";
                if (!bd.Add($"{t.S.Isin}|{t.Buy}|{face}|{t.SD}")) continue;
                decimal acc = Math.Round(face * (decimal)(r.NextDouble() * 0.02), 2);
                L.Add([t.Acct, t.S.Isin, "BD", t.Buy ? "B" : "S", F(face, 0, US, true), F(px, 6, US, false), F(acc, 2, US, true), D(t.TD, "yyyy-MM-dd", Inv), D(t.SD, "yyyy-MM-dd", Inv), Desks[r.Next(Desks.Length)], "TR" + r.Next(10, 99).ToString(Inv)]);
                R.Add([t.S.Isin, "Fixed Income", t.Buy ? "BUY" : "SELL", F(face, 0, DE, true), s32, F(acc, 2, DE, true), D(t.TD, "yyyyMMdd", Inv), D(t.SD, "dd-MM-yy", Inv), Parties[r.Next(Parties.Length)]]);
            }
            made++;
        }
        Shuffle(R, r);
        Write(9, "left", ["Acct", "ISIN", "Asset Class", "Side", "Qty", "Price", "Accrued", "Trade Date", "Settle Date", "Desk", "Trader"], L);
        Write(9, "right", ["Security ID", "Type", "Dir", "Face/Shares", "Clean Px", "Accrued Int", "TD", "SD", "Cpty"], R);
    }

    // P10: everything at once
    static void P10()
    {
        var r = new Random(1010); var pool = Pool(r, 600);
        var L = new List<string[]>(); var R = new List<string[]>();
        var keys = new HashSet<string>();
        int n = 30000, blk = 0, al = 0;
        string RDate(DateOnly d) => r.Next(10) < 7 ? D(d, "dd.MM.yyyy", Inv) : D(d, "yyyyMMdd", Inv);
        string[] ent = ["LUX01", "IRL02", "CAY03"];
        string[] LRow(string rf, string bk, Trade t, decimal q, decimal px, decimal netAbs) =>
            [rf, bk, t.S.Isin, t.Buy ? "B" : "S", F(q, 0, US, true), F(px, 4, US, false), F(t.Buy ? -netAbs : netAbs, 2, US, true), D(t.TD, "yyyy-MM-dd", Inv), ent[r.Next(3)]];
        string[] RRow(string bk, string rf, Trade t, decimal q, decimal px, decimal netAbs) =>
            [bk, rf, t.S.Isin.ToLowerInvariant(), t.Buy ? "BUY" : "SELL", F(q, 0, DE, true), F(px, 4, DE, false), F(netAbs * 100m, 0, DE, true), t.Buy ? "D" : "C", RDate(t.TD), new[] { "XNYS", "XLON", "XETR", "OTC" }[r.Next(4)]];
        void Plain(bool refOnRight)
        {
            n += r.Next(1, 6);
            Trade t;
            string nk;
            do { t = NewTrade(r, pool, n); nk = $"{t.S.Isin}|{t.Buy}|{t.Qty}|{t.TD}|{t.Px}"; } while (!keys.Add(nk));
            L.Add(LRow("TR-" + n.ToString("0000000", Inv), "", t, t.Qty, t.Px, t.Net));
            R.Add(RRow("", refOnRight ? "tr/" + n.ToString(Inv) : "", t, t.Qty, t.Px, t.Net));
        }
        for (int i = 0; i < 380; i++) Plain(true);
        for (int i = 0; i < 250; i++) Plain(false);
        for (int i = 0; i < 120; i++)
        {
            blk += r.Next(1, 5); n += r.Next(1, 6);
            var t = NewTrade(r, pool, n);
            int k = PickK(r, [0, 30, 35, 20, 10, 5]);
            decimal total = Math.Max(r.Next(k, 80) * 10m, k * 10m);
            var parts = Split(r, total, k, 10m);
            decimal val = 0;
            foreach (var p in parts)
            {
                al += r.Next(1, 4);
                decimal amt = Math.Round(p * t.Px, 2, MidpointRounding.AwayFromZero);
                val += amt;
                L.Add(LRow("AL-" + al.ToString(Inv), "BLK" + blk.ToString("0000", Inv), t, p, t.Px, amt));
            }
            R.Add(RRow("blk-" + blk.ToString(Inv), "", t, total, t.Px, val));
        }
        Shuffle(R, r);
        Write(10, "left", ["Ref", "Block", "ISIN", "Side", "Qty", "Price", "Net Cash", "Trade Date", "Entity"], L);
        Write(10, "right", ["Block ID", "Cpty Ref", "ISIN Code", "Buy/Sell", "Qty", "Px", "Cash (cents)", "D/C", "Settled", "Venue"], R);
    }
}
