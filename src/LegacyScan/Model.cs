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
public sealed record Rule(
    string Id,
    string Title,
    Severity Severity,
    string Category,
    double BaseHours,
    double HoursPerOccurrence,
    string Guidance);

/// <summary>One location where a rule matched.</summary>
public sealed record Occurrence(string File, int Line, string Snippet);

/// <summary>A rule that matched in a project, with all its occurrences.</summary>
public sealed class Finding
{
    public required Rule Rule { get; init; }
    public List<Occurrence> Occurrences { get; } = [];
    public int Count => Math.Max(1, Occurrences.Count);
    public double EstimatedHours => Rule.BaseHours + Rule.HoursPerOccurrence * Count;
}

public sealed class ProjectReport
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string Language { get; set; } = "C#";
    public ProjectKind Kind { get; set; } = ProjectKind.Unknown;
    public bool IsSdkStyle { get; set; }
    public string TargetFramework { get; set; } = "unknown";
    public int SourceFiles { get; set; }
    public int LinesOfCode { get; set; }
    public List<string> ProjectReferences { get; } = [];
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
    public string ToolVersion { get; init; } = typeof(ScanReport).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public List<ProjectReport> Projects { get; } = [];
    public List<string> MigrationOrder { get; set; } = [];
    public double EstimatedHoursLow { get; set; }
    public double EstimatedHoursHigh { get; set; }
    public string Difficulty { get; set; } = "";
    public int Score { get; set; }
    public List<string> Warnings { get; } = [];

    public int TotalLoc => Projects.Sum(p => p.LinesOfCode);
    public int CountBySeverity(Severity s) => Projects.SelectMany(p => p.Findings).Count(f => f.Rule.Severity == s);
}

public sealed class ReportOptions
{
    public string? Brand { get; set; }
    public string? Contact { get; set; }
    public double HoursPerDay { get; set; } = 6;
}
