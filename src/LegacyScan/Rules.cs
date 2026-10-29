namespace LegacyScan;

/// <summary>
/// Rule catalogue. Hours are deliberately conservative heuristics for an experienced .NET developer,
/// excluding regression testing (added globally by <see cref="Estimator"/>).
/// </summary>
public static class Rules
{
    // ---------- Project format ----------
    public static readonly Rule LegacyProjectFormat = new("PRJ001", "Legacy (non-SDK) project format", Severity.Minor, "Project",
        2, 0, "Convert to SDK-style csproj (by hand or with Microsoft's upgrade tooling). Do this first; it makes every later step easier and can be done while still targeting net48.");

    public static readonly Rule PackagesConfig = new("PRJ002", "packages.config NuGet format", Severity.Minor, "Project",
        1, 0, "Migrate to PackageReference (Visual Studio: right-click packages.config → Migrate; not offered for ASP.NET web projects, convert those by hand or as part of the SDK-style conversion). Review transitive dependencies afterwards.");

    public static readonly Rule OldFramework = new("PRJ003", "Targets an old .NET Framework version (< 4.7.2)", Severity.Minor, "Project",
        2, 0, "Retarget to net48 first. It gives netstandard2.0 compatibility and lets you share libraries with the new .NET app during an incremental migration.");

    // ---------- Application models ----------
    public static readonly Rule WebForms = new("APP001", "ASP.NET Web Forms pages", Severity.Blocker, "Web",
        16, 6, "Web Forms does not exist on modern .NET. Rewrite the UI (Blazor or Razor Pages). Migrate incrementally: put an ASP.NET Core app with YARP in front, move pages one by one, and share session/auth with System.Web adapters.");

    public static readonly Rule WebFormsControls = new("APP002", "Web Forms user controls / master pages", Severity.Blocker, "Web",
        0, 3, "Rewrite as Blazor components / Razor layouts and partials.");

    public static readonly Rule Mvc5 = new("APP003", "ASP.NET MVC 5 (System.Web.Mvc)", Severity.Major, "Web",
        12, 1.5, "Move controllers to ASP.NET Core MVC. Most action code ports with small changes; filters, model binders, bundling, routing and HtmlHelpers need rework.");

    public static readonly Rule MvcViews = new("APP004", "Razor views (MVC 5)", Severity.Minor, "Web",
        0, 0.5, "Views usually port with small changes: replace @Html/@Url helpers, bundling (@Scripts/@Styles) and child actions with tag helpers / view components.");

    public static readonly Rule WebApi2 = new("APP005", "ASP.NET Web API 2 (System.Web.Http)", Severity.Major, "Web",
        8, 1, "Port ApiControllers to ASP.NET Core controllers. Check message handlers, formatters and OWIN middleware.");

    public static readonly Rule GlobalAsax = new("APP006", "Global.asax application events", Severity.Major, "Web",
        4, 0, "Move Application_Start/Error/BeginRequest logic into Program.cs and middleware.");

    public static readonly Rule HttpModulesHandlers = new("APP007", "Custom IHttpModule / IHttpHandler", Severity.Major, "Web",
        0, 4, "Rewrite as ASP.NET Core middleware or endpoints.");

    public static readonly Rule WcfService = new("APP008", "WCF service hosting (.svc / system.serviceModel services)", Severity.Blocker, "Services",
        8, 8, "Server-side WCF is not part of modern .NET. Options: CoreWCF (closest, keeps contracts and clients), or redesign to gRPC / REST. Choose per client: who consumes this service and can they change?");

    public static readonly Rule WcfClient = new("APP009", "WCF client / service references", Severity.Major, "Services",
        2, 3, "Regenerate clients with dotnet-svcutil (System.ServiceModel.* packages). Only some bindings are supported (basicHttp, netTcp, wsHttp partially); check each endpoint's binding.");

