namespace LegacyScan;

public static class Scanner
{
    public static ScanReport Scan(string input)
    {
        var report = new ScanReport { Input = Path.GetFullPath(input) };
        var projects = SolutionReader.FindProjects(input, report.Warnings);
        if (projects.Count == 0) report.Warnings.Add("No .csproj or .vbproj projects found.");

        var rootDir = Directory.Exists(report.Input) ? report.Input : Path.GetDirectoryName(report.Input)!;
        foreach (var path in projects)
        {
            try
            {
                report.Projects.Add(ProjectAnalyzer.Analyze(path, rootDir));
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                report.Warnings.Add($"Could not analyze {Path.GetRelativePath(rootDir, path)}: {ex.Message}");
            }
        }

        Estimator.Estimate(report);
        return report;
    }
}
