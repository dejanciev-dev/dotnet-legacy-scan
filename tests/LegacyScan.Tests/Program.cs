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
    Eq(5, F("LegacyShop.Admin", Rules.PackageNeedsReplacement)!.Occurrences.Count, "packages to replace");
});
Test("Reports (VB): Crystal via assembly reference, Thread.Abort once (comment ignored)", () =>
{
    True(F("LegacyShop.Reports", Rules.Reporting) is not null, "crystal");
    Eq(1, F("LegacyShop.Reports", Rules.ThreadAbort)!.Occurrences.Count, "abort");
});

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

Console.WriteLine($"\n{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;