    public static readonly Rule Asmx = new("APP010", "ASMX web services", Severity.Blocker, "Services",
        4, 6, "Not supported on modern .NET. Re-implement as Web API endpoints or host with CoreWCF (basicHttpBinding) to keep the SOAP contract for existing clients.");

    public static readonly Rule Workflow = new("APP011", "Windows Workflow Foundation", Severity.Blocker, "Services",
        24, 8, "WF is not supported on modern .NET. Consider CoreWF (community port) or redesign with a workflow/orchestration library.", SolutionWide: true);

    public static readonly Rule DesktopApp = new("APP012", "Desktop UI (WinForms / WPF)", Severity.Minor, "Desktop",
        8, 0, "Supported on net10.0-windows. Check third-party control suites, designer files, ClickOnce deployment and app.config settings.");

    // ---------- Platform features ----------
    public static readonly Rule MembershipAuth = new("SEC001", "ASP.NET Membership / Forms Authentication / Roles", Severity.Blocker, "Security",
        24, 0, "Move to ASP.NET Core Identity (or an external IdP). Plan the user/password-hash data migration and keep a compatible password hasher so users do not need to reset passwords.", SolutionWide: true);

    public static readonly Rule AspNetIdentity2 = new("SEC002", "ASP.NET Identity 2 / OWIN auth", Severity.Major, "Security",
        12, 0, "Move to ASP.NET Core Identity. Schema is similar but not identical; plan a migration script.", SolutionWide: true);

    public static readonly Rule FormsAuth = new("SEC003", "Forms Authentication with custom login (no Membership provider)", Severity.Major, "Security",
        6, 0, "Replace with ASP.NET Core cookie authentication and port the login code (FormsAuthentication.* calls). During incremental migration, System.Web adapters can share authentication between the old and new app.", SolutionWide: true);

    public static readonly Rule SessionState = new("WEB001", "ASP.NET Session state (usage or out-of-process mode)", Severity.Major, "Web",
        6, 0.25, "ASP.NET Core session is different (no SQLServer/StateServer modes, values are byte[]). During incremental migration share session via System.Web adapters.");

    public static readonly Rule Edmx = new("DATA001", "Entity Framework EDMX model (database-first designer)", Severity.Major, "Data",
        4, 8, "EF6 (6.4+) runs on modern .NET, but the EDMX designer does not. Short term: keep EF6 with code-based configuration. Long term: reverse-engineer to EF Core.");

    public static readonly Rule Ef6Package = new("DATA002", "Entity Framework 6", Severity.Minor, "Data",
        2, 0, "EF 6.4+ runs on modern .NET. Upgrade the package first and migrate to EF Core later as a separate step to reduce risk.");

    public static readonly Rule SystemDataSqlClient = new("DATA003", "System.Data.SqlClient", Severity.Minor, "Data",
        0, 0.1, "Switch to Microsoft.Data.SqlClient (note: Encrypt=true is the default, which may require TrustServerCertificate or proper certificates).");

    public static readonly Rule Reporting = new("RPT001", "Crystal Reports / ReportViewer", Severity.Blocker, "Reporting",
        16, 4, "No direct support on modern .NET web. Options: keep a small .NET Framework reporting service, move to SSRS/Power BI embedding, or a .NET reporting library.", SolutionWide: true);

    // ---------- Code patterns ----------
    public static readonly Rule HttpContextCurrent = new("CODE001", "HttpContext.Current (static access)", Severity.Major, "Code",
        0, 0.5, "Inject IHttpContextAccessor or pass the needed values explicitly. In shared libraries this is often the biggest hidden cost.");

    public static readonly Rule SystemWebInLibrary = new("CODE002", "System.Web used in a non-web library", Severity.Major, "Code",
        2, 0.5, "Remove System.Web dependencies from business libraries (abstractions for request/user/cache) so they can multi-target net48 and net10.0.");

