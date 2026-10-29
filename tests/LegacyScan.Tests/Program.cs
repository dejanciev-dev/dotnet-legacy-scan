using LegacyScan;

var failures = 0;
var passed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine($"  ok   {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"  FAIL {name}: {ex.Message}"); }
}
void Eq<T>(T expected, T actual, string what = "") { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what} expected <{expected}> but was <{actual}>"); }
void True(bool cond, string what) { if (!cond) throw new Exception(what); }

var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/LegacyShop/LegacyShop.sln"));
var report = Scanner.Scan(sample);
ProjectReport P(string name) => report.Projects.Single(p => p.Name == name);
Finding? F(string project, Rule rule) => P(project).Findings.SingleOrDefault(f => f.Rule == rule);

Console.WriteLine("Code scanner");
Test("C# comments and strings are blanked", () =>
{
    var s = CodeScanner.StripCSharp("var a = \"HttpContext.Current\"; // BinaryFormatter\n/* x\n y */ var b = @\"c:\\\"\"q\"\"\"; var c = '\\''; Real();");
    True(!s.Contains("HttpContext") && !s.Contains("BinaryFormatter"), "comment/string content removed");
    True(s.Contains("Real()"), "code kept");
    Eq(3, s.Split('\n').Length, "line count");
});
Test("VB comments, REM and strings are blanked", () =>
{
    var s = CodeScanner.StripVb("Dim x = \"a \"\"HttpContext.Current\"\" b\" ' Thread.Abort\nREM BinaryFormatter\nRemove()");
    True(!s.Contains("HttpContext") && !s.Contains("Thread.Abort") && !s.Contains("BinaryFormatter"), "removed");
    True(s.Contains("Remove()"), "identifier starting with Rem kept");
});

Test("C# interpolation holes keep code, strings inside holes are blanked", () =>
{
    var s = CodeScanner.StripCSharp("var a = $\"{HttpContext.Current.User} {(x ? \"BinaryFormatter\" : \"y\")} {{Thread.Abort}}\"; Real();");
    True(s.Contains("HttpContext.Current.User"), "hole code kept: " + s);
    True(!s.Contains("BinaryFormatter") && !s.Contains("Thread.Abort"), "nested string and {{ }} text blanked: " + s);
    True(s.Contains("Real()"), "code after string kept");
});
Test("C# raw strings with 4 quotes and $$ holes", () =>
{
    var s = CodeScanner.StripCSharp("var a = \"\"\"\"\n has \"\"\" inside BinaryFormatter\n \"\"\"\";\nvar b = $$\"\"\"\n {{HttpContext.Current}} {x} BinaryFormatter\n \"\"\";\nReal();");
    True(!s.Contains("BinaryFormatter"), "raw content blanked: " + s);
    True(s.Contains("HttpContext.Current") && s.Contains("Real()"), "code after raw string and in $$ hole kept: " + s);
    Eq(7, s.Split('\n').Length, "line count");
});
Test("pattern false positives: member Session[], ThreadPool.Resume()", () =>
{
    bool Hits(Rule rule, string line) => CodeScanner.Patterns.Any(p => p.Rule == rule && p.Regex.IsMatch(line));
    True(Hits(Rules.SessionState, "var c = Session[\"x\"];"), "Session[ on page");
    True(Hits(Rules.SessionState, "HttpContext.Current.Session.Abandon();"), "HttpContext.Current.Session");
    True(!Hits(Rules.SessionState, "var v = viewModel.Session[0];"), "member Session[ ignored");
    True(Hits(Rules.ThreadAbort, "_workerThread.Abort();"), "thread variable");
    True(!Hits(Rules.ThreadAbort, "myThreadPool.Resume();"), "ThreadPool ignored");
});
Test("generated code is counted as lines but not scanned", () =>
{
    True(CodeScanner.IsGenerated("Service References/Pay/Reference.cs"), "service reference");
    True(CodeScanner.IsGenerated("Forms/Main.Designer.cs"), "designer");
    True(!CodeScanner.IsGenerated("Services/OrderService.cs"), "normal file");
});

