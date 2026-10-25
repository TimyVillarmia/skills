---
name: dotnet-scaffold
description: Scaffold a new .NET solution, project, test project, or complete feature/entity/vertical slice (command, handler, endpoint, validation, tests) following repository conventions. Use when creating something new from scratch — new project, new test project, new entity, or CQRS/Vertical Slice feature. For engineering guidance on existing code use the dotnet skill, and for Aspire-specific workflows use the Aspire skill.
license: MIT
metadata:
  author: Timy
  version: "1.0"
---

# .NET Scaffolding

Use this workflow to create a complete, working .NET solution, project, or feature. This skill owns the scaffolding workflow; when the `dotnet` skill is available, apply its general .NET engineering guidance rather than duplicating it here.

## Scope

First determine what is being scaffolded:

1. **New solution/repository** — establish the solution and required projects.
2. **New project** — add a project to an existing solution/repository.
3. **New feature/entity** — add a complete vertical slice or feature to an existing project.

Do not turn a feature-scaffolding request into a broader solution redesign. Do not impose a project template, architecture, mediator, mapper, validation library, or other dependency before inspecting requirements and the repository.

## Workflow

### 1. Understand the request

Identify the minimum information needed to scaffold correctly:

- intended application/project type and purpose
- required capabilities and boundaries
- feature/entity name and operations when applicable
- persistence, external integrations, or infrastructure requirements
- testing and deployment/runtime expectations when relevant

Do not ask questions that can be answered by inspecting the repository or environment.

### 2. Inspect the environment

Before generating files, inspect:

- repository instructions and conventions
- installed .NET SDKs and relevant workloads/templates
- target framework and C# language version when already established
- solution/project structure
- existing dependencies and package versions
- entry points, DI/registration, endpoint organization, persistence, and test projects
- existing architecture and representative features

For a genuinely new project, establish the target SDK/framework and required capabilities before selecting the project shape.

### 3. Choose the project shape and architecture

Use the requirements and repository evidence to decide:

- solution/project structure
- executable/library/API/worker/web shape
- boundaries and dependency direction
- whether CQRS, Vertical Slice, Clean Architecture, Modular Monolith, or other patterns provide concrete value

Prefer the simplest architecture that protects the real boundaries and requirements. Existing repository conventions take precedence. For a genuinely new project, apply the `dotnet` skill's personal defaults only after requirements and architecture have been established.

When adding a feature to an existing project, do not introduce a new architectural style merely because it is preferred for greenfield work. Follow the existing feature structure unless the task explicitly includes an architectural change or the existing structure cannot satisfy the requirement safely.

### 4. Decide dependencies

Follow the dependency decision rules in the `dotnet` skill. In order:

```text
Framework/platform capability
        ↓
Existing project dependency
        ↓
Small explicit implementation for simple, non-sensitive functionality
        ↓
New third-party dependency when justified
```

Do not ask for approval before using an existing dependency. For a new dependency, explain the trade-off and obtain approval when an interactive channel exists. In explicitly autonomous/headless execution, do not silently introduce a new third-party dependency; report the decision and alternatives if approval is unavailable.

### 5. Scaffold

Generate only what the requirements call for, matching established conventions.

For a new solution/project, create the required solution/project files and only the infrastructure needed to make the intended application shape coherent.

#### Greenfield Solution Archetype
When creating a new repository or multi-project solution from scratch:
- **Target Framework**: Use the target framework of the existing repository or `global.json`. For greenfield projects, default to the latest LTS supported by the installed SDK (`net10.0`), as .NET 9 reaches End of Support on 2026-11-10 while .NET 10 is the active LTS.
- **Solution Format**: Prefer modern XML-based `.slnx` (`dotnet new sln --format slnx`, supported in SDK 9.0.200+ and default in .NET 10 SDK) when supported by tooling and IDEs, or standard `.sln` when backward compatibility is required.
- **Standard Directory Layout**:
  ```text
  <repo-root>/
  ├── .editorconfig
  ├── .gitignore
  ├── global.json
  ├── Directory.Build.props
  ├── Directory.Packages.props
  ├── <SolutionName>.slnx
  ├── src/
  │   └── <ProjectName>/
  └── tests/
      └── <ProjectName>.Tests/
  ```
