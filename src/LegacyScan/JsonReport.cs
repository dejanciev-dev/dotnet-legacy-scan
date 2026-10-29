using System.Text.Json;
using System.Text.Json.Serialization;

namespace LegacyScan;

public static class JsonReport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Render(ScanReport r, ReportOptions o) => JsonSerializer.Serialize(new
    {
        schema = "dotnet-legacy-scan/v1",
        r.ToolVersion,
        r.Input,
        r.ScannedAt,
        summary = new
        {
            projects = r.Projects.Count,
            modernProjects = r.Projects.Count(p => p.IsModernAlready),
            linesOfCode = r.TotalLoc,
            blockers = r.CountBySeverity(Severity.Blocker),
            major = r.CountBySeverity(Severity.Major),
            minor = r.CountBySeverity(Severity.Minor),
            r.Difficulty,
            complexityScore = r.Score,
            r.EstimatedHoursLow,
            r.EstimatedHoursHigh,
            hoursPerDay = o.HoursPerDay,
        },
        r.MigrationOrder,
        r.Warnings,
        projects = r.Projects.Select(p => new
        {
            p.Name,
            p.Path,
            p.Language,
            p.Kind,
            p.IsSdkStyle,
            p.IsModernAlready,
            p.TargetFramework,
            p.SourceFiles,
            p.LinesOfCode,
            p.EstimatedHours,
            p.RecommendedTarget,
            p.ProjectReferences,
            p.Packages,
            findings = p.Findings.Select(f => new
            {
                f.Rule.Id,
                f.Rule.Title,
                f.Rule.Severity,
                f.Rule.Category,
                count = f.Occurrences.Count,
                f.EstimatedHours,
                f.Rule.Guidance,
                occurrences = f.Occurrences,
            }),
        }),
    }, Options);
}
