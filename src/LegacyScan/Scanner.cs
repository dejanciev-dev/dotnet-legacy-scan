using System.Text.RegularExpressions;

namespace LegacyScan;

public static class Scanner
{
    /// <param name="exclude">Wildcard patterns (* and ?) matched against the project name or its path relative to the input.</param>
    public static ScanReport Scan(string input, IReadOnlyCollection<string>? exclude = null)
    {
        var report = new ScanReport { Input = Path.GetFullPath(input) };
        var projects = SolutionReader.FindProjects(input, report.Warnings);
        if (projects.Count == 0) report.Warnings.Add("No .csproj or .vbproj projects found.");

        var rootDir = Directory.Exists(report.Input) ? report.Input : Path.GetDirectoryName(report.Input)!;
        if (exclude is { Count: > 0 })
        {
            var patterns = exclude.Select(Wildcard).ToList();
            var excluded = projects.Where(p => patterns.Any(r =>
                r.IsMatch(Path.GetFileNameWithoutExtension(p)) || r.IsMatch(Path.GetRelativePath(rootDir, p).Replace('\\', '/')))).ToList();
            // Said in the report, so nobody reads a partial scan as the whole solution.
            if (excluded.Count > 0)
            {
                projects = projects.Except(excluded).ToList();
                report.Warnings.Add($"Excluded by --exclude ({excluded.Count}): {string.Join(", ", excluded.Select(Path.GetFileNameWithoutExtension))}");
            }
        }

        foreach (var shared in projects.GroupBy(p => Path.GetDirectoryName(p)!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            report.Warnings.Add($"{string.Join(" and ", shared.Select(Path.GetFileName))} share the folder {Path.GetRelativePath(rootDir, shared.Key)}; its files are counted for each of them.");

        foreach (var path in projects)
        {
            try
            {
                report.Projects.Add(ProjectAnalyzer.Analyze(path, rootDir, report.Warnings));
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                report.Warnings.Add($"Could not analyze {Path.GetRelativePath(rootDir, path)}: {ex.Message}");
            }
        }

        MakeNamesUnique(report.Projects, rootDir);
        Estimator.Estimate(report);
        return report;
    }

    private static Regex Wildcard(string pattern) =>
        new("^" + Regex.Escape(pattern.Replace('\\', '/')).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Projects with the same file name (e.g. two "Core.csproj" in a directory scan) get their path appended,
    /// and project references are re-pointed by full path so the migration order stays correct.
    /// </summary>
    private static void MakeNamesUnique(List<ProjectReport> projects, string rootDir)
    {
        foreach (var group in projects.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var p in group) p.Name = $"{p.Name} ({p.Path})";

        var byPath = projects
            .GroupBy(p => Path.GetFullPath(Path.Combine(rootDir, p.Path)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
            for (var i = 0; i < p.ProjectReferencePaths.Count; i++)
                if (byPath.TryGetValue(p.ProjectReferencePaths[i], out var target)) p.ProjectReferences[i] = target.Name;
    }
}