- **SDK & Runner Pinning (`global.json`)**:
  Pin the SDK feature band, MSTest.Sdk version, and opt into Microsoft.Testing.Platform runner mode for `dotnet test`:
  ```json
  {
    "sdk": {
      "version": "10.0.402",
      "rollForward": "latestFeature"
    },
    "msbuild-sdks": {
      "MSTest.Sdk": "3.8.2"
    },
    "test": {
      "runner": "Microsoft.Testing.Platform"
    }
  }
  ```
- **Central Package Management (`Directory.Packages.props`)**:
  Enable CPM by default across multi-project solutions to eliminate package version drift. Resolve current compatible package versions dynamically at scaffold time and record them centrally; projects declare `PackageReference` without `Version`:
  ```xml
  <Project>
    <PropertyGroup>
      <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
      <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
    </PropertyGroup>
    <ItemGroup>
      <!-- Centralized package versions resolved at scaffold time -->
      <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
    </ItemGroup>
  </Project>
  ```
- **Global Build Policy (`Directory.Build.props`)**:
  Enforce baseline quality and compiler options centrally (defaulting to the active LTS `net10.0` for greenfield):
  ```xml
  <Project>
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <ImplicitUsings>enable</ImplicitUsings>
      <Nullable>enable</Nullable>
      <TreatWarningsAsErrors Condition="'$(Configuration)' == 'Release'">true</TreatWarningsAsErrors>
      <AnalysisLevel>latest-recommended</AnalysisLevel>
    </PropertyGroup>
  </Project>
  ```

#### Test Project Scaffolding Archetype
When scaffolding a new test project (e.g. `tests/<Project>.IntegrationTests`), follow these defaults:
- **Primary Framework Default:** Current `MSTest.Sdk` (resolve version via `global.json` or project Sdk attribute; MTP-native).
- **Alternative Framework:** `xUnit v3` (for existing xUnit repositories or constructor fixture preferences).
- **Persistence Testing Standard:** Include `Testcontainers` (e.g. `Testcontainers.PostgreSql`, `Testcontainers.MsSql`) and `Respawn` when the project uses relational persistence. Never scaffold tests using in-memory EF Core providers.

```xml
<!-- Default: MSTest.Sdk Integration Test Project -->
<!-- SDK version resolved via global.json msbuild-sdks or Sdk="MSTest.Sdk/x.y.z" -->
<Project Sdk="MSTest.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Package versions resolved dynamically into Directory.Packages.props under CPM -->
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Respawn" />
  </ItemGroup>
</Project>
```

For detailed fixture and lifecycle patterns, see `skills/dotnet/references/testing.md`.

For a feature/entity, generate the applicable parts of the feature such as:

- request/command/query types
- handler/use-case logic
- endpoint/API contract
- validation
- DTOs and explicit mapping where applicable
- persistence model/configuration where applicable
- DI or endpoint registration where the existing project requires it
- tests at the appropriate behavioral boundary

#### Minimal API Vertical Slice Archetype
When scaffolding a feature in a Minimal API project, co-locate the endpoint, handler, and contracts in a feature slice:

```text
Features/<Area>/<Action>/
├── <Action>Endpoint.cs    // Route registration + endpoint mapping implementing IEndpoint
├── <Action>Request.cs     // Request DTO / parameters
├── <Action>Handler.cs     // Use-case logic & domain execution
└── <Action>Response.cs    // Response DTO with static From factory & Project expression
```