    public static readonly Rule BinaryFormatter = new("CODE003", "BinaryFormatter / NetDataContractSerializer", Severity.Major, "Code",
        2, 2, "BinaryFormatter is removed from .NET 9+. Replace with System.Text.Json, DataContractSerializer or MessagePack. Check persisted data that was serialized with it.");

    public static readonly Rule RemotingAppDomain = new("CODE004", ".NET Remoting / AppDomain creation", Severity.Blocker, "Code",
        8, 4, "Not supported. Replace remoting with gRPC/HTTP; replace AppDomain isolation with AssemblyLoadContext or separate processes.");

    public static readonly Rule ThreadAbort = new("CODE005", "Thread.Abort / Suspend / Resume", Severity.Major, "Code",
        0, 2, "Throws PlatformNotSupportedException. Use CancellationToken-based cooperative cancellation.");

    public static readonly Rule EnterpriseServices = new("CODE006", "System.EnterpriseServices (COM+)", Severity.Blocker, "Code",
        16, 4, "Not supported. Redesign transactional components; for distributed transactions see System.Transactions (Windows only on .NET 7+).", SolutionWide: true);

    public static readonly Rule CodeAccessSecurity = new("CODE007", "Code Access Security (CAS) APIs", Severity.Minor, "Code",
        0, 0.25, "CAS is not supported; remove the attributes/calls (they are no-ops or throw).");

    public static readonly Rule WebClientObsolete = new("CODE008", "WebClient / HttpWebRequest (obsolete)", Severity.Minor, "Code",
        0, 0.5, "Works but obsolete (SYSLIB0014). Move to HttpClient via IHttpClientFactory.");

    public static readonly Rule ConfigurationManager = new("CODE009", "ConfigurationManager / web.config appSettings", Severity.Minor, "Code",
        1, 0.1, "Either add System.Configuration.ConfigurationManager package (quick) or move to IConfiguration/Options (recommended for web apps).");

    public static readonly Rule WindowsOnlyApis = new("CODE010", "Windows-only APIs (Registry, System.Drawing, EventLog, WMI)", Severity.Info, "Code",
        0, 0.1, "Fine on Windows. If you plan to run on Linux/containers, these need replacing (e.g. System.Drawing → ImageSharp/SkiaSharp).");

    public static readonly Rule HttpRuntimeCache = new("CODE011", "HttpRuntime.Cache / System.Web.Caching", Severity.Minor, "Code",
        0, 0.5, "Replace with IMemoryCache / IDistributedCache.");

    public static readonly Rule ServerMapPath = new("CODE012", "Server.MapPath / HostingEnvironment", Severity.Minor, "Code",
        0, 0.25, "Use IWebHostEnvironment.ContentRootPath / WebRootPath.");

    // ---------- Packages ----------
    public static readonly Rule PackageNeedsReplacement = new("PKG001", "NuGet package not supported on modern .NET (needs replacement)", Severity.Major, "Packages",
        0, 3, "Find the ASP.NET Core / modern replacement for each listed package family.");

    public static readonly Rule PackageNeedsUpgrade = new("PKG002", "NuGet package needs a major upgrade or swap to its ASP.NET Core variant", Severity.Minor, "Packages",
        0, 1, "Upgrade to a version that supports netstandard2.0/net8+ or switch to the .AspNetCore / .Extensions.DependencyInjection variant.");

    /// <summary>
    /// A known package (matched by id prefix). Packages of the same <see cref="Family"/> are charged once.
    /// When <see cref="CoveredBy"/> already matched in the project, its cost includes this package and it is skipped.
    /// </summary>
    public sealed record KnownPackage(string Prefix, Rule Rule, string Note, Rule? CoveredBy = null, string? Family = null)
    {
        public string FamilyKey => Family ?? Prefix;
    }

