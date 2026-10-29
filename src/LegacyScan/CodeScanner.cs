using System.Text;
using System.Text.RegularExpressions;

namespace LegacyScan;

/// <summary>
/// Lightweight, dependency-free source scanner. Comments and string literals are blanked out
/// (line structure preserved) so patterns only match real code.
/// </summary>
public static class CodeScanner
{
    public sealed record Pattern(Rule Rule, Regex Regex, Func<ProjectReport, bool>? AppliesTo = null);

    private static Regex R(string p) => new(p, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool IsNonWeb(ProjectReport p) =>
        p.Kind is ProjectKind.ClassLibrary or ProjectKind.Console or ProjectKind.Test or ProjectKind.WinForms or ProjectKind.Wpf;

    public static readonly Pattern[] Patterns =
    [
        new(Rules.HttpContextCurrent, R(@"\bHttpContext\.Current\b")),
        new(Rules.SystemWebInLibrary, R(@"^\s*(using|Imports)\s+System\.Web\b"), IsNonWeb),
        new(Rules.BinaryFormatter, R(@"\b(BinaryFormatter|NetDataContractSerializer|SoapFormatter)\b")),
        new(Rules.RemotingAppDomain, R(@"\bAppDomain\.CreateDomain\b|\bSystem\.Runtime\.Remoting\b|\bRemotingConfiguration\b|(:|Inherits)\s*MarshalByRefObject\b")),
        // Only receivers named like a thread ("_workerThread", "thread2"): worker.Abort() is missed to avoid HttpWebRequest/ThreadPool false positives.
        new(Rules.ThreadAbort, R(@"\bThread\.CurrentThread\.(Abort|Suspend|Resume)\b|\b\w*[Tt]hread\d*\.(Abort|Suspend|Resume)\s*\(\s*\)")),
        new(Rules.EnterpriseServices, R(@"\bSystem\.EnterpriseServices\b|\bServicedComponent\b")),
        new(Rules.CodeAccessSecurity, R(@"[\[<]\s*(assembly:\s*)?(SecurityPermission|FileIOPermission|ReflectionPermission|PermissionSet|AllowPartiallyTrustedCallers)\b|\bnew\s+(PermissionSet|SecurityPermission|FileIOPermission)\s*\(")),
        new(Rules.WebClientObsolete, R(@"\bnew\s+WebClient\b|\bWebRequest\.Create\b|\bHttpWebRequest\b")),
        new(Rules.ConfigurationManager, R(@"\bConfigurationManager\.(AppSettings|ConnectionStrings|GetSection)\b|\bWebConfigurationManager\b")),
        new(Rules.WindowsOnlyApis, R(@"\bRegistry(Key)?\.\w|\bSystem\.Drawing\b|\bEventLog\b|\bSystem\.Management\b|\bManagementObjectSearcher\b")),
        new(Rules.HttpRuntimeCache, R(@"\bHttpRuntime\.Cache\b|\bSystem\.Web\.Caching\b")),
        new(Rules.ServerMapPath, R(@"\bServer\.MapPath\b|\bHostingEnvironment\.MapPath\b")),
        new(Rules.SystemDataSqlClient, R(@"\bSystem\.Data\.SqlClient\b")),
        new(Rules.HttpModulesHandlers, R(@"(:|Implements)\s*([\w.]+\s*,\s*)*(IHttpModule|IHttpHandler|IHttpAsyncHandler)\b")),
        new(Rules.WcfClient, R(@"\bClientBase\s*(<|\(Of)|\bChannelFactory\s*(<|\(Of)")),
        // Session[...] on the page/controller, not someObject.Session[...]
        new(Rules.SessionState, R(@"(?<![\w.])Session\s*(\[|\.(Add|Remove|Abandon|Clear|Contents)\b)|\b(HttpContext\.Current|Context|Page|this)\.Session\b")),
    ];

    /// <summary>Designer and service-reference code is generated: counted as lines, not scanned for patterns.</summary>
    internal static bool IsGenerated(string relPath)
    {
        var name = Path.GetFileName(relPath);
        if (name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase))
            return true;
        return relPath.Split('/', '\\').Any(d => d.Equals("Service References", StringComparison.OrdinalIgnoreCase)
            || d.Equals("Web References", StringComparison.OrdinalIgnoreCase) || d.Equals("Connected Services", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Scans one file and adds occurrences to the per-rule dictionary.</summary>
    public static void ScanFile(string path, string relPath, ProjectReport project, Dictionary<Rule, Finding> findings)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        var isVb = path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase);
        var original = text.Split('\n');
        var lines = (isVb ? StripVb(text) : StripCSharp(text)).Split('\n');
        project.LinesOfCode += original.Count(l => !string.IsNullOrWhiteSpace(l));
        if (IsGenerated(relPath)) return;

        foreach (var pattern in Patterns)
        {
            if (pattern.AppliesTo is not null && !pattern.AppliesTo(project)) continue;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!pattern.Regex.IsMatch(lines[i])) continue;
                Add(findings, pattern.Rule, new Occurrence(relPath, i + 1, original[i].Trim().TrimEnd('\r')));
                // SystemWebInLibrary: count once per file
                if (pattern.Rule == Rules.SystemWebInLibrary) break;
            }
        }
    }

    public static void Add(Dictionary<Rule, Finding> findings, Rule rule, Occurrence? occurrence = null)
    {
        if (!findings.TryGetValue(rule, out var f)) findings[rule] = f = new Finding { Rule = rule };
        if (occurrence is not null) f.Occurrences.Add(occurrence with { Snippet = Truncate(occurrence.Snippet, 160) });
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    /// Blanks C# comments, strings and chars, keeping newlines so line numbers stay valid.
    /// Code inside interpolation holes (<c>$"{HttpContext.Current.User}"</c>) is kept.
    /// </summary>
    public static string StripCSharp(string s)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;
        StripCode(s, ref i, sb, inHole: false);
        return sb.ToString();
    }

    /// <summary>Copies code, blanking comments and literals. In an interpolation hole, stops at the closing '}' (not consumed).</summary>
    private static void StripCode(string s, ref int i, StringBuilder sb, bool inHole)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < s.Length && s[i] != '\n') { sb.Append(' '); i++; }
            }
            else if (c == '/' && next == '*')
            {
                sb.Append("  "); i += 2;
                while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/')) { sb.Append(Blank(s[i])); i++; }
                if (i < s.Length) { sb.Append("  "); i += 2; }
            }
            else if (c is '"' or '$' or '@' && IsStringStart(s, i))
            {
                StripString(s, ref i, sb);
            }
            else if (c == '\'' && i + 2 < s.Length && (s[i + 2] == '\'' || (s[i + 1] == '\\' && i + 3 < s.Length)))
            {
                // char literal 'x' or '\n' / '\u0000'
                sb.Append('\''); i++;
                while (i < s.Length && s[i] != '\'' && s[i] != '\n')
                {
                    if (s[i] == '\\' && i + 1 < s.Length) { sb.Append("  "); i += 2; continue; }
                    sb.Append(' '); i++;
                }
                if (i < s.Length && s[i] == '\'') { sb.Append('\''); i++; }
            }
            else
            {
                if (inHole && c == '{') depth++;
                else if (inHole && c == '}' && depth-- == 0) return;
                sb.Append(c); i++;
            }
        }
    }

    private static bool IsStringStart(string s, int i)
    {
        while (i < s.Length && s[i] is '$' or '@') i++;
        return i < s.Length && s[i] == '"';
    }

    /// <summary>Regular, verbatim (@), interpolated ($) and raw ("""…""", any number of quotes and $) string literals.</summary>
    private static void StripString(string s, ref int i, StringBuilder sb)
    {
        var dollars = 0;
        var verbatim = false;
        for (; s[i] != '"'; i++)
        {
            if (s[i] == '$') dollars++; else verbatim = true;
            sb.Append(s[i]);
        }

        var quotes = 0;
        while (i + quotes < s.Length && s[i + quotes] == '"') quotes++;
        if (quotes >= 3)
        {
            // raw string: closed by the same number of quotes; holes open with as many '{' as there are '$'
            sb.Append('"', quotes); i += quotes;
            while (i < s.Length)
            {
                if (IsRun(s, i, '"', quotes)) { sb.Append('"', quotes); i += quotes; return; }
                if (dollars > 0 && IsRun(s, i, '{', dollars)) { Hole(s, ref i, sb, dollars); continue; }
                sb.Append(Blank(s[i])); i++;
            }
            return;
        }

        sb.Append('"'); i++;
        while (i < s.Length)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            if (!verbatim && c == '\n') return; // unterminated: stop at end of line
            if (verbatim && c == '"' && next == '"') { sb.Append("  "); i += 2; continue; }
            if (c == '"') { sb.Append('"'); i++; return; }
            if (!verbatim && c == '\\' && next != '\0') { sb.Append("  "); i += 2; continue; }
            if (dollars > 0 && c is '{' or '}' && next == c) { sb.Append("  "); i += 2; continue; } // {{ }} escapes
            if (dollars > 0 && c == '{') { Hole(s, ref i, sb, 1); continue; }
            sb.Append(Blank(c)); i++;
        }
    }

    private static void Hole(string s, ref int i, StringBuilder sb, int braces)
    {
        sb.Append('{', braces); i += braces;
        StripCode(s, ref i, sb, inHole: true);
        for (var k = 0; k < braces && i < s.Length && s[i] == '}'; k++) { sb.Append('}'); i++; }
    }

    private static bool IsRun(string s, int i, char c, int count)
    {
        if (i + count > s.Length) return false;
        for (var k = 0; k < count; k++) if (s[i + k] != c) return false;
        return true;
    }

    private static char Blank(char c) => c == '\n' ? '\n' : ' ';

    /// <summary>Blanks VB.NET comments (' and REM) and strings ("" escapes). Strings may span lines (VB 14).</summary>
    public static string StripVb(string s)
    {
        var sb = new StringBuilder(s.Length);
        var inString = false;
        foreach (var rawLine in s.Split('\n'))
        {
            var line = rawLine;
            var lineSb = new StringBuilder(line.Length);
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inString)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { lineSb.Append("  "); i++; continue; }
                    if (c == '"') { inString = false; lineSb.Append('"'); continue; }
                    lineSb.Append(' ');
                    continue;
                }
                if (c == '"') { inString = true; lineSb.Append('"'); continue; }
                if (c == '\'' || IsRemAt(line, i))
                {
                    lineSb.Append(new string(' ', line.Length - i));
                    break;
                }
                lineSb.Append(c);
            }
            sb.Append(lineSb).Append('\n');
        }
        return sb.ToString(0, Math.Max(0, sb.Length - 1));
    }

    private static bool IsRemAt(string line, int i) =>
        i + 3 <= line.Length
        && string.Compare(line, i, "REM", 0, 3, StringComparison.OrdinalIgnoreCase) == 0
        && (i == 0 || char.IsWhiteSpace(line[i - 1]))
        && (i + 3 == line.Length || char.IsWhiteSpace(line[i + 3]));
}
