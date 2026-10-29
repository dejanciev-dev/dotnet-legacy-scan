using LegacyScan;

return Cli.Run(args);

internal static class Cli
{
    private const string Usage = """
        dotnet-legacy-scan – .NET Framework → modern .NET migration readiness scanner

        Usage:
          dotnet legacy-scan <path> [options]

          <path>               .sln, .slnx, .csproj, .vbproj or a directory

        Options:
          -o, --out <file>     HTML report path (default: legacy-scan-report.html)
          --json <file>        Also write a JSON report
          --md <file>          Also write a Markdown summary (e.g. "$GITHUB_STEP_SUMMARY" in GitHub Actions)
          --exclude <pattern>  Skip projects whose name or relative path matches (* and ?); repeatable
          --no-snippets        Leave source code lines out of the reports (safe to share)
          --brand <text>       Name shown in the report footer (e.g. your company)
          --contact <text>     Contact shown in the report footer (email or URL)
          --hours-per-day <n>  Productive hours per developer day (default: 6)
          --fail-on <level>    Exit with code 2 if a finding of this severity exists (minor|major|blocker)
          -q, --quiet          No console summary
          --version            Show the version
          -h, --help           Show help

        Nothing leaves your machine: the scan is local and read-only.
        """;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help" or "/?"))
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }
        if (args.Contains("--version"))
        {
            Console.WriteLine(ScanReport.GetToolVersion());
            return 0;
        }

        string? input = null, html = "legacy-scan-report.html", json = null, md = null;
        var exclude = new List<string>();
        var options = new ReportOptions();
        Severity? failOn = null;
        var quiet = false;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
                switch (args[i])
                {
                    case "-o" or "--out": html = Next(); break;
                    case "--json": json = Next(); break;
                    case "--md": md = Next(); break;
                    case "--exclude": exclude.Add(Next()); break;
                    case "--no-snippets": options.IncludeSnippets = false; break;
                    case "--brand": options.Brand = Next(); break;
                    case "--contact": options.Contact = Next(); break;
                    case "--hours-per-day":
                        options.HoursPerDay = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                        if (options.HoursPerDay is <= 0 or > 24) throw new ArgumentException("--hours-per-day must be between 0 and 24");
                        break;
                    case "--fail-on": failOn = Enum.Parse<Severity>(Next(), ignoreCase: true); break;
                    case "-q" or "--quiet": quiet = true; break;
                    default:
                        if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option {args[i]}");
                        if (input is not null) throw new ArgumentException("Only one input path is supported");
                        input = args[i];
                        break;
                }
            }
            if (input is null) throw new ArgumentException("Missing <path>");

            var report = Scanner.Scan(input, exclude);
            if (report.Projects.Count == 0)
            {
                // An empty report would say "Difficulty: None, 0 days", which reads like good news.
                foreach (var w in report.Warnings) Console.Error.WriteLine($"warning: {w}");
                Console.Error.WriteLine("error: no project could be analyzed, so no report was written.");
                return 1;
            }
            File.WriteAllText(html, HtmlReport.Render(report, options));
            if (json is not null) File.WriteAllText(json, JsonReport.Render(report, options));
            if (md is not null) File.WriteAllText(md, MarkdownReport.Render(report, options));

            if (!quiet) PrintSummary(report, options, html, json, md);

            if (failOn is { } level && report.Projects.SelectMany(p => p.Findings).Any(f => f.Rule.Severity >= level)) return 2;
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or FormatException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine("Run 'dotnet legacy-scan --help' for usage.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: unexpected {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine("Please report this at https://github.com/dejanciev-dev/dotnet-legacy-scan/issues");
            return 1;
        }
    }

    private static void PrintSummary(ScanReport r, ReportOptions o, string html, string? json, string? md)
    {
        Console.WriteLine();
        Console.WriteLine($"  Projects:     {r.Projects.Count} ({r.Projects.Count(p => p.IsModernAlready)} already modern)");
        Console.WriteLine($"  Code:         {r.TotalLoc:N0} lines");
        Console.WriteLine($"  Findings:     {r.CountBySeverity(Severity.Blocker)} blocker · {r.CountBySeverity(Severity.Major)} major · {r.CountBySeverity(Severity.Minor)} minor");
        Console.WriteLine($"  Difficulty:   {r.Difficulty} (complexity {r.Score}/100)");
        Console.WriteLine($"  Effort:       {Days(r.EstimatedHoursLow, o)}–{Days(r.EstimatedHoursHigh, o)} developer days (rough estimate)");
        foreach (var w in r.Warnings) Console.WriteLine($"  warning: {w}");
        Console.WriteLine();
        Console.WriteLine($"  Report:       {Path.GetFullPath(html)}");
        if (json is not null) Console.WriteLine($"  JSON:         {Path.GetFullPath(json)}");
        if (md is not null) Console.WriteLine($"  Markdown:     {Path.GetFullPath(md)}");
        Console.WriteLine();
    }

    internal static string Days(double hours, ReportOptions o) => Math.Ceiling(hours / o.HoursPerDay).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