    public static readonly KnownPackage[] KnownPackages =
    [
        new("Microsoft.AspNet.Mvc", PackageNeedsReplacement, "→ ASP.NET Core MVC (built in)", Mvc5),
        new("Microsoft.AspNet.WebApi", PackageNeedsReplacement, "→ ASP.NET Core controllers (built in)", WebApi2),
        new("Microsoft.AspNet.Web.Optimization", PackageNeedsReplacement, "bundling → WebOptimizer / build-time bundling", Mvc5),
        new("Microsoft.AspNet.Razor", PackageNeedsReplacement, "→ ASP.NET Core Razor (built in)", Mvc5, "Microsoft.AspNet.Razor"),
        new("Microsoft.AspNet.WebPages", PackageNeedsReplacement, "→ ASP.NET Core Razor (built in)", Mvc5, "Microsoft.AspNet.Razor"),
        new("Microsoft.AspNet.SignalR", PackageNeedsReplacement, "→ ASP.NET Core SignalR (protocol not compatible: clients must change)"),
        new("Microsoft.AspNet.Identity", PackageNeedsReplacement, "→ Microsoft.AspNetCore.Identity", AspNetIdentity2),
        new("Microsoft.Owin", PackageNeedsReplacement, "OWIN middleware → ASP.NET Core middleware"),
        new("Owin", PackageNeedsReplacement, "OWIN → ASP.NET Core", Family: "Microsoft.Owin"),
        new("WebGrease", PackageNeedsReplacement, "bundling → build-time bundling", Mvc5, "Microsoft.AspNet.Web.Optimization"),
        new("Microsoft.AspNet.FriendlyUrls", PackageNeedsReplacement, "Web Forms only", WebForms, "WebForms"),
        new("Microsoft.AspNet.ScriptManager", PackageNeedsReplacement, "Web Forms only", WebForms, "WebForms"),
        new("AjaxControlToolkit", PackageNeedsReplacement, "Web Forms only", WebForms, "WebForms"),
        new("Elmah", PackageNeedsReplacement, "→ ILogger + Serilog/Seq/Application Insights"),
        new("DotNetOpenAuth", PackageNeedsReplacement, "→ ASP.NET Core authentication handlers"),
        new("Microsoft.ReportViewer", Reporting, "ReportViewer controls"),
        new("Microsoft.ReportingServices.ReportViewerControl", Reporting, "ReportViewer controls", Family: "Microsoft.ReportViewer"),
        new("CrystalDecisions", Reporting, "Crystal Reports"),
        new("Ninject.Web", PackageNeedsUpgrade, "→ Microsoft.Extensions.DependencyInjection or Ninject core"),
        new("Autofac.Mvc", PackageNeedsUpgrade, "→ Autofac.Extensions.DependencyInjection", Family: "Autofac"),
        new("Autofac.WebApi", PackageNeedsUpgrade, "→ Autofac.Extensions.DependencyInjection", Family: "Autofac"),
        new("Unity.Mvc", PackageNeedsUpgrade, "→ Unity.Microsoft.DependencyInjection", Family: "Unity"),
        new("Unity.AspNet.WebApi", PackageNeedsUpgrade, "→ Unity.Microsoft.DependencyInjection", Family: "Unity"),
        new("Microsoft.ApplicationInsights.Web", PackageNeedsUpgrade, "→ Microsoft.ApplicationInsights.AspNetCore"),
        new("Swashbuckle.Core", PackageNeedsUpgrade, "→ Swashbuckle.AspNetCore"),
        new("log4net", PackageNeedsUpgrade, "upgrade to 2.0.10+ (or move to Microsoft.Extensions.Logging)"),
    ];

    /// <summary>Packages that match a known prefix but already support netstandard2.0 / modern .NET.</summary>
    public static readonly HashSet<string> SupportedOnModern = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.AspNet.WebApi.Client", "Microsoft.AspNet.SignalR.Client",
    };

    public static IEnumerable<Rule> All =>
        typeof(Rules).GetFields().Where(f => f.FieldType == typeof(Rule)).Select(f => (Rule)f.GetValue(null)!);
}
