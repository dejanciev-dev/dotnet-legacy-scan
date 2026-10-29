# dotnet-legacy-scan

**.NET Framework → modern .NET migration readiness scanner.**

Point it at a solution and get a report a CTO can read: what blocks the migration (Web Forms, WCF, ASMX, Membership, Remoting, BinaryFormatter, Crystal Reports…), a rough effort range, and the order to migrate projects in.

- **Local and read-only:** nothing leaves the machine, nothing in the solution is changed
- **Zero dependencies**, works offline
- **C# and VB.NET:** `.sln`, `.slnx`, a single `.csproj`/`.vbproj`, or a folder
- **Outputs:** self-contained HTML report (prints to PDF), JSON, Markdown summary for pull requests and CI

![Migration readiness report](https://raw.githubusercontent.com/dejanciev-dev/dotnet-legacy-scan/main/docs/report-screenshot.png)

## Usage

```bash
dotnet tool install -g dotnet-legacy-scan
dotnet legacy-scan path/to/MySolution.sln

# one-off run without installing (.NET 10 SDK)
dnx dotnet-legacy-scan path/to/MySolution.sln
```

Useful options: `--json <file>`, `--md <file>` (e.g. `"$GITHUB_STEP_SUMMARY"`), `--exclude <pattern>`, `--no-snippets` (leave source code out so the report can be shared), `--fail-on <minor|major|blocker>` (exit code 2 for CI gates). Run `dotnet legacy-scan --help` for all options.

The effort range is a heuristic starting point for planning, not a quote.

How it works, the 37 rules, the estimate formula and the limitations are documented on [GitHub](https://github.com/dejanciev-dev/dotnet-legacy-scan#readme). False-positive reports are very welcome as [issues](https://github.com/dejanciev-dev/dotnet-legacy-scan/issues).
