using System.Globalization;
using System.Text;
using static System.Net.WebUtility;

namespace LegacyScan;

/// <summary>Self-contained, printable HTML report aimed at both developers and decision makers.</summary>
public static class HtmlReport
{
    private const int MaxOccurrencesShown = 25;

    public static string Render(ScanReport r, ReportOptions o)
    {
        var sb = new StringBuilder();
        var title = Path.GetFileName(r.Input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string E(string? s) => HtmlEncode(s ?? "");
        string Days(double h) => Cli.Days(h, o);

        sb.Append($$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Migration readiness – {{E(title)}}</title>
            <style>{{Css}}</style>
            </head>
            <body>
            <main>
            <header>
              <p class="eyebrow">.NET Framework → modern .NET</p>
              <h1>Migration readiness report</h1>
              <p class="muted">{{E(title)}} · scanned {{r.ScannedAt.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)}} · dotnet-legacy-scan {{E(r.ToolVersion)}}</p>
            </header>
            """);

        // ---------- Summary ----------
        var blockers = r.CountBySeverity(Severity.Blocker);
        sb.Append($$"""
            <section class="cards">
              <div class="card"><span class="label">Difficulty</span><span class="value diff-{{E(r.Difficulty.Replace(' ', '-').ToLowerInvariant())}}">{{E(r.Difficulty)}}</span><span class="sub">complexity {{r.Score}}/100</span></div>
              <div class="card"><span class="label">Estimated effort</span><span class="value">{{Days(r.EstimatedHoursLow)}}–{{Days(r.EstimatedHoursHigh)}}</span><span class="sub">developer days ({{o.HoursPerDay.ToString(CultureInfo.InvariantCulture)}}h/day)</span></div>
              <div class="card"><span class="label">Projects</span><span class="value">{{r.Projects.Count}}</span><span class="sub">{{r.Projects.Count(p => p.IsModernAlready)}} already on modern .NET</span></div>
              <div class="card"><span class="label">Code</span><span class="value">{{r.TotalLoc.ToString("N0", CultureInfo.InvariantCulture)}}</span><span class="sub">lines in {{r.Projects.Sum(p => p.SourceFiles)}} files</span></div>
              <div class="card"><span class="label">Findings</span><span class="value">{{blockers}} <small>blocker</small></span><span class="sub">{{r.CountBySeverity(Severity.Major)}} major · {{r.CountBySeverity(Severity.Minor)}} minor</span></div>
            </section>
            """);

        foreach (var w in r.Warnings) sb.Append($"<p class=\"warn\">⚠ {E(w)}</p>");

        // ---------- Key blockers ----------
        var blockerGroups = r.Projects
            .SelectMany(p => p.Findings.Where(f => f.Rule.Severity == Severity.Blocker).Select(f => (p, f)))
            .GroupBy(x => x.f.Rule)
            .OrderByDescending(g => g.Sum(x => x.f.EstimatedHours))
            .ToList();
        if (blockerGroups.Count > 0)
        {
            sb.Append("<section><h2>What makes this migration hard</h2><ul class=\"blockers\">");
            foreach (var g in blockerGroups)
            {
                var count = g.Sum(x => x.f.Count);
                sb.Append($"<li><strong>{E(g.Key.Title)}</strong> <span class=\"muted\">×{count} in {E(string.Join(", ", g.Select(x => x.p.Name).Distinct()))}</span><p>{E(g.Key.Guidance)}</p></li>");
            }
            sb.Append("</ul></section>");
        }

        // ---------- Order ----------
        var byName = r.Projects.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        sb.Append("<section><h2>Recommended migration order</h2><p class=\"muted\">Dependencies first. Shared libraries are multi-targeted so the old and the new application can run side by side during the migration.</p><ol class=\"order\">");
        foreach (var name in r.MigrationOrder)
        {
            byName.TryGetValue(name.Replace(" (circular reference)", ""), out var p);
            sb.Append($"<li><strong>{E(name)}</strong>");
            if (p is not null) sb.Append($" <span class=\"kind\">{E(KindLabel(p.Kind))}</span><br><span class=\"muted\">{E(p.RecommendedTarget)}</span>");
            sb.Append("</li>");
        }
        sb.Append("</ol></section>");

        // ---------- Projects table ----------
        sb.Append("""
            <section><h2>Projects</h2><div class="table-wrap"><table>
            <thead><tr><th>Project</th><th>Type</th><th>Target</th><th>Format</th><th class="num">Lines</th><th>Worst</th><th class="num">Effort (days)</th></tr></thead><tbody>
            """);
        foreach (var p in r.Projects.OrderByDescending(p => p.EstimatedHours))
        {
            sb.Append($"<tr><td><a href=\"#p-{Anchor(p.Name)}\">{E(p.Name)}</a><br><span class=\"muted small\">{E(p.Language)}</span></td>" +
                      $"<td>{E(KindLabel(p.Kind))}</td><td><code>{E(p.TargetFramework)}</code></td><td>{(p.IsSdkStyle ? "SDK" : "Legacy")}</td>" +
                      $"<td class=\"num\">{p.LinesOfCode.ToString("N0", CultureInfo.InvariantCulture)}</td><td>{Badge(p.IsModernAlready && p.Findings.Count == 0 ? null : p.WorstSeverity)}</td>" +
                      $"<td class=\"num\">{(p.EstimatedHours > 0 ? Days(p.EstimatedHours) : "–")}</td></tr>");
        }
        sb.Append("</tbody></table></div></section>");

        // ---------- Details ----------
        sb.Append("<section><h2>Findings by project</h2>");
        foreach (var p in r.Projects.OrderByDescending(p => p.EstimatedHours))
        {
            sb.Append($"<article id=\"p-{Anchor(p.Name)}\" class=\"project\"><h3>{E(p.Name)} <span class=\"kind\">{E(KindLabel(p.Kind))}</span></h3>");
            sb.Append($"<p class=\"muted small\">{E(p.Path)} · <code>{E(p.TargetFramework)}</code> · {p.ProjectReferences.Count} project refs · {p.Packages.Count} packages</p>");
            sb.Append($"<p><strong>Recommendation:</strong> {E(p.RecommendedTarget)}</p>");
            if (p.Findings.Count == 0)
            {
                sb.Append("<p class=\"ok\">No migration issues found.</p></article>");
                continue;
            }
            foreach (var f in p.Findings)
            {
                sb.Append($"<details class=\"finding\"><summary>{Badge(f.Rule.Severity)} <span class=\"ftitle\">{E(f.Rule.Title)}</span>" +
                          $"<span class=\"meta\">{(f.Occurrences.Count > 0 ? $"×{f.Occurrences.Count} · " : "")}~{f.EstimatedHours.ToString("0.#", CultureInfo.InvariantCulture)}h · {E(f.Rule.Id)}</span></summary>");
                sb.Append($"<p>{E(f.Rule.Guidance)}</p>");
                if (f.Occurrences.Count > 0)
                {
                    sb.Append("<ul class=\"occ\">");
                    foreach (var oc in f.Occurrences.Take(MaxOccurrencesShown))
                        sb.Append($"<li><code>{E(oc.File)}{(oc.Line > 1 ? ":" + oc.Line : "")}</code> {(oc.Snippet != Path.GetFileName(oc.File) ? $"<span class=\"snip\">{E(oc.Snippet)}</span>" : "")}</li>");
                    if (f.Occurrences.Count > MaxOccurrencesShown) sb.Append($"<li class=\"muted\">… and {f.Occurrences.Count - MaxOccurrencesShown} more</li>");
                    sb.Append("</ul>");
                }
                sb.Append("</details>");
            }
            sb.Append("</article>");
        }
        sb.Append("</section>");

        // ---------- Method ----------
        sb.Append($$"""
            <section class="method"><h2>How this estimate is made</h2>
            <p>dotnet-legacy-scan reads project files, configuration, NuGet packages and source code (comments and strings excluded) and matches them against {{Rules.All.Count()}} migration rules.
            Each rule carries a heuristic effort. Totals add {{Estimator.BaseHoursPerProject}}h per project and {{(Estimator.TestingOverhead * 100).ToString("0", CultureInfo.InvariantCulture)}}% for regression testing; the range is ×{{Estimator.LowFactor.ToString(CultureInfo.InvariantCulture)}} to ×{{Estimator.HighFactor.ToString(CultureInfo.InvariantCulture)}}.
            It is a starting point for planning, not a quote. UI redesign, new features, infrastructure and deployment changes are not included.</p>
            <p>The scan runs locally and read-only. No code leaves the machine.</p>
            </section>
            """);

        // ---------- Footer ----------
        sb.Append("<footer>");
        if (!string.IsNullOrWhiteSpace(o.Brand) || !string.IsNullOrWhiteSpace(o.Contact))
        {
            sb.Append("<div class=\"cta\"><strong>Need help with this migration?</strong> ");
            if (!string.IsNullOrWhiteSpace(o.Brand)) sb.Append(E(o.Brand)).Append(' ');
            if (!string.IsNullOrWhiteSpace(o.Contact))
            {
                var c = o.Contact!;
                var href = c.Contains('@') && !c.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "mailto:" + c : c.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? c : null;
                sb.Append(href is null ? $"· {E(c)}" : $"· <a href=\"{E(href)}\">{E(c)}</a>");
            }
            sb.Append("</div>");
        }
        sb.Append("<p class=\"muted small\">Generated by <a href=\"https://github.com/dejanciev-dev/dotnet-legacy-scan\">dotnet-legacy-scan</a>.</p></footer></main></body></html>");
        return sb.ToString();
    }

    private static string Badge(Severity? s) => s switch
    {
        null => "<span class=\"badge ok\">OK</span>",
        Severity.Blocker => "<span class=\"badge blocker\">Blocker</span>",
        Severity.Major => "<span class=\"badge major\">Major</span>",
        Severity.Minor => "<span class=\"badge minor\">Minor</span>",
        _ => "<span class=\"badge info\">Info</span>",
    };

    private static string Anchor(string s) => new(s.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());

    public static string KindLabel(ProjectKind k) => k switch
    {
        ProjectKind.ClassLibrary => "Class library",
        ProjectKind.Console => "Console / service",
        ProjectKind.WebForms => "ASP.NET Web Forms",
        ProjectKind.AspNetMvc => "ASP.NET MVC",
        ProjectKind.WebApi => "ASP.NET Web API",
        ProjectKind.WcfService => "WCF service",
        ProjectKind.WinForms => "WinForms",
        ProjectKind.Wpf => "WPF",
        ProjectKind.Test => "Tests",
        _ => "Unknown",
    };

    private const string Css = """
        :root{--bg:#f7f7f5;--surface:#fff;--text:#1d1d1f;--muted:#6b6b70;--border:#e3e3e0;--accent:#3d5afe;
          --blocker:#c62828;--blocker-bg:#fdecea;--major:#b25e00;--major-bg:#fff3e0;--minor:#5f6368;--minor-bg:#eef0f2;--ok:#1b7f3b;--ok-bg:#e6f4ea;color-scheme:light}
        @media (prefers-color-scheme:dark){:root{--bg:#141416;--surface:#1d1d20;--text:#ececef;--muted:#9a9aa2;--border:#2e2e33;--accent:#8c9eff;
          --blocker:#ff8a80;--blocker-bg:#3a1d1d;--major:#ffb74d;--major-bg:#3a2a14;--minor:#b0b4ba;--minor-bg:#2a2c30;--ok:#81c995;--ok-bg:#16301f;color-scheme:dark}}
        *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.55 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif}
        main{max-width:1080px;margin:0 auto;padding:40px 20px 64px}
        h1{font-size:30px;margin:4px 0 6px;letter-spacing:-.02em}h2{font-size:20px;margin:40px 0 12px}h3{font-size:17px;margin:0 0 4px}
        .eyebrow{text-transform:uppercase;letter-spacing:.08em;font-size:12px;color:var(--accent);margin:0;font-weight:600}
        .muted{color:var(--muted)}.small{font-size:13px}code{font:13px ui-monospace,SFMono-Regular,Consolas,monospace}
        a{color:var(--accent)}
        .cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px;margin-top:28px}
        .card{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:16px;display:flex;flex-direction:column;gap:2px}
        .card .label{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.05em}
        .card .value{font-size:26px;font-weight:650;font-variant-numeric:tabular-nums}.card .value small{font-size:14px;font-weight:500;color:var(--muted)}
        .card .sub{font-size:13px;color:var(--muted)}
        .diff-low{color:var(--ok)}.diff-medium{color:var(--major)}.diff-high,.diff-very-high{color:var(--blocker)}
        .warn{background:var(--major-bg);color:var(--major);padding:8px 12px;border-radius:8px}
        .blockers{list-style:none;padding:0;display:grid;gap:10px}.blockers li{background:var(--surface);border:1px solid var(--border);border-left:4px solid var(--blocker);border-radius:8px;padding:12px 14px}
        .blockers p{margin:4px 0 0;color:var(--muted);font-size:14px}
        .order{padding-left:22px}.order li{margin:8px 0}
        .kind{font-size:12px;border:1px solid var(--border);border-radius:999px;padding:1px 8px;color:var(--muted);font-weight:500;white-space:nowrap}
        .table-wrap{overflow-x:auto;background:var(--surface);border:1px solid var(--border);border-radius:12px}
        table{width:100%;border-collapse:collapse;font-size:14px}th,td{padding:10px 12px;text-align:left;border-bottom:1px solid var(--border);vertical-align:top}
        th{font-size:12px;text-transform:uppercase;letter-spacing:.04em;color:var(--muted);font-weight:600}tr:last-child td{border-bottom:0}
        .num{text-align:right;font-variant-numeric:tabular-nums}
        .badge{display:inline-block;font-size:11px;font-weight:650;padding:2px 8px;border-radius:999px;text-transform:uppercase;letter-spacing:.04em}
        .badge.blocker{background:var(--blocker-bg);color:var(--blocker)}.badge.major{background:var(--major-bg);color:var(--major)}
        .badge.minor,.badge.info{background:var(--minor-bg);color:var(--minor)}.badge.ok{background:var(--ok-bg);color:var(--ok)}
        .project{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:18px;margin:14px 0}
        .project p{margin:4px 0 10px}.ok{color:var(--ok)}
        .finding{border-top:1px solid var(--border);padding:8px 0}.finding summary{cursor:pointer;display:flex;gap:10px;align-items:center;flex-wrap:wrap}
        .finding .ftitle{font-weight:550}.finding .meta{margin-left:auto;color:var(--muted);font-size:13px;font-variant-numeric:tabular-nums}
        .finding p{color:var(--muted);font-size:14px}
        .occ{margin:0 0 6px;padding-left:18px;font-size:13px}.occ li{margin:2px 0;overflow-wrap:anywhere}.snip{color:var(--muted)}
        .method{font-size:14px;color:var(--muted)}.method h2{color:var(--text)}
        footer{margin-top:40px;border-top:1px solid var(--border);padding-top:18px}
        .cta{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:16px;font-size:15px}
        @media print{body{background:#fff}details{display:block}details>*{display:block}.finding summary{list-style:none}.card,.project,.table-wrap{break-inside:avoid}}
        @media (max-width:600px){main{padding:24px 16px 48px}h1{font-size:24px}.finding .meta{margin-left:0;width:100%}}
        """;
}
