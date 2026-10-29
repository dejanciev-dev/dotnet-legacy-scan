namespace LegacyScan;

/// <summary>Turns findings into an effort range, a difficulty rating and a migration order.</summary>
public static class Estimator
{
    /// <summary>Per non-modern project: build fixes, config, deployment changes.</summary>
    public const double BaseHoursPerProject = 3;

    /// <summary>Regression testing / stabilisation overhead on top of the code work.</summary>
    public const double TestingOverhead = 0.30;

    public const double LowFactor = 0.75;
    public const double HighFactor = 1.6;

    public static void Estimate(ScanReport report)
    {
        foreach (var p in report.Projects)
        {
            var hours = p.Findings.Sum(f => f.EstimatedHours);
            if (!p.IsModernAlready) hours += BaseHoursPerProject;
            p.EstimatedHours = Math.Round(hours, 1);
        }

        var total = report.Projects.Sum(p => p.EstimatedHours) * (1 + TestingOverhead);
        report.EstimatedHoursLow = Math.Round(total * LowFactor);
        report.EstimatedHoursHigh = Math.Round(total * HighFactor);

        var blockerTypes = report.Projects.SelectMany(p => p.Findings).Where(f => f.Rule.Severity == Severity.Blocker).Select(f => f.Rule.Id).Distinct().Count();
        report.Score = (int)Math.Clamp(Math.Round(22 * Math.Log10(1 + total) + 4 * blockerTypes), 0, 100);
        report.Difficulty = total switch
        {
            <= 0 => "None",
            <= 80 when blockerTypes == 0 => "Low",
            <= 320 => "Medium",
            <= 1200 => "High",
            _ => "Very high",
        };

        report.MigrationOrder = Order(report.Projects);
    }

    /// <summary>Dependencies first (topological), easier projects first among equals. Cycles are appended at the end.</summary>
    public static List<string> Order(List<ProjectReport> projects)
    {
        var byName = projects.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var remaining = new HashSet<string>(byName.Keys, StringComparer.OrdinalIgnoreCase);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Select(n => byName[n])
                .Where(p => p.ProjectReferences.All(r => !byName.ContainsKey(r) || done.Contains(r)))
                .OrderBy(p => p.Kind == ProjectKind.Test ? 1 : 0)
                .ThenBy(p => p.WorstSeverity)
                .ThenBy(p => p.EstimatedHours)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ready.Count == 0)
            {
                order.AddRange(remaining.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => n + " (circular reference)"));
                break;
            }
            var next = ready[0];
            order.Add(next.Name);
            done.Add(next.Name);
            remaining.Remove(next.Name);
        }
        return order;
    }
}
