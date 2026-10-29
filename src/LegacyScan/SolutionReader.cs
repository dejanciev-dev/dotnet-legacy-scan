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

    [GeneratedRegex("""^Project\("\{[^}]+\}"\)\s*=\s*"[^"]*"\s*,\s*"(?<path>[^"]+)"\s*,""", RegexOptions.Multiline)]
    private static partial Regex SlnProjectLine();

    public static List<string> FindProjects(string input, List<string> warnings)
    {
        var full = Path.GetFullPath(input);
        if (Directory.Exists(full))
        {
            var slns = Directory.GetFiles(full, "*.sln").Concat(Directory.GetFiles(full, "*.slnx")).ToList();
            if (slns.Count == 1) return FromSolution(slns[0], warnings);
            if (slns.Count > 1) warnings.Add($"Multiple solution files found in {full}; scanning all projects in the directory instead.");
            return FromDirectory(full);
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
            relPaths = SlnProjectLine().Matches(File.ReadAllText(slnPath)).Select(m => m.Groups["path"].Value);
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
            result.AddRange(Directory.GetFiles(d).Where(f => ProjectExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
            foreach (var sub in Directory.GetDirectories(d))
                if (!IgnoredDirs.Contains(Path.GetFileName(sub))) stack.Push(sub);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
