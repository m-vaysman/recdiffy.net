using System.Data;
using System.Net;
using RecDiffy.Extensions;

namespace RecDiffy.PublicDataDemo;

/// <summary>One tab of the page: its id, its label and its HTML.</summary>
internal sealed record Tab(string Id, string Label, string Title, string Html);

/// <summary>The page: a header, one tab per reconciliation, and a footer. Tabs switch with CSS alone, so the page needs no script.</summary>
internal static class Page
{
    /// <summary>A grid in a scroll box; <paramref name="call"/> names the RecDiffy.Extensions call that produced it, when one did.</summary>
    public static string Grid(DataTable table, string? call = null)
    {
        var label = call is { Length: > 0 } ? $"<p class=\"call\"><code>{WebUtility.HtmlEncode(call)}</code></p>" : "";
        return $"{label}<div class=\"scroll\">{table.ToDataGridHtml("")}</div>";
    }

    public static string Code(string code) => $"<pre><code>{WebUtility.HtmlEncode(code)}</code></pre>";

    public static string Render(IReadOnlyList<Tab> tabs, DateTimeOffset generated)
    {
        var inputs = string.Concat(tabs.Select((tab, index) =>
            $"""<input type="radio" name="tab" id="tab-{tab.Id}"{(index is 0 ? " checked" : "")}><label for="tab-{tab.Id}">{WebUtility.HtmlEncode(tab.Label)}</label>"""));
        var panels = string.Concat(tabs.Select(tab =>
            $"""<section class="panel" id="panel-{tab.Id}"><h2>{WebUtility.HtmlEncode(tab.Title)}</h2>{tab.Html}</section>"""));
        var showRules = string.Concat(tabs.Select(tab => $"#tab-{tab.Id}:checked ~ #panel-{tab.Id} {{ display: block; }}\n"));
        var activeRules = string.Join(",\n", tabs.Select(tab => $"#tab-{tab.Id}:checked + label"));

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>RecDiffy.NET live reconciliations</title>
            <meta name="description" content="RecDiffy.NET reconciles public FINRA and US Treasury data every business day.">
            <style>
            :root { --ink: #1f2937; --muted: #64748b; --line: #e2e8f0; --accent: #0f4c81; --paper: #fff; --wash: #f8fafc; }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--wash); color: var(--ink); font: 15px/1.55 'Segoe UI', system-ui, -apple-system, Arial, sans-serif; }
            header, main, footer { max-width: 1180px; margin: 0 auto; padding: 0 16px; }
            header { padding-top: 28px; }
            header h1 { margin: 0; font-size: 26px; letter-spacing: -0.01em; }
            header p { margin: 6px 0 0; color: var(--muted); max-width: 70ch; }
            header a { color: var(--accent); }
            header p.try { margin-top: 10px; color: var(--ink); }
            header p.try a { font-weight: 600; }
            main { margin-top: 22px; }
            input[name="tab"] { position: absolute; opacity: 0; pointer-events: none; }
            label { display: inline-block; padding: 9px 16px; margin-right: 4px; border: 1px solid var(--line); border-bottom: 0;
                    border-radius: 8px 8px 0 0; background: var(--wash); color: var(--muted); font-weight: 600; cursor: pointer; }
            {{activeRules}} { background: var(--paper); color: var(--ink); box-shadow: inset 0 3px 0 var(--accent); }
            input[name="tab"]:focus-visible + label { outline: 2px solid var(--accent); outline-offset: 2px; }
            .panel { display: none; background: var(--paper); border: 1px solid var(--line); border-radius: 0 8px 8px 8px; padding: 6px 22px 22px; }
            {{showRules}}
            .panel h2 { font-size: 20px; margin: 18px 0 6px; }
            .panel h3 { font-size: 15px; margin: 26px 0 8px; padding-top: 14px; border-top: 1px solid var(--line); }
            .lead { max-width: 80ch; margin: 0 0 16px; }
            .note { max-width: 80ch; color: var(--muted); margin: 0 0 10px; }
            .ok { color: #15803d; font-weight: 600; }
            .call { margin: 0 0 6px; }
            .call code { font: 12px Consolas, 'Cascadia Mono', monospace; color: var(--accent); background: #eef4fa; padding: 2px 6px; border-radius: 4px; }
            .scroll { overflow-x: auto; margin-bottom: 6px; }
            pre { background: #0f172a; color: #e2e8f0; padding: 14px 16px; border-radius: 6px; overflow-x: auto; font: 13px/1.5 Consolas, 'Cascadia Mono', monospace; }
            .source { color: var(--muted); font-size: 13px; margin-top: 18px; }
            .source a { color: var(--accent); }
            footer { color: var(--muted); font-size: 13px; padding-top: 18px; padding-bottom: 36px; }
            {{DataGrid.Css}}
            </style>
            </head>
            <body>
            <header>
            <h1>RecDiffy.NET, reconciling public data</h1>
            <p>Every business day, <a href="https://github.com/m-vaysman/recdiffy.net">RecDiffy.NET</a> reconciles two pairs of public datasets that
            should agree, and shows what ties, what doesn't, and why. Grids labelled with a call, such as <code>result.ToMatchTable()</code>, are
            tables the engine's result returns through RecDiffy.Extensions; the others are built from the same results.</p>
            <p class="try"><a href="try/">Try it with your own files</a>: reconcile two CSV or Excel files in your browser. Nothing is uploaded.</p>
            </header>
            <main>
            {{inputs}}
            {{panels}}
            </main>
            <footer>Generated {{generated.ToInvariant("yyyy-MM-dd HH:mm")}} UTC from public data. Not affiliated with FINRA or the US Treasury.</footer>
            </body>
            </html>
            """;
    }
}
