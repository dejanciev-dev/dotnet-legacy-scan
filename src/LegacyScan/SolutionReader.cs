using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LegacyScan;

/// <summary>Finds project files from a .sln, .slnx, a single project file or a directory.</summary>
public static partial class SolutionReader
{
    private static readonly string[] ProjectExtensions = [".csproj", ".vbproj"];

    internal static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "packages", "node_modules", ".git", ".vs", ".idea", "TestResults", "artifacts",
    };

    [GeneratedRegex("""^Project\("\{(?<type>[^}]+)\}"\)\s*=\s*"(?<name>[^"]*)"\s*,\s*"(?<path>[^"]+)"\s*,""", RegexOptions.Multiline)]
    private static partial Regex SlnProjectLine();

    /// <summary>"Web Site" projects have no project file (just a folder with App_Code) and cannot be analyzed.</summary>
    private const string WebSiteProjectType = "E24C65DC-7377-472B-9ABA-BC803B73C61A";

    private const string WebSiteWarning = "Web Site projects have no project file and are not analyzed; convert to a Web Application project or review it manually.";

    public static List<string> FindProjects(string input, List<string> warnings)
    {
        var full = Path.GetFullPath(input);
        if (Directory.Exists(full))
        {
            var slns = Directory.GetFiles(full, "*.sln").Concat(Directory.GetFiles(full, "*.slnx")).ToList();
            if (slns.Count == 1) return FromSolution(slns[0], warnings);
            if (slns.Count > 1) warnings.Add($"Multiple solution files found in {full}; scanning all projects in the directory instead.");
            var projects = FromDirectory(full);
            if (projects.Count == 0 && LooksLikeWebSite(full)) warnings.Add($"{Path.GetFileName(full)} looks like a Web Site (App_Code / .aspx, no project file). {WebSiteWarning}");
            return projects;
        }

        if (!File.Exists(full)) throw new FileNotFoundException($"Not found: {input}");
        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is ".sln" or ".slnx") return FromSolution(full, warnings);
        if (ProjectExtensions.Contains(ext)) return [full];
        throw new ArgumentException($"Unsupported input '{input}'. Pass a .sln, .slnx, .csproj, .vbproj or a directory.");
    }

    public static List<string> FromSolution(string slnPath, List<string> warnings)
    {
        var dir = Path.GetDirectoryName(slnPath)!;
        IEnumerable<string> relPaths;
        if (slnPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            XDocument doc;
            try { doc = XDocument.Load(slnPath); }
            catch (System.Xml.XmlException ex) { throw new FormatException($"Could not read {Path.GetFileName(slnPath)}: {ex.Message}", ex); }
            relPaths = doc.Descendants()
                .Where(e => e.Name.LocalName == "Project")
                .Select(e => (string?)e.Attribute("Path"))
                .OfType<string>();
        }
        else
        {
            var lines = SlnProjectLine().Matches(File.ReadAllText(slnPath));
            foreach (var site in lines.Where(m => m.Groups["type"].Value.Equals(WebSiteProjectType, StringComparison.OrdinalIgnoreCase)))
                warnings.Add($"Web Site project '{site.Groups["name"].Value}' ({site.Groups["path"].Value}): {WebSiteWarning}");
            relPaths = lines.Select(m => m.Groups["path"].Value);
        }

        var result = new List<string>();
        foreach (var rel in relPaths)
        {
            var normalized = rel.Replace('\\', Path.DirectorySeparatorChar);
            if (!ProjectExtensions.Contains(Path.GetExtension(normalized).ToLowerInvariant())) continue; // solution folders, website projects etc.
            var p = Path.GetFullPath(Path.Combine(dir, normalized));
            if (File.Exists(p)) result.Add(p);
            else warnings.Add($"Project listed in solution but not found: {rel}");
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> FromDirectory(string dir)
    {
        var result = new List<string>();
        var stack = new Stack<string>([dir]);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            try { result.AddRange(Directory.GetFiles(d).Where(f => ProjectExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var sub in SubDirectories(d)) stack.Push(sub);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>
    /// Subdirectories to scan, sorted: build/tool folders, junctions and symlinks (which can loop) and unreadable folders are skipped.
    /// </summary>
    internal static string[] SubDirectories(string dir)
    {
        try
        {
            var subs = new DirectoryInfo(dir).GetDirectories()
                .Where(d => !IgnoredDirs.Contains(d.Name) && !d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Select(d => d.FullName)
                .ToArray();
            Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
            return subs;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool LooksLikeWebSite(string dir)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateDirectories(dir, "App_Code", options).Any() || Directory.EnumerateFiles(dir, "*.aspx", options).Any();
    }
}