**Example (`Features/Orders/CreateOrder/CreateOrderEndpoint.cs`):**
```csharp
using Microsoft.AspNetCore.Http.HttpResults;

public sealed class CreateOrderEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/orders", async Task<Results<Created<OrderResponse>, ProblemHttpResult>> (
            CreateOrderRequest request,
            CreateOrderHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);
            return result.Match<Results<Created<OrderResponse>, ProblemHttpResult>>(
                order => TypedResults.Created($"/orders/{order.Id}", OrderResponse.From(order)),
                error => TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: error.Description, title: error.Code));
        })
        .WithName("CreateOrder");
    }
}
```
Register the handler and endpoint in dependency injection and startup:
```csharp
builder.Services.AddScoped<CreateOrderHandler>();
// Map endpoint instance (or scan IEndpoint implementations):
new CreateOrderEndpoint().Map(app);
```
Direct handler injection keeps the endpoint thin and eliminates in-process mediator overhead. Note: `.WithOpenApi()` is deprecated in .NET 10 (`ASPDEPR002`); ASP.NET Core built-in OpenAPI automatically tracks Minimal API endpoints, while `AddOpenApiOperationTransformer` handles custom metadata.

Do not generate ceremony merely because a reference template contains it. Do not overwrite existing files or conventions without a concrete reason.

### 6. Configure

Wire the generated pieces into the application using its established conventions. Account for:

- dependency injection
- endpoint/route registration
- configuration and Options patterns
- persistence and migrations when applicable
- serialization/OpenAPI behavior when applicable
- local development/orchestration when the repository uses Aspire or similar tooling

For Aspire-specific workflows, use the official Aspire Agent Skill when it is installed and available. Do not duplicate Aspire-specific instructions here.

Keep infrastructure concerns in their appropriate project/boundary.

### 7. Verify

Choose verification proportional to the change. At minimum, prove that generated code is structurally valid and builds when the environment permits it.

Typical verification:

- restore
- build affected project, then broader solution when warranted
- focused tests, then broader tests when relevant
- formatter/analyzers when appropriate
- application startup or API smoke test for runnable applications
- database/migration checks when persistence changed
- inspect generated dependencies and project references

Do not claim scaffolding is complete merely because files were generated. Report checks that could not be run and why.

## Completeness checklist

Before reporting completion, verify the applicable items:

- [ ] Correct solution/project structure
- [ ] Required application/domain components
- [ ] Required endpoint/API surface
- [ ] Handler/use-case implementation where applicable
- [ ] Validation where required
- [ ] DTO/request/response contracts where appropriate
- [ ] Explicit mapping where the project uses it
- [ ] Persistence configuration where applicable
- [ ] DI/registration where required
- [ ] `CancellationToken` propagation across meaningful async boundaries
- [ ] Result/error to transport mapping where applicable
- [ ] Tests for important behavior and boundaries
- [ ] OpenAPI metadata where applicable
- [ ] Configuration/Options wiring where applicable
- [ ] Restore/build/tests or the appropriate verification subset passed
- [ ] No unintended changes or unnecessary dependencies

Do not require every item for every project. "Applicable" is determined by the requirements and existing architecture.

## Boundaries

This skill does not:

- replace the `dotnet` skill's engineering guidance
- automatically migrate an existing architecture to personal preferences
- require a specific mediator, mapper, Result library, validation library, or architecture
- introduce third-party dependencies silently
- duplicate official specialized skills such as the Aspire Agent Skill
- perform unrelated refactoring
- replace a dedicated code-review workflow
- require MCP servers, plugins, hooks, or other agent-specific tooling

If semantic code-navigation tooling is available, prefer bounded semantic inspection for large or unfamiliar codebases when it materially improves accuracy or reduces unnecessary context. The workflow must remain useful without it.

## Reporting

Report:

1. What was scaffolded.
2. Key architecture/dependency decisions and any deviations from repository conventions.
3. Files/projects added or materially changed.
4. Verification performed and results.
5. Any unresolved decision, failed check, or follow-up required.