Console.WriteLine("Solution");
Test("all projects from .sln are found, solution folders skipped", () => Eq(7, report.Projects.Count));
Test("directory input finds the single solution", () => Eq(7, Scanner.Scan(Path.GetDirectoryName(sample)!).Projects.Count));
Test("project kinds", () =>
{
    Eq(ProjectKind.WebForms, P("LegacyShop.Web").Kind);
    Eq(ProjectKind.WcfService, P("LegacyShop.Services").Kind);
    Eq(ProjectKind.AspNetMvc, P("LegacyShop.Admin").Kind);
    Eq(ProjectKind.Test, P("LegacyShop.Tests").Kind);
    Eq(ProjectKind.ClassLibrary, P("LegacyShop.Core").Kind);
    Eq("VB.NET", P("LegacyShop.Reports").Language);
});
Test("modern SDK project has no findings", () =>
{
    True(P("LegacyShop.Modern").IsModernAlready, "modern");
    Eq(0, P("LegacyShop.Modern").Findings.Count);
});

Console.WriteLine("Rules");
Test("comments, strings and bin/ are ignored in Core", () =>
{
    Eq(1, F("LegacyShop.Core", Rules.HttpContextCurrent)!.Occurrences.Count, "HttpContext.Current");
    Eq(1, F("LegacyShop.Core", Rules.BinaryFormatter)!.Occurrences.Count, "BinaryFormatter");
});
Test("Core: old framework, legacy format, packages.config, EF6, EDMX, System.Web in library", () =>
{
    foreach (var r in new[] { Rules.OldFramework, Rules.LegacyProjectFormat, Rules.PackagesConfig, Rules.Ef6Package, Rules.Edmx, Rules.SystemWebInLibrary, Rules.SystemDataSqlClient })
        True(F("LegacyShop.Core", r) is not null, r.Id);
});
Test("Web: pages, controls, membership, session, module counted once, WCF client", () =>
{
    Eq(3, F("LegacyShop.Web", Rules.WebForms)!.Occurrences.Count, "aspx");
    Eq(2, F("LegacyShop.Web", Rules.WebFormsControls)!.Occurrences.Count, "ascx+master");
    True(F("LegacyShop.Web", Rules.MembershipAuth) is not null, "membership");
    True(F("LegacyShop.Web", Rules.SessionState) is not null, "session");
    Eq(1, F("LegacyShop.Web", Rules.HttpModulesHandlers)!.Occurrences.Count, "module");
    Eq(2, F("LegacyShop.Web", Rules.WcfClient)!.Occurrences.Count, "svcmap + config endpoint");
    True(F("LegacyShop.Web", Rules.SystemWebInLibrary) is null, "no library rule in web project");
});
Test("Web.Debug.config transform is not analyzed", () =>
    True(F("LegacyShop.Web", Rules.MembershipAuth)!.Occurrences.All(o => o.File == "Web.config"), "only Web.config"));
