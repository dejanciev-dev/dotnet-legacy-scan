using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LegacyScan;

/// <summary>Analyzes one .csproj/.vbproj: project file, config files, NuGet packages, content files and source code.</summary>
public static partial class ProjectAnalyzer
{
    private static class TypeGuids
    {
        public const string WebApp = "349c5851-65df-11da-9384-00065b846f21";
        public const string Wcf = "3d9ad99f-2412-4246-b90b-4eaa41c64699";
        public const string Wpf = "60dc8134-eba5-43b8-bcc9-bb4bc16c2548";
        public const string Test = "3ac096d0-a1c2-e12c-1390-a8335801fdab";
        public const string Workflow = "32f31d43-81cc-4c15-9de6-3fc5453562b6";
    }

    [GeneratedRegex(@"^net(coreapp\d|standard\d|[5-9]\.|\d{2}\.)", RegexOptions.IgnoreCase)]
    private static partial Regex ModernTfm();

    [GeneratedRegex(@"^net(\d)(\d)(\d)?$", RegexOptions.IgnoreCase)]
    private static partial Regex FrameworkTfm();

    private sealed class Inventory
    {
        public readonly List<string> Aspx = [], Ascx = [], Master = [], Svc = [], Asmx = [], Views = [], Edmx = [], Workflows = [], ServiceRefs = [], Configs = [], Reports = [];
        public string? GlobalAsax;
        public readonly List<string> Sources = [];
    }

    public static ProjectReport Analyze(string projectPath, string rootDir, List<string>? warnings = null)
    {
        var dir = Path.GetDirectoryName(projectPath)!;
        var report = new ProjectReport
        {
            Name = Path.GetFileNameWithoutExtension(projectPath),
            Path = Path.GetRelativePath(rootDir, projectPath),
            Language = projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ? "VB.NET" : "C#",
        };
        var findings = new Dictionary<Rule, Finding>();

        var xml = XDocument.Load(projectPath);
        var root = xml.Root!;
        XElement[] Els(string name) => root.Descendants().Where(e => e.Name.LocalName == name).ToArray();
        string? Val(string name) => Els(name).Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);

