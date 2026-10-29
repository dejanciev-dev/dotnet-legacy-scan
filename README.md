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

# from source
dotnet run --project src/LegacyScan -- path/to/MySolution.sln -o report.html --json report.json
```

Options:

| Option | Description |
|---|---|
| `-o, --out <file>` | HTML report path (default `legacy-scan-report.html`) |
| `--json <file>` | Also write JSON |
| `--brand <text>` / `--contact <text>` | Footer call-to-action (your company + email/URL) |
| `--hours-per-day <n>` | Productive hours per day for the day estimate (default 6) |
| `--fail-on <minor\|major\|blocker>` | Exit code 2 when such findings exist (CI gate) |
| `-q, --quiet` | No console summary |

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
dotnet run --project tests/LegacyScan.Tests     # 42 tests against samples/LegacyShop
dotnet pack src/LegacyScan -c Release           # → nupkg/dotnet-legacy-scan.0.1.0.nupkg
```

`samples/LegacyShop` is a fake legacy solution (Web Forms, WCF + ASMX, MVC 5 + Web API, EF6/EDMX library, VB.NET Crystal Reports, tests, one modern project) used by the tests.

Targets `net10.0` with `RollForward=Major`. To support machines with only .NET 8, change to `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`.

## Roadmap

- Roslyn semantic analysis (exact API usage, fewer false positives)
- NuGet API lookup: does a newer version support net8+/netstandard2.0?
- Per-page Web Forms complexity (controls, ViewState, UpdatePanels, code-behind size)
- Calibrate effort from real migrations
- `--format md` for pull-request comments

## Releasing

Push a tag `vX.Y.Z`. CI builds, tests, packs with that version and publishes to NuGet (needs the `NUGET_API_KEY` repository secret).

## License

MIT
