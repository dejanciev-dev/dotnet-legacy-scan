namespace LegacyScan;

public enum Severity
{
    Info = 0,
    Minor = 1,
    Major = 2,
    Blocker = 3,
}

public enum ProjectKind
{
    ClassLibrary,
    Console,
    WebForms,
    AspNetMvc,
    WebApi,
    WcfService,
    WinForms,
    Wpf,
    Test,
    Unknown,
}

/// <summary>A rule from the catalogue (see <see cref="Rules"/>).</summary>
/// <param name="SolutionWide">Base hours are a one-off for the whole solution (e.g. moving users to Identity), not per project.</param>
public sealed record Rule(
    string Id,
    string Title,
    Severity Severity,
    string Category,
    double BaseHours,
    double HoursPerOccurrence,
    string Guidance,
    bool SolutionWide = false);

/// <summary>One location where a rule matched.</summary>
/// <param name="FromSource">The snippet is a line of the scanned code (hidden with --no-snippets), not text made by the tool.</param>
public sealed record Occurrence(string File, int Line, string Snippet, bool FromSource = false);

/// <summary>A rule that matched in a project, with all its occurrences.</summary>
public sealed class Finding
{
    public required Rule Rule { get; init; }
    public List<Occurrence> Occurrences { get; } = [];
    public int Count => Math.Max(1, Occurrences.Count);
    /// <summary>False when a solution-wide rule's base hours are already charged to another project.</summary>
    public bool ChargeBaseHours { get; set; } = true;
    public double EstimatedHours => (ChargeBaseHours ? Rule.BaseHours : 0) + Rule.HoursPerOccurrence * Count;
}

public sealed class ProjectReport
{
    /// <summary>Project file name; made unique by <see cref="Scanner"/> when two projects share it.</summary>
    public required string Name { get; set; }
    public required string Path { get; init; }
    public string Language { get; set; } = "C#";
    public ProjectKind Kind { get; set; } = ProjectKind.Unknown;
    public bool IsSdkStyle { get; set; }
    public string TargetFramework { get; set; } = "unknown";
    public int SourceFiles { get; set; }
    public int LinesOfCode { get; set; }
    public List<string> ProjectReferences { get; } = [];
    /// <summary>Full paths of <see cref="ProjectReferences"/> (same order), used to resolve references to renamed projects.</summary>
    internal List<string> ProjectReferencePaths { get; } = [];
    public List<string> Packages { get; } = [];
    public List<Finding> Findings { get; } = [];
    public string RecommendedTarget { get; set; } = "";
    public double EstimatedHours { get; set; }
    public Severity WorstSeverity => Findings.Count == 0 ? Severity.Info : Findings.Max(f => f.Rule.Severity);
    public bool IsModernAlready { get; set; }
}

public sealed class ScanReport
{
    public required string Input { get; init; }
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.Now;
    public string ToolVersion { get; init; } = GetToolVersion();
    public List<ProjectReport> Projects { get; } = [];
    public List<string> MigrationOrder { get; set; } = [];
    public double EstimatedHoursLow { get; set; }
    public double EstimatedHoursHigh { get; set; }
    public string Difficulty { get; set; } = "";
    public int Score { get; set; }
    public List<string> Warnings { get; } = [];

    public int TotalLoc => Projects.Sum(p => p.LinesOfCode);

    /// <summary>Blocker rules across all projects, most expensive first.</summary>
    public List<(Rule Rule, int Count, List<string> ProjectNames)> BlockerGroups() =>
        Projects
            .SelectMany(p => p.Findings.Where(f => f.Rule.Severity == Severity.Blocker).Select(f => (p, f)))
            .GroupBy(x => x.f.Rule)
            .OrderByDescending(g => g.Sum(x => x.f.EstimatedHours))
            .Select(g => (g.Key, g.Sum(x => x.f.Count), g.Select(x => x.p.Name).Distinct().ToList()))
            .ToList();

    /// <summary>Package version including prerelease (0.2.0-beta.1), without the "+commit" build metadata.</summary>
    internal static string GetToolVersion()
    {
        var assembly = typeof(ScanReport).Assembly;
        var informational = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion;
        return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
    public int CountBySeverity(Severity s) => Projects.SelectMany(p => p.Findings).Count(f => f.Rule.Severity == s);
}

public sealed class ReportOptions
{
    public string? Brand { get; set; }
    public string? Contact { get; set; }
    public double HoursPerDay { get; set; } = 6;
    /// <summary>False with --no-snippets: lines of the scanned code are left out so the report can be shared.</summary>
    public bool IncludeSnippets { get; set; } = true;

    internal string? SnippetFor(Occurrence o) => o.FromSource && !IncludeSnippets ? null : o.Snippet;
}