        // ---- Project format & target ----
        report.IsSdkStyle = root.Attribute("Sdk") is not null || Els("Sdk").Length > 0 || Els("Import").Any(e => e.Attribute("Sdk") is not null);
        var tfmValue = Val("TargetFrameworks") ?? Val("TargetFramework");
        if (tfmValue is null && report.IsSdkStyle) tfmValue = TfmFromDirectoryBuildProps(dir);
        var tfms = tfmValue?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tfms is { Length: > 0 }) report.TargetFramework = string.Join(";", tfms);
        else if (Val("TargetFrameworkVersion") is { } v) report.TargetFramework = "net" + v.TrimStart('v', 'V').Replace(".", "");

        // Multi-targeting net48;net10.0 means the project already builds for modern .NET.
        var allTfms = report.TargetFramework.Split(';');
        report.IsModernAlready = report.IsSdkStyle && allTfms.Any(t => ModernTfm().IsMatch(t));
        if (report.IsSdkStyle && !allTfms.Any(t => ModernTfm().IsMatch(t) || FrameworkTfm().IsMatch(t)))
            warnings?.Add($"Target framework of {report.Path} could not be resolved ('{report.TargetFramework}'); treated as not yet migrated.");

        // ---- References & packages ----
        var references = Els("Reference").Select(e => ((string?)e.Attribute("Include") ?? "").Split(',')[0].Trim()).Where(s => s.Length > 0).ToList();
        foreach (var pr in Els("PackageReference"))
            if ((string?)pr.Attribute("Include") is { } id) report.Packages.Add($"{id} {(string?)pr.Attribute("Version") ?? pr.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value}".Trim());

        var packagesConfig = Path.Combine(dir, "packages.config");
        if (File.Exists(packagesConfig))
        {
            CodeScanner.Add(findings, Rules.PackagesConfig, new Occurrence("packages.config", 1, "packages.config"));
            try
            {
                foreach (var p in XDocument.Load(packagesConfig).Descendants("package"))
                    report.Packages.Add($"{(string?)p.Attribute("id")} {(string?)p.Attribute("version")}".Trim());
            }
            catch (System.Xml.XmlException) { /* ignore malformed */ }
        }

        foreach (var pr in Els("ProjectReference"))
            if ((string?)pr.Attribute("Include") is { } inc)
            {
                var refPath = inc.Replace('\\', Path.DirectorySeparatorChar);
                report.ProjectReferences.Add(Path.GetFileNameWithoutExtension(refPath));
                report.ProjectReferencePaths.Add(Path.GetFullPath(Path.Combine(dir, refPath)));
            }

        bool HasRef(string name) =>
            references.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase) || r.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
            || report.Packages.Any(p => p.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase) || p.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase));

        var typeGuids = (Val("ProjectTypeGuids") ?? "").ToLowerInvariant();
        var outputType = (Val("OutputType") ?? "Library").ToLowerInvariant();

        // ---- File inventory ----
        var inv = new Inventory();
        Walk(dir, dir, inv, isRoot: true);

        // ---- Kind ----
        // Test projects often reference System.Web.Mvc / Web API to test controllers, so check them before the web kinds.
        report.Kind =
            inv.Aspx.Count > 0 ? ProjectKind.WebForms :
            typeGuids.Contains(TypeGuids.Test) || HasRef("Microsoft.VisualStudio.QualityTools.UnitTestFramework") || HasRef("NUnit") || HasRef("xunit") || HasRef("MSTest.TestFramework") ? ProjectKind.Test :
            HasRef("System.Web.Mvc") || HasRef("Microsoft.AspNet.Mvc") ? ProjectKind.AspNetMvc :
            HasRef("System.Web.Http") || HasRef("Microsoft.AspNet.WebApi") ? ProjectKind.WebApi :
            inv.Svc.Count > 0 || typeGuids.Contains(TypeGuids.Wcf) ? ProjectKind.WcfService :
            typeGuids.Contains(TypeGuids.WebApp) ? ProjectKind.WebForms :
            typeGuids.Contains(TypeGuids.Wpf) || HasRef("PresentationFramework") || Val("UseWPF") == "true" ? ProjectKind.Wpf :
            HasRef("System.Windows.Forms") && outputType == "winexe" || Val("UseWindowsForms") == "true" ? ProjectKind.WinForms :
            outputType is "exe" or "winexe" ? ProjectKind.Console :
            ProjectKind.ClassLibrary;

        // ---- Source code ----
        foreach (var src in inv.Sources)
            CodeScanner.ScanFile(src, Path.GetRelativePath(dir, src), report, findings);
        report.SourceFiles = inv.Sources.Count;

        if (report.IsModernAlready)
        {
            // Already on modern .NET: only keep code-level findings that still matter.
            foreach (var key in findings.Keys.Where(r => r != Rules.BinaryFormatter && r != Rules.ThreadAbort).ToList()) findings.Remove(key);
            Finish(report, findings);
            report.RecommendedTarget = "Already on modern .NET";
            return report;
        }

        // ---- Project-level rules ----
        if (!report.IsSdkStyle) CodeScanner.Add(findings, Rules.LegacyProjectFormat, new Occurrence(report.Path, 1, "ToolsVersion / non-SDK project"));
        var fw = allTfms.Select(t => FrameworkTfm().Match(t)).FirstOrDefault(m => m.Success);
        if (fw is not null)
        {
            var version = int.Parse(fw.Groups[1].Value) * 100 + int.Parse(fw.Groups[2].Value) * 10 + (fw.Groups[3].Success ? int.Parse(fw.Groups[3].Value) : 0);
            if (version < 472) CodeScanner.Add(findings, Rules.OldFramework, new Occurrence(report.Path, 1, report.TargetFramework));
        }

        AddFiles(findings, Rules.WebForms, inv.Aspx, dir);
        AddFiles(findings, Rules.WebFormsControls, inv.Ascx.Concat(inv.Master), dir);
        AddFiles(findings, Rules.Asmx, inv.Asmx, dir);
        AddFiles(findings, Rules.Edmx, inv.Edmx, dir);
        AddFiles(findings, Rules.WcfClient, inv.ServiceRefs, dir);
        AddFiles(findings, Rules.Reporting, inv.Reports, dir);
        if (inv.Workflows.Count > 0 || typeGuids.Contains(TypeGuids.Workflow) || HasRef("System.Activities")) AddFiles(findings, Rules.Workflow, inv.Workflows, dir, ensure: true);
        if (inv.GlobalAsax is not null) CodeScanner.Add(findings, Rules.GlobalAsax, new Occurrence(Path.GetRelativePath(dir, inv.GlobalAsax), 1, "Global.asax"));

        if (report.Kind == ProjectKind.AspNetMvc)
        {
            var f = new Finding { Rule = Rules.Mvc5 };
            f.Occurrences.AddRange(inv.Sources
                .Where(s => Path.GetFileNameWithoutExtension(s).EndsWith("Controller", StringComparison.OrdinalIgnoreCase) && !File.ReadAllText(s).Contains("ApiController"))
                .Select(s => new Occurrence(Path.GetRelativePath(dir, s), 1, "controller")));
            findings[Rules.Mvc5] = f;
            AddFiles(findings, Rules.MvcViews, inv.Views, dir);
        }
        if (report.Kind == ProjectKind.WebApi || (report.Kind == ProjectKind.AspNetMvc && (HasRef("System.Web.Http") || HasRef("Microsoft.AspNet.WebApi"))))
        {
            var f = new Finding { Rule = Rules.WebApi2 };
            f.Occurrences.AddRange(inv.Sources.Where(s => File.ReadAllText(s).Contains("ApiController"))
                .Select(s => new Occurrence(Path.GetRelativePath(dir, s), 1, "ApiController")));
            findings[Rules.WebApi2] = f;
        }
        if (report.Kind is ProjectKind.WinForms or ProjectKind.Wpf) CodeScanner.Add(findings, Rules.DesktopApp, new Occurrence(report.Path, 1, report.Kind.ToString()));

        // ---- web.config / app.config ----
        foreach (var cfg in inv.Configs) AnalyzeConfig(cfg, Path.GetRelativePath(dir, cfg), findings, inv);
        if (inv.Svc.Count > 0 || findings.ContainsKey(Rules.WcfService))
        {
            var f = findings.TryGetValue(Rules.WcfService, out var existing) ? existing : findings[Rules.WcfService] = new Finding { Rule = Rules.WcfService };
            f.Occurrences.AddRange(inv.Svc.Select(s => new Occurrence(Path.GetRelativePath(dir, s), 1, Path.GetFileName(s))));
        }

        // Session usage only matters when there is session config or a web project
        if (findings.ContainsKey(Rules.SessionState) && report.Kind is not (ProjectKind.WebForms or ProjectKind.AspNetMvc or ProjectKind.WebApi or ProjectKind.ClassLibrary))
            findings.Remove(Rules.SessionState);

        // WCF client code in a WCF service project is the service itself; skip duplicate cost
        if (report.Kind == ProjectKind.WcfService) findings.Remove(Rules.WcfClient);

        // ---- Packages ----
        if (HasRef("Microsoft.AspNet.Identity") || HasRef("Microsoft.Owin.Security"))
            CodeScanner.Add(findings, Rules.AspNetIdentity2, new Occurrence("packages", 0, "Microsoft.AspNet.Identity / Microsoft.Owin.Security"));

        // One occurrence per package family; skip families whose cost is already in an app-model rule (e.g. MVC packages → APP003).
        var families = new Dictionary<string, (Rules.KnownPackage Known, List<string> Packages)>(StringComparer.OrdinalIgnoreCase);
        foreach (var pkg in report.Packages.Concat(references.Where(r => !report.Packages.Any(p => p.StartsWith(r + " ", StringComparison.OrdinalIgnoreCase)))))
        {
            var id = pkg.Split(' ')[0];
            if (id.Equals("EntityFramework", StringComparison.OrdinalIgnoreCase))
            {
                CodeScanner.Add(findings, Rules.Ef6Package, new Occurrence("packages", 0, pkg));
                continue;
            }
            if (Rules.SupportedOnModern.Contains(id)) continue;
            var known = Rules.KnownPackages.FirstOrDefault(k => id.StartsWith(k.Prefix, StringComparison.OrdinalIgnoreCase));
            if (known is null || (known.CoveredBy is { } covered && findings.ContainsKey(covered))) continue;
            if (!families.TryGetValue(known.FamilyKey, out var family)) families[known.FamilyKey] = family = (known, []);
            family.Packages.Add(pkg);
        }
        foreach (var (known, packages) in families.Values)
            CodeScanner.Add(findings, known.Rule, new Occurrence("packages", 0, $"{string.Join(", ", packages)} {known.Note}"));

        Finish(report, findings);
        report.RecommendedTarget = Recommend(report);
        return report;
    }

    private static void Finish(ProjectReport report, Dictionary<Rule, Finding> findings)
    {
        report.Findings.AddRange(findings.Values
            .OrderByDescending(f => f.Rule.Severity)
            .ThenByDescending(f => f.EstimatedHours));
    }

    private static void AddFiles(Dictionary<Rule, Finding> findings, Rule rule, IEnumerable<string> files, string dir, bool ensure = false)
    {
        var list = files.ToList();
        if (list.Count == 0 && !ensure) return;
        CodeScanner.Add(findings, rule);
        findings[rule].Occurrences.AddRange(list.Select(f => new Occurrence(Path.GetRelativePath(dir, f), 1, Path.GetFileName(f))));
    }

    private static void Walk(string dir, string projectDir, Inventory inv, bool isRoot)
    {
        string[] files;
        try { files = Directory.GetFiles(dir); }
        catch (UnauthorizedAccessException) { return; }
        Array.Sort(files, StringComparer.OrdinalIgnoreCase); // same report order on Windows and Linux

        // Another project nested below this one owns its own folder.
        if (!isRoot && files.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))) return;

        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            var ext = Path.GetExtension(f).ToLowerInvariant();
            switch (ext)
            {
                case ".cs" or ".vb": inv.Sources.Add(f); break;
                case ".aspx": inv.Aspx.Add(f); break;
                case ".ascx": inv.Ascx.Add(f); break;
                case ".master": inv.Master.Add(f); break;
                case ".svc": inv.Svc.Add(f); break;
                case ".asmx": inv.Asmx.Add(f); break;
                case ".cshtml" or ".vbhtml": inv.Views.Add(f); break;
                case ".edmx": inv.Edmx.Add(f); break;
                case ".xamlx": inv.Workflows.Add(f); break;
                case ".xaml" when IsWorkflowXaml(f): inv.Workflows.Add(f); break;
                case ".asax" when name.Equals("Global.asax", StringComparison.OrdinalIgnoreCase): inv.GlobalAsax = f; break;
                case ".config" when name.StartsWith("web", StringComparison.OrdinalIgnoreCase) || name.StartsWith("app", StringComparison.OrdinalIgnoreCase):
                    // only the base config, not Web.Debug.config transforms
                    if (name.Count(ch => ch == '.') == 1) inv.Configs.Add(f);
                    break;
                case ".svcmap" or ".discomap": inv.ServiceRefs.Add(f); break;
                case ".map" when name.Equals("Reference.map", StringComparison.OrdinalIgnoreCase): inv.ServiceRefs.Add(f); break; // ASMX Web Reference
                case ".rpt" or ".rdlc": inv.Reports.Add(f); break;
                case ".json" when name.Equals("ConnectedService.json", StringComparison.OrdinalIgnoreCase): inv.ServiceRefs.Add(f); break;
            }
        }
        var subs = Directory.GetDirectories(dir);
        Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
        foreach (var sub in subs)
            if (!SolutionReader.IgnoredDirs.Contains(Path.GetFileName(sub)))
                Walk(sub, projectDir, inv, isRoot: false);
    }

    private static bool IsWorkflowXaml(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var head = new char[600];
            var n = reader.Read(head, 0, head.Length);
            var s = new string(head, 0, n);
            return s.Contains("<Activity", StringComparison.Ordinal) || s.Contains("schemas.microsoft.com/netfx/2009/xaml/activities", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
    }

    private static void AnalyzeConfig(string path, string rel, Dictionary<Rule, Finding> findings, Inventory inv)
    {
        XDocument doc;
        try { doc = XDocument.Load(path, LoadOptions.SetLineInfo); }
        catch (System.Xml.XmlException) { return; }

        int Line(XElement e) => ((System.Xml.IXmlLineInfo)e).LineNumber;
        IEnumerable<XElement> D(string n) => doc.Descendants().Where(e => e.Name.LocalName == n);
        IEnumerable<XElement> SystemWeb(string n) => D(n).Where(e => e.Ancestors().Any(a => a.Name.LocalName == "system.web"));

        var providers = SystemWeb("membership")
            .Concat(SystemWeb("roleManager").Where(e => string.Equals((string?)e.Attribute("enabled"), "true", StringComparison.OrdinalIgnoreCase)))
            .Concat(SystemWeb("profile"))
            .ToList();
        foreach (var m in providers)
            CodeScanner.Add(findings, Rules.MembershipAuth, new Occurrence(rel, Line(m), $"<{m.Name.LocalName}>"));
        // Forms auth on its own (custom login) is a much smaller job than moving Membership users to Identity.
        foreach (var a in SystemWeb("authentication").Where(e => string.Equals((string?)e.Attribute("mode"), "Forms", StringComparison.OrdinalIgnoreCase)))
            CodeScanner.Add(findings, providers.Count > 0 ? Rules.MembershipAuth : Rules.FormsAuth, new Occurrence(rel, Line(a), "<authentication mode=\"Forms\">"));

        foreach (var s in D("sessionState"))
        {
            var mode = (string?)s.Attribute("mode") ?? "InProc";
            if (mode is "SQLServer" or "StateServer" or "Custom")
                CodeScanner.Add(findings, Rules.SessionState, new Occurrence(rel, Line(s), $"<sessionState mode=\"{mode}\">"));
        }

        var serviceModel = D("system.serviceModel").FirstOrDefault();
        if (serviceModel is not null)
        {
            var services = serviceModel.Descendants().Where(e => e.Name.LocalName == "service").ToList();
            if (services.Count > 0 && inv.Svc.Count == 0)
            {
                // self-hosted or config-based activation
                foreach (var s in services)
                    CodeScanner.Add(findings, Rules.WcfService, new Occurrence(rel, Line(s), $"<service name=\"{(string?)s.Attribute("name")}\">"));
            }
            foreach (var ep in serviceModel.Descendants().Where(e => e.Name.LocalName == "endpoint" && e.Parent?.Name.LocalName == "client"))
                CodeScanner.Add(findings, Rules.WcfClient, new Occurrence(rel, Line(ep), $"client endpoint binding=\"{(string?)ep.Attribute("binding")}\""));
        }

        // Custom modules/handlers registered in config. Classic and integrated pipeline often register the same type twice,
        // and the class itself may already have been found in code, so count each type once.
        var known = findings.TryGetValue(Rules.HttpModulesHandlers, out var hm)
            ? hm.Occurrences.Select(o => Path.GetFileNameWithoutExtension(o.File)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in D("httpModules").Concat(D("modules")).Concat(D("httpHandlers")).Concat(D("handlers")).SelectMany(e => e.Elements().Where(x => x.Name.LocalName == "add")))
        {
            var type = ((string?)h.Attribute("type") ?? "").Split(',')[0].Trim();
            if (type.StartsWith("System.", StringComparison.Ordinal) || type.Length == 0) continue; // built-in
            var shortName = type.Split('.')[^1];
            if (!known.Add(shortName)) continue;
            CodeScanner.Add(findings, Rules.HttpModulesHandlers, new Occurrence(rel, Line(h), $"<add type=\"{type}\">"));
        }
    }

    private static string Recommend(ProjectReport p)
    {
        var usesSystemWeb = p.Findings.Any(f => f.Rule == Rules.SystemWebInLibrary || f.Rule == Rules.HttpContextCurrent);
        return p.Kind switch
        {
            ProjectKind.WebForms => "New ASP.NET Core app (Blazor or Razor Pages). Migrate page by page behind YARP, share auth/session via System.Web adapters.",
            ProjectKind.AspNetMvc => "ASP.NET Core MVC on net10.0. Large apps: incremental migration with YARP + System.Web adapters.",
            ProjectKind.WebApi => "ASP.NET Core Web API on net10.0.",
            ProjectKind.WcfService => "CoreWCF on net10.0 (keeps contracts/clients) or redesign as gRPC / REST.",
            ProjectKind.WinForms or ProjectKind.Wpf => "net10.0-windows.",
            ProjectKind.Test => "net10.0, same test framework with updated packages. Migrate together with the project under test.",
            ProjectKind.Console => "net10.0.",
            _ when usesSystemWeb => "Remove System.Web dependencies, then multi-target net48;net10.0 (or netstandard2.0).",
            _ when p.WorstSeverity >= Severity.Major => "Multi-target net48;net10.0 (or netstandard2.0) once its major and blocker findings are resolved.",
            _ => "Multi-target net48;net10.0 (or netstandard2.0). Good first candidate.",
        };
    }

    /// <summary>Nearest Directory.Build.props (walking up) that sets TargetFramework(s), as SDK projects often inherit it.</summary>
    private static string? TfmFromDirectoryBuildProps(string projectDir)
    {
        for (var d = new DirectoryInfo(projectDir); d is not null; d = d.Parent)
        {
            var props = Path.Combine(d.FullName, "Directory.Build.props");
            if (!File.Exists(props)) continue;
            try
            {
                var els = XDocument.Load(props).Descendants().ToList();
                var value = els.Where(e => e.Name.LocalName == "TargetFrameworks").Concat(els.Where(e => e.Name.LocalName == "TargetFramework"))
                    .Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);
                if (value is not null) return value;
            }
            catch (System.Xml.XmlException) { return null; }
        }
        return null;
    }
}