Test("Services: two .svc + one .asmx", () =>
{
    Eq(2, F("LegacyShop.Services", Rules.WcfService)!.Occurrences.Count);
    Eq(1, F("LegacyShop.Services", Rules.Asmx)!.Occurrences.Count);
});
Test("Admin: MVC controller excludes ApiController, Web API found, Identity/OWIN", () =>
{
    Eq(1, F("LegacyShop.Admin", Rules.Mvc5)!.Occurrences.Count, "mvc controllers");
    Eq(1, F("LegacyShop.Admin", Rules.WebApi2)!.Occurrences.Count, "api controllers");
    True(F("LegacyShop.Admin", Rules.AspNetIdentity2) is not null, "identity");
    // MVC, Web API, Identity and bundling packages are part of APP003/APP005/SEC002; only the OWIN family is left.
    var pkg = F("LegacyShop.Admin", Rules.PackageNeedsReplacement)!;
    Eq(1, pkg.Occurrences.Count, "package families to replace");
    True(pkg.Occurrences[0].Snippet.StartsWith("Microsoft.Owin.Security.Cookies"), pkg.Occurrences[0].Snippet);
});
Test("Web: Web Forms-only packages are part of APP001, not charged again", () =>
{
    var pkg = F("LegacyShop.Web", Rules.PackageNeedsReplacement)!;
    Eq(1, pkg.Occurrences.Count, "package families");
    True(pkg.Occurrences[0].Snippet.StartsWith("Elmah"), pkg.Occurrences[0].Snippet);
});
Test("Reports (VB): Crystal via assembly reference, Thread.Abort once (comment ignored)", () =>
{
    True(F("LegacyShop.Reports", Rules.Reporting) is not null, "crystal");
    Eq(1, F("LegacyShop.Reports", Rules.ThreadAbort)!.Occurrences.Count, "abort");
});
Test("Reports: library with a blocker is not called a good first candidate", () =>
    True(!P("LegacyShop.Reports").RecommendedTarget.Contains("Good first candidate"), P("LegacyShop.Reports").RecommendedTarget));

Console.WriteLine("Estimate");
Test("dependencies come first in migration order", () =>
{
    var o = report.MigrationOrder;
    True(o.IndexOf("LegacyShop.Core") < o.IndexOf("LegacyShop.Web"), "Core before Web");
    True(o.IndexOf("LegacyShop.Services") < o.IndexOf("LegacyShop.Web"), "Services before Web");
    Eq("LegacyShop.Tests", o[^1], "tests last");
});
Test("range and difficulty", () =>
{
    True(report.EstimatedHoursLow > 0 && report.EstimatedHoursHigh > report.EstimatedHoursLow, "range");
    True(report.Difficulty is "High" or "Very high" or "Medium", report.Difficulty);
});
Test("cycles do not hang", () =>
{
    var a = new ProjectReport { Name = "A", Path = "A" }; a.ProjectReferences.Add("B");
    var b = new ProjectReport { Name = "B", Path = "B" }; b.ProjectReferences.Add("A");
    Eq(2, Estimator.Order([a, b]).Count);
});

Console.WriteLine("Reports");
Test("HTML is self-contained, encoded and branded", () =>
{
    var html = HtmlReport.Render(report, new ReportOptions { Brand = "<Acme>", Contact = "me@example.com" });
    True(html.StartsWith("<!doctype html>"), "doctype");
    True(!html.Contains("<script"), "no scripts");
    True(html.Contains("&lt;Acme&gt;"), "encoded brand");
    True(html.Contains("mailto:me@example.com"), "mailto");
});
Test("JSON parses", () =>
{
    using var doc = System.Text.Json.JsonDocument.Parse(JsonReport.Render(report, new ReportOptions()));
    Eq(7, doc.RootElement.GetProperty("projects").GetArrayLength());
});

