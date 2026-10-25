---
name: dotnet-scaffold
description: Scaffold a new .NET solution, project, test project, or complete feature/entity/vertical slice (command, handler, endpoint, validation, tests) following repository conventions. Use when creating something new from scratch — new project, new test project, new entity, or CQRS/Vertical Slice feature. For engineering guidance on existing code use the dotnet skill, and for Aspire-specific workflows use the Aspire skill.
license: MIT
metadata:
  author: Timy
  version: "1.1"
---

# .NET Scaffolding

Use this workflow to scaffold a complete, working .NET solution, project, or feature. This skill owns the scaffolding workflow; when the `dotnet` skill is available, apply its engineering guidance rather than duplicating it here.

## Scope

Determine the scaffolding target:
1. **New solution/repository** — create solution structure, central build/package policies, and initial projects.
2. **New project** — add an application, worker, or test project to an existing solution.
3. **New feature/entity** — add a vertical slice or feature to an existing project.

Do not turn a feature request into a solution redesign. Greenfield archetypes and `assets/SliceTemplate/` apply **only** when the repository has no established convention. When working in an existing codebase, follow its existing conventions, architecture, and dependencies.

## Prerequisites

Automated scaffolding via `scripts/scaffold.cs` requires the **.NET 10 SDK or later** (for file-based C# apps and modern `slnx` solutions). Before executing, verify the installed SDK via `dotnet --version`. If running on an older SDK, stop and report the environment constraint to the user; do not guess or manually generate mismatched files.

## Workflow

### 1. Inspect Environment & Conventions
Before generating code, inspect:
- Repository instructions and conventions.
- Installed SDK version (`dotnet --version`) and project templates (`dotnet new list`).
- Existing target framework (`TargetFramework`) and language version (`LangVersion`).
- Solution layout and Central Package Management (`Directory.Packages.props`).
- Existing feature structure, DI wiring, and testing patterns.

### 2. Scaffold Greenfield Solution
For a new solution, invoke the deterministic scaffolding script rather than re-deriving configuration:

```bash
dotnet run <path-to-skill>/scripts/scaffold.cs -- -n <SolutionName> -o <OutputDir> [--with-testcontainers]
```

The script automates:
- Modern XML solution creation (`dotnet new sln --format slnx`).
- Tooling configuration (`global.json` pinning SDK and Microsoft.Testing.Platform runner).
- Central Package Management (`Directory.Packages.props`) and build policies (`Directory.Build.props`).
- Web API and test projects referencing reusable templates in `assets/`.
- Dual-configuration verification (`dotnet build` and `dotnet test` in Debug and Release).

Reusable asset definitions reside in `assets/`:
- `Directory.Build.props`: Enforces central compiler warnings and active LTS (`net10.0`).
- `Directory.Packages.props`: Central package management skeleton.
- `global.json`: SDK feature band and MTP runner pin.
- `.editorconfig`: Standard .NET code formatting rules.
- `TestProject.csproj`: Canonical `MSTest.Sdk` test project template without inline versions.

### 3. Scaffold Test Project
When adding a test project to an existing solution:
- **Default Test Framework:** Current `MSTest.Sdk` (MTP-native, standalone executable). Reference `assets/TestProject.csproj`.
- **Alternative Framework:** `xUnit v3` for existing xUnit solutions.
- **Relational Persistence (Conditional):** Include `Testcontainers` (`Testcontainers.PostgreSql`, `Testcontainers.MsSql`) and `Respawn` **only** when the project uses relational persistence.
- **Docker Prerequisite:** Testcontainers requires a running Docker daemon. If Docker is unavailable in the environment, report this clearly in the verification summary; **never silently fall back to the EF Core in-memory provider** because in-memory providers do not enforce relational constraints, transactions, or SQL semantics.

### 4. Scaffold Feature / Vertical Slice
When adding a feature to a Minimal API project without prior conventions, use the slice template in `assets/SliceTemplate/`:
```text
Features/<Area>/<Action>/
├── <Action>Endpoint.cs    // Implements IEndpoint; maps route with TypedResults
├── <Action>Request.cs     // Input DTO / parameters
├── <Action>Handler.cs     // Domain / use-case execution
└── <Action>Response.cs    // Output DTO with static From factory
```

Key rules:
- Endpoints implement `IEndpoint` (`Map(IEndpointRouteBuilder endpoints)`).
- Return typed results: `Results<Created<TResponse>, ProblemHttpResult>`.
- Inject handlers directly into endpoints; avoid mediator overhead unless justified.
- Do not use `.WithOpenApi()`; it is deprecated in .NET 10 (`ASPDEPR002`). ASP.NET Core built-in OpenAPI tracks endpoints automatically.
- Follow the dependency decision order in the `dotnet` skill: framework capability -> existing dependency -> small explicit implementation -> approved third-party package.

### 5. Verify
Always verify scaffolding with real commands:
1. Restore dependencies: `dotnet restore`
2. Build solution in both configurations: `dotnet build -c Debug` and `dotnet build -c Release`
3. Run tests: `dotnet test -c Debug` and `dotnet test -c Release`
4. Report any check that could not run.

## Completeness Checklist

Verify applicable items before reporting completion:
- [ ] Solution/project structure follows conventions (or modern archetype for greenfield)
- [ ] Central Package Management (`Directory.Packages.props`) used; no inline package versions in `.csproj`
- [ ] Target framework set appropriately (defaulting to installed LTS `net10.0` for greenfield)
- [ ] Endpoint and handler contracts well-defined with cancellation token propagation
- [ ] Result/error mapped explicitly to HTTP status codes / ProblemDetails
- [ ] No deprecated APIs used (e.g. no `.WithOpenApi()`)
- [ ] Test project configured with `MSTest.Sdk` or repository test standard
- [ ] Testcontainers included conditionally only for relational persistence; Docker requirement reported
- [ ] Solution builds and tests pass in both Debug and Release

## Reporting Contract

When scaffolding completes, output a structured report:
1. **Scaffolded Scope:** Type of artifact created (solution, project, or feature slice).
2. **Files Created / Modified:** Explicit list of created or altered files.
3. **Architecture & Dependency Decisions:** Selected frameworks, CPM additions, or deviations from repo defaults.
4. **Verification Results:** Logs and exit codes for `dotnet build` and `dotnet test` (Debug & Release).
5. **Environment Constraints / Follow-ups:** Any prerequisites (e.g., Docker for Testcontainers) or manual steps required.
