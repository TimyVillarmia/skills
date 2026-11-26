# Configuration, Options & Feature Flags

Use this reference when binding strongly typed options, validating startup configuration, managing secrets, or evaluating feature flags.

## Guardrails

- **Strongly typed options:** Bind strongly typed `IOptions<T>` at startup rather than injecting `IConfiguration` into application services, handlers, or domain classes (string-key lookups scatter paths and defer missing-config failures until runtime).
- **Secret hygiene:** Store local secrets with `dotnet user-secrets` and deployed secrets in environment variables or managed secret stores; keep `appsettings.json`, `appsettings.Development.json`, and source control secret-free.

## Options pattern and startup validation

Treat external infrastructure and security settings (connection strings, token secrets, third-party endpoints/keys, bounded channel capacities) as startup invariants validated with `ValidateOnStart()`. Properties bound via `BindConfiguration(...)` require `public set` or `public init` accessors.

### 1. Compile-time `[OptionsValidator]` (Default for .NET 8+ / Native AOT)

Prefer source-generated `[OptionsValidator]` over reflection-based `ValidateDataAnnotations()` (which emits `IL2026` under Native AOT). In Native AOT projects, also set `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` in `.csproj`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required, MinLength(1)]
    public string ConnectionString { get; init; } = string.Empty;

    [Range(1, 100)]
    public int MaxRetryCount { get; init; } = 3;
}

[OptionsValidator]
public partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions> { }

// Registration in Program.cs:
builder.Services
    .AddOptionsWithValidateOnStart<DatabaseOptions, DatabaseOptionsValidator>()
    .BindConfiguration(DatabaseOptions.SectionName);
```

### 2. Cross-field and legacy validation

- **Cross-field / custom invariants:** Implement `IValidateOptions<T>` directly or chain `.Validate(opts => ..., "message").ValidateOnStart()`.
- **Brownfield non-AOT:** Existing projects using `.ValidateDataAnnotations().ValidateOnStart()` can keep working registrations; use `[OptionsValidator]` for new options types.

## Options lifetimes and captive dependencies

| Interface | DI Lifetime | Behavior | Use When |
|---|---|---|---|
| `IOptions<T>` | Singleton | Read once at startup | Static configuration and stateless singletons |
| `IOptionsSnapshot<T>` | Scoped | Re-computed per request/scope | Scoped handlers needing per-request config reload |
| `IOptionsMonitor<T>` | Singleton | `CurrentValue` + `OnChange` callbacks | Singletons / `BackgroundService` workers needing live reload |

> [!CAUTION]
> **Captive dependency trap:** Inject `IOptionsMonitor<T>` or `IOptions<T>` into Singletons and `BackgroundService` (never scoped `IOptionsSnapshot<T>`). When `ValidateScopes = true` (default in Development), injecting `IOptionsSnapshot<T>` throws `InvalidOperationException` at startup; otherwise it silently captures a stale scope.

## Feature flags

- **Static toggles:** Use a strongly typed `bool` property on an `IOptions<T>` class.
- **Dynamic rollouts & targeting:** Add `Microsoft.FeatureManagement` (`IFeatureManager`) only when percentage rollouts, user/tenant targeting filters, or time-window activation are required.
- Every feature flag must have an explicit owner and removal date; delete flags and dead branches promptly once a rollout reaches 100%.
