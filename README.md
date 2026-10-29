# dotnet-legacy-scan

[![CI](https://github.com/dejanciev-dev/dotnet-legacy-scan/actions/workflows/ci.yml/badge.svg)](https://github.com/dejanciev-dev/dotnet-legacy-scan/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**.NET Framework → modern .NET migration readiness scanner.**

Point it at a solution and get a report a CTO can read: what blocks the migration (Web Forms, WCF, ASMX, Membership, Remoting, BinaryFormatter, Crystal Reports…), a rough effort range, and the order to migrate projects in.

- Local and read-only: no code leaves the machine
- Zero dependencies, works offline
- C# and VB.NET, `.sln`, `.slnx`, single project or folder
- Self-contained HTML report (print to PDF) + JSON for CI

## Install / run

```bash
dotnet tool install -g dotnet-legacy-scan
dotnet legacy-scan path/to/MySolution.sln

# one-off run without installing (.NET 10 SDK)
dnx dotnet-legacy-scan path/to/MySolution.sln

# from source
dotnet run --project src/LegacyScan -- path/to/MySolution.sln -o report.html --json report.json
```

Options:

| Option | Description |
|---|---|
| `-o, --out <file>` | HTML report path (default `legacy-scan-report.html`) |
| `--json <file>` | Also write JSON |
| `--md <file>` | Also write a short Markdown summary (for pull requests or the CI job summary) |
| `--exclude <pattern>` | Skip projects whose name or relative path matches (`*`, `?`); repeatable. The report lists what was excluded |
| `--no-snippets` | Leave source code lines out of the HTML/JSON, so the report can be shared outside the team |
| `--brand <text>` / `--contact <text>` | Footer call-to-action (your company + email/URL) |
| `--hours-per-day <n>` | Productive hours per day for the day estimate (default 6) |
| `--fail-on <minor\|major\|blocker>` | Exit code 2 when such findings exist (CI gate) |
| `-q, --quiet` | No console summary |
| `--version` | Print the version |

### In CI (GitHub Actions)

```yaml
- run: dotnet tool install -g dotnet-legacy-scan
- run: dotnet legacy-scan MySolution.sln -o legacy-scan.html --md "$GITHUB_STEP_SUMMARY" --exclude "*.Tests"
- uses: actions/upload-artifact@v6
  with:
    name: legacy-scan
    path: legacy-scan.html
```

Add `--fail-on blocker` to stop new blockers (for example a new Web Forms page) from coming back once they are gone.

## What it checks (37 rules)

- **Project:** legacy csproj format, packages.config, old target framework
- **App models:** Web Forms pages/controls/master pages, MVC 5, Web API 2, Global.asax, custom HTTP modules/handlers, WCF services and clients, ASMX, Workflow Foundation, WinForms/WPF
- **Platform:** Membership / roles / profile, Forms authentication, ASP.NET Identity 2 / OWIN, out-of-process session, EDMX, EF6, System.Data.SqlClient, Crystal Reports / ReportViewer
- **Code:** `HttpContext.Current`, System.Web in libraries, BinaryFormatter, Remoting/AppDomains, Thread.Abort, COM+, CAS, WebClient, ConfigurationManager, Windows-only APIs, HttpRuntime.Cache, Server.MapPath
- **Packages:** known packages not supported on modern .NET (charged once per family, not again when the app-model rule already covers them) or that need their ASP.NET Core variant

Comments and string literals are excluded from code matching.

## Estimates

Each rule carries a heuristic effort (base + per occurrence), see `Rules.cs`. Solution-wide concerns (Membership/Identity, reporting platform, Workflow, COM+) pay their base hours once per solution. Totals add 3h per project and 30% regression testing; the range is ×0.75–×1.6. It is a planning starting point, not a quote. Tune the numbers with your own project history.

## Contributing

Issues and pull requests are welcome, especially new rules and false-positive reports. Please include a minimal sample project when reporting a detection problem.

## Development

```bash
dotnet build
dotnet run --project tests/LegacyScan.Tests     # 47 tests against samples/LegacyShop
dotnet pack src/LegacyScan -c Release           # → nupkg/dotnet-legacy-scan.0.1.0.nupkg
```

`samples/LegacyShop` is a fake legacy solution (Web Forms, WCF + ASMX, MVC 5 + Web API, EF6/EDMX library, VB.NET Crystal Reports, tests, one modern project) used by the tests.

Targets `net10.0` (the current LTS) with `RollForward=Major`, so it also runs on newer runtimes. .NET 8 and 9 reach end of support on 10 November 2026, so they are not targeted.

## Roadmap

- Roslyn semantic analysis (exact API usage, fewer false positives)
- NuGet API lookup: does a newer version support net8+/netstandard2.0?
- Use per-page Web Forms complexity (controls, UpdatePanels and code-behind size are already shown per page) in the estimate, once calibrated
- Calibrate effort from real migrations

## Releasing

Push a tag `vX.Y.Z`. CI builds, tests, packs with that version and publishes to NuGet (needs the `NUGET_API_KEY` repository secret).

## License

MIT
