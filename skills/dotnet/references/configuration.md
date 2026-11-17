# Configuration, Options & Feature Flags

Use this reference when configuring application settings, binding strongly typed options, managing secrets, or evaluating feature flags.

## Contents

- [Configuration source precedence](#configuration-source-precedence)
- [Environment variable binding](#environment-variable-binding)
- [User Secrets (local development)](#user-secrets-local-development)
- [Options pattern and startup validation](#options-pattern-and-startup-validation)
- [Options lifetimes and captive dependencies](#options-lifetimes-and-captive-dependencies)
- [Feature flags](#feature-flags)
- [Configuration anti-patterns](#configuration-anti-patterns)

---

## Configuration source precedence

`WebApplication.CreateBuilder(args)` and `Host.CreateApplicationBuilder(args)` load configuration sources in increasing order of precedence (later sources override earlier ones):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User Secrets (`Development` environment only)
4. Environment variables
5. Command-line arguments

Do not inject `IConfiguration` into application services, handlers, or domain classes. String-key lookups (`configuration["Smtp:Host"]`) scatter configuration paths, bypass startup validation, and hide missing-config errors until runtime. Bind configuration to strongly typed options classes at startup instead.

---

## Environment variable binding

On Linux and in containers, colon (`:`) separators in hierarchical keys are not valid in many shells. Use double underscores (`__`), which the .NET environment configuration provider translates to `:`:

```bash
# Maps to Database:ConnectionString and Smtp:Port
Database__ConnectionString="Host=db;Database=app;Username=app;Password=..."
Smtp__Port=587
```

---

## User Secrets (local development)

Never commit connection strings, API keys, signing secrets, or passwords to `appsettings.json`, `appsettings.Development.json`, or source control. Store local development secrets outside the repository using `dotnet user-secrets`:

```bash
dotnet user-secrets init --project src/MyApi
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;..." --project src/MyApi
```

In `Production` and `Staging`, provide secrets via environment variables, managed secret stores (Azure Key Vault, AWS Secrets Manager), or mounted orchestrator secrets.

---

## Options pattern and startup validation

Treat configuration for external infrastructure and security (database connection strings, identity/token secrets, third-party endpoints/keys, bounded channel capacities) as a startup invariant. Validate and fail fast at startup with `ValidateOnStart()`.

Properties bound via `BindConfiguration(...)` require `public set` or `public init` accessors.

### 1. Compile-time `[OptionsValidator]` (Default for .NET 8+ / Native AOT)

Prefer compile-time source-generated validation (`[OptionsValidator]`) over reflection-based `ValidateDataAnnotations()`, which emits `IL2026` trimming warnings under Native AOT:

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
public partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
}

// Registration in Program.cs:
builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.SectionName)
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
```

### 2. Cross-field or custom validation

When validation depends on multiple properties or custom invariants, implement `IValidateOptions<T>` directly or chain `.Validate(...)`:

```csharp
builder.Services.AddOptions<StorageOptions>()
    .BindConfiguration(StorageOptions.SectionName)
    .Validate(
        opts => !opts.UseS3 || !string.IsNullOrWhiteSpace(opts.BucketName),
        "BucketName is required when UseS3 is true.")
    .ValidateOnStart();
```

### 3. Legacy reflection validation (`ValidateDataAnnotations`)

Existing non-AOT projects may use `.ValidateDataAnnotations().ValidateOnStart()`. Do not replace working registration without a reason, but prefer `[OptionsValidator]` for new options types.

Optional feature toggles and non-critical defaults with safe fallbacks may omit validation boilerplate.

---

## Options lifetimes and captive dependencies

Choose the options interface based on service lifetime and reload needs:

| Interface | DI Lifetime | Behavior | Use When |
|---|---|---|---|
| `IOptions<T>` | Singleton | Read once at startup | Static configuration and stateless singletons |
| `IOptionsSnapshot<T>` | Scoped | Re-computed per request/scope | Scoped handlers needing per-request config reload |
| `IOptionsMonitor<T>` | Singleton | CurrentValue + `OnChange` callbacks | Singletons / `BackgroundService` workers needing live reload |

> [!CAUTION]
> **Captive dependency trap:** Never inject `IOptionsSnapshot<T>` (Scoped) into a Singleton service or `BackgroundService`. When `ValidateScopes = true` (default in Development), the DI container throws `InvalidOperationException` at startup; otherwise it silently captures a stale scope. Inject `IOptionsMonitor<T>` or `IOptions<T>` into Singletons.

---

## Feature flags

Follow the dependency decision order:

- **Simple static toggles:** Use a strongly typed `bool` property on an `IOptions<T>` class (e.g., `FeaturesOptions.EnableNewCheckout`).
- **Dynamic rollouts & targeting:** Add `Microsoft.FeatureManagement` (`IFeatureManager`) only when you need percentage rollouts, user/tenant targeting filters, or time-window activation.

Every feature flag must have an explicit owner and removal date. Delete flags and dead branches promptly once a rollout reaches 100%.

---

## Configuration anti-patterns

❌ Injecting `IConfiguration` into services, handlers, or token validators.

❌ Omitting `.ValidateOnStart()` on required infrastructure, connection string, or security options.

❌ Injecting scoped `IOptionsSnapshot<T>` into a Singleton service.

❌ Committing secrets to `appsettings.*.json` instead of using `dotnet user-secrets` or runtime secret stores.

❌ Leaving permanent, unowned feature flags in the codebase after full rollout.