Console.WriteLine("Edge cases");
var temp = Path.Combine(Path.GetTempPath(), "legacyscan-tests-" + Guid.NewGuid().ToString("N"));
string Fixture(string relPath, string content)
{
    var full = Path.Combine(temp, relPath);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllText(full, content);
    return full;
}
const string LegacyProject = """<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup>{0}</Project>""";
string Legacy(string items = "") => LegacyProject.Replace("{0}", items);
try
{
    Test("projects with the same name do not crash and keep their references", () =>
    {
        Fixture("dup/a/Core/Core.csproj", Legacy());
        Fixture("dup/b/Core/Core.csproj", Legacy());
        Fixture("dup/a/App/App.csproj", Legacy("""<ItemGroup><ProjectReference Include="..\..\b\Core\Core.csproj" /></ItemGroup>"""));
        var r = Scanner.Scan(Path.Combine(temp, "dup"));
        Eq(3, r.Projects.Select(p => p.Name).Distinct().Count(), "unique names");
        var coreB = r.Projects.Single(p => p.Name.StartsWith("Core") && p.Path.StartsWith("b")).Name;
        Eq(coreB, r.Projects.Single(p => p.Name == "App").ProjectReferences.Single(), "reference re-pointed");
        True(r.MigrationOrder.IndexOf(coreB) < r.MigrationOrder.IndexOf("App"), "dependency first");
        True(HtmlReport.Render(r, new ReportOptions()).Contains(coreB), "html renders");
    });
    Test("malformed .slnx is a clean error, not a crash", () =>
        Eq(1, Cli.Run([Fixture("bad/Bad.slnx", "<Solution><Project Path=\"x.csproj\""), "-q", "-o", Path.Combine(temp, "bad.html")])));
    Test("test project referencing MVC is a test project", () =>
    {
        var path = Fixture("mvctest/Web.Tests/Web.Tests.csproj", Legacy("""<ItemGroup><Reference Include="System.Web.Mvc, Version=5.2.3.0" /><Reference Include="Microsoft.VisualStudio.QualityTools.UnitTestFramework" /></ItemGroup>"""));
        var p = ProjectAnalyzer.Analyze(path, temp);
        Eq(ProjectKind.Test, p.Kind);
        True(p.Findings.All(f => f.Rule != Rules.Mvc5), "no MVC finding");
    });
    Test("multi-targeted net48;net10.0 project is already modern", () =>
    {
        var path = Fixture("multi/Multi/Multi.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net48;net10.0</TargetFrameworks></PropertyGroup></Project>""");
        True(ProjectAnalyzer.Analyze(path, temp).IsModernAlready, "modern");
    });
    Test("target framework is read from Directory.Build.props", () =>
    {
        Fixture("props/Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var p = ProjectAnalyzer.Analyze(Fixture("props/src/Lib/Lib.csproj", """<Project Sdk="Microsoft.NET.Sdk"></Project>"""), temp);
        Eq("net10.0", p.TargetFramework);
        True(p.IsModernAlready, "modern");
    });
    Test("unresolvable target framework produces a warning", () =>
    {
        var warnings = new List<string>();
        ProjectAnalyzer.Analyze(Fixture("prop2/Lib/Lib.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>$(LibTfm)</TargetFramework></PropertyGroup></Project>"""), temp, warnings);
        Eq(1, warnings.Count, "warnings");
    });
    Test("packages that support modern .NET are not flagged", () =>
    {
        Fixture("client/Client/packages.config", """<packages><package id="Microsoft.AspNet.WebApi.Client" version="5.2.7" /></packages>""");
        var p = ProjectAnalyzer.Analyze(Fixture("client/Client/Client.csproj", Legacy()), temp);
        True(p.Findings.All(f => f.Rule != Rules.PackageNeedsReplacement), "WebApi.Client is netstandard2.0");
    });
    Test("ASMX Web References and report files are detected", () =>
    {
        Fixture("refs/App/Web References/Legacy/Reference.map", "<DiscoveryClientResultsFile />");
        Fixture("refs/App/Reports/Invoice.rpt", "");
        Fixture("refs/App/Reports/Orders.rdlc", "<Report />");
        var p = ProjectAnalyzer.Analyze(Fixture("refs/App/App.csproj", Legacy()), temp);
        Eq(1, p.Findings.Single(f => f.Rule == Rules.WcfClient).Occurrences.Count, "web reference");
        Eq(2, p.Findings.Single(f => f.Rule == Rules.Reporting).Occurrences.Count, "report files");
    });
    Test("membership/forms settings outside system.web are ignored", () =>
    {
        Fixture("cfg/Lib/app.config", "<configuration><mySection><profile name=\"x\" /><membership /></mySection></configuration>");
        var p = ProjectAnalyzer.Analyze(Fixture("cfg/Lib/Lib.csproj", Legacy()), temp);
        True(p.Findings.All(f => f.Rule != Rules.MembershipAuth), "no membership");
    });
    Test("Web Site projects in a .sln and in a folder produce a warning", () =>
    {
        Fixture("site/WebSite1/App_Code/Util.cs", "class Util { }");
        Fixture("site/WebSite1/Default.aspx", "<%@ Page %>");
        var sln = Fixture("site/Site.sln", "Project(\"{E24C65DC-7377-472B-9ABA-BC803B73C61A}\") = \"WebSite1\", \"WebSite1\\\", \"{11111111-1111-1111-1111-111111111111}\"\nEndProject\n");
        True(Scanner.Scan(sln).Warnings.Any(w => w.Contains("Web Site project 'WebSite1'")), "sln warning");
        True(Scanner.Scan(Path.Combine(temp, "site", "WebSite1")).Warnings.Any(w => w.Contains("looks like a Web Site")), "folder warning");
    });
    Test("Forms auth without Membership is SEC003, not the Membership blocker", () =>
    {
        Fixture("forms/Web/Web.config", "<configuration><system.web><authentication mode=\"Forms\" /></system.web></configuration>");
        var p = ProjectAnalyzer.Analyze(Fixture("forms/Web/Web.csproj", Legacy()), temp);
        True(p.Findings.Any(f => f.Rule == Rules.FormsAuth), "forms auth");
        True(p.Findings.All(f => f.Rule != Rules.MembershipAuth), "no membership");
    });
    Test("solution-wide base hours are charged once", () =>
    {
        const string config = "<configuration><system.web><membership /></system.web></configuration>";
        Fixture("wide/A/Web.config", config);
        Fixture("wide/B/Web.config", config);
        Fixture("wide/A/A.csproj", Legacy());
        Fixture("wide/B/B.csproj", Legacy());
        var r = Scanner.Scan(Path.Combine(temp, "wide"));
        var membership = r.Projects.SelectMany(p => p.Findings).Where(f => f.Rule == Rules.MembershipAuth).ToList();
        Eq(2, membership.Count, "both projects list it");
        Eq(Rules.MembershipAuth.BaseHours, membership.Sum(f => f.EstimatedHours), "base hours once");
    });
    Test("tool version keeps the prerelease part and drops build metadata", () =>
        True(!report.ToolVersion.Contains('+') && report.ToolVersion.StartsWith("0.1.0"), report.ToolVersion));
    Test("a symlink/junction loop does not drop the project", () =>
    {
        var path = Fixture("loop/P/P.csproj", Legacy());
        Fixture("loop/P/src/A.cs", "class A { object x = HttpContext.Current; }");
        var link = Path.Combine(temp, "loop", "P", "src", "loop");
        try { Directory.CreateSymbolicLink(link, Path.Combine(temp, "loop", "P", "src")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows without developer mode cannot create symlinks; a junction behaves the same for the scanner.
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{Path.GetDirectoryName(link)}\"") { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
        }
        True(Directory.Exists(link), "loop link created");
        var p = ProjectAnalyzer.Analyze(path, temp);
        Eq(1, p.Findings.Single(f => f.Rule == Rules.HttpContextCurrent).Occurrences.Count, "file scanned once");
        Directory.Delete(link); // remove the link only, before the recursive cleanup
    });
    Test("nothing to analyze is an error, not an empty 'None' report", () =>
    {
        Directory.CreateDirectory(Path.Combine(temp, "empty"));
        var html = Path.Combine(temp, "empty.html");
        Eq(1, Cli.Run([Path.Combine(temp, "empty"), "-q", "-o", html]), "exit code");
        True(!File.Exists(html), "no report written");
    });
    Test("two projects in one folder produce a warning", () =>
    {
        Fixture("shared/Lib/Lib.csproj", Legacy());
        Fixture("shared/Lib/Lib.Core.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        True(Scanner.Scan(Path.Combine(temp, "shared")).Warnings.Any(w => w.Contains("share the folder")), "warning");
    });
}
finally
{
    try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
}

Console.WriteLine($"\n{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;
