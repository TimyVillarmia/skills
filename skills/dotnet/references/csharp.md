# Modern C# Engineering

Use this reference when a task depends on C# language features, idioms, language-version decisions, API design at the type level, or modernization of existing C# code.

The goal is not to use every new feature. Choose language features when they make intent clearer, reduce accidental complexity, improve correctness, or provide a meaningful performance or maintainability benefit.

## Official references

Use Microsoft Learn as the authoritative source for C# language behavior and version-specific syntax:

- [C# documentation](https://learn.microsoft.com/dotnet/csharp/) — tutorials, language reference, and feature documentation.
- [What's new in C# 14](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14) — current C# 14 features and examples.
- [C# language versioning](https://learn.microsoft.com/dotnet/csharp/language-reference/language-versioning) — relationship between target frameworks, SDKs, and language versions.
- [Configure C# language version](https://learn.microsoft.com/dotnet/csharp/language-reference/configure-language-version) — project-level language-version configuration.
- [C# compiler language feature options](https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-options/language) — `LangVersion`, nullable context, and related compiler options.
- [.NET 10 what's new](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview) — runtime and SDK features that complement C# 14.

For a specific feature, prefer the linked Microsoft Learn language-reference or feature page from the C# documentation when available. Do not treat this list as exhaustive; verify version-sensitive behavior against current official documentation.

## Version awareness

Before using a language feature:

1. Identify the project's target framework and SDK.
2. Determine the effective C# language version (`LangVersion` and project/SDK defaults).
3. Check nullable and analyzer settings.
4. Confirm the feature is supported by that language version.
5. Follow repository conventions if they intentionally target an older language subset.

Do not introduce syntax from a newer C# version into a project that cannot compile it. Do not confuse a .NET runtime feature with a C# language feature; verify both independently when relevant.

For a project targeting .NET 10, C# 14 is the expected modern language baseline unless the repository explicitly chooses otherwise. Verify the actual project configuration before relying on C# 14-specific behavior.

## Primary constructors

Use primary constructors when constructor parameters naturally describe the type's required dependencies or state and keeping the declaration compact improves readability.

```csharp
public sealed class OrderService(IOrderRepository orders, TimeProvider clock)
{
    public async Task<Order?> GetAsync(Guid id, CancellationToken ct)
        => await orders.GetAsync(id, ct);
}
```

Do not use primary constructors merely to make every class one line. Be deliberate when constructor parameters are captured as state, when initialization is complex, or when the traditional constructor communicates invariants more clearly.

For dependency injection, primary constructors work well when the class has a small, stable dependency set. Avoid hiding important initialization behavior behind overly clever field/property interactions.

## C# 14 features

### `field` keyword

Use `field` in property accessors when the compiler-provided backing field makes the property implementation clearer than introducing an explicit backing field.

```csharp
public string Name
{
    get;
    set => field = value.Trim();
}
```

Use an explicit backing field when the state needs a meaningful name, is shared by multiple members, or the accessor logic becomes easier to understand with explicit state.

See [What's new in C# 14](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14) for the `field` keyword and its interaction with other C# 14 features.

### Extension blocks

Use extension blocks when several related extension members belong to the same conceptual type and grouping improves discoverability.

```csharp
public static class OrderExtensions
{
    extension(Order order)
    {
        public bool IsOpen => order.Status is OrderStatus.Pending or OrderStatus.Processing;
    }
}
```

Do not introduce an extension block solely because it is newer syntax. A normal extension method can remain clearer for a small or isolated operation.

See the [C# 14 extension members documentation](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14#extension-members) when exact syntax or behavior matters.

### Other C# 14 language improvements

Use other C# 14 features when they solve a concrete readability or correctness problem. Verify exact syntax and compiler behavior against the project's SDK/compiler rather than relying on memory for version-sensitive features.

The [C# 14 feature overview](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14) covers implicit span conversions, unbound generic types with `nameof`, lambda parameter modifiers, partial constructors/events, user-defined compound assignment, null-conditional assignment, and other C# 14 changes.

## Records and immutable models

Use `record` or `record struct` when value-oriented equality, concise immutable-style data modeling, or non-destructive mutation is useful.

Use classes when identity, mutable lifecycle, inheritance behavior, or reference semantics are more appropriate.

Use `init` for properties that should be set only during initialization. Use `required` when construction must provide a member and the requirement is best expressed at compile time.

Do not make every DTO, entity, or domain object a record automatically. Choose semantics first.

For language-reference details, use the [C# documentation](https://learn.microsoft.com/dotnet/csharp/).

## Collection expressions

Use collection expressions (`[...]`) when the target type and surrounding code make the intent clearer and the target supports the required semantics.

```csharp
int[] values = [1, 2, 3];
List<string> names = ["Ada", "Grace"];
```

Use spread elements (`..`) when composing collections is clearer than repeated `Add` calls or verbose initialization.

Be aware of target typing, allocation behavior, and the distinction between creating a new collection and using an existing collection instance. Do not claim a collection expression is faster without evidence.

Use the [C# language reference](https://learn.microsoft.com/dotnet/csharp/language-reference/) to verify collection-expression syntax and semantics when needed.

## Pattern matching

Prefer pattern matching when it expresses type, nullability, shape, or value conditions directly.

Useful forms include:

- relational patterns: `age is >= 18`
- logical patterns: `status is not null and not Disabled`
- property patterns: `order is { Status: Pending, Total: > 0 }`
- list patterns: `items is [var first, ..]`
- type patterns: `value is Customer customer`
- switch expressions for exhaustive value-to-result transformations

Prefer patterns that make the business rule readable. Avoid deeply nested patterns that are technically compact but harder to understand than a small named predicate or ordinary control flow.

## Nullability

Treat nullable reference types as part of the type contract, not as compiler noise.

- Enable and respect nullable analysis where the repository supports it.
- Model optional values explicitly with `T?` where appropriate.
- Establish non-null invariants at boundaries rather than scattering null-forgiving operators (`!`).
- Do not suppress warnings without understanding the invariant that makes the suppression safe.
- Use `ArgumentNullException.ThrowIfNull` and related guard APIs when appropriate.

A nullability warning can indicate a real contract defect. Fix the underlying model or control flow when practical rather than silencing the warning.

For compiler configuration, see [C# compiler language feature options](https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-options/language).

## Async and streams

Use `Task` for normal asynchronous operations and `IAsyncEnumerable<T>` when the consumer benefits from streaming results rather than materializing the entire sequence.

Pass `CancellationToken` through meaningful asynchronous boundaries, including async streams where cancellation is supported.

Use `ValueTask` only when profiling or a well-understood API contract shows that avoiding `Task` allocation materially matters. Do not use it as the default async return type.

Avoid:

- `.Result` or `.Wait()` on asynchronous work
- unnecessary `Task.Run` for naturally asynchronous I/O
- fire-and-forget work without an explicit lifetime/error-handling mechanism
- materializing an async stream when streaming is part of the requirement

Use the [C# documentation](https://learn.microsoft.com/dotnet/csharp/) for language-level async and iterator details, and the applicable .NET API documentation for runtime behavior.

## Performance-oriented language features

`Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ArrayPool<T>`, ref structs, `ref` returns, and related low-level features are powerful tools, not default coding styles.

Consider them when profiling identifies allocation, copying, parsing, serialization, or hot-loop costs that these features can address. Prefer ordinary types when they provide equivalent behavior with clearer ownership and lifetime semantics.

Never use `unsafe`, stack allocation, pooling, or complex ref semantics merely to appear performant. Measure first and document non-obvious lifetime or ownership constraints.

For C# 14's new implicit span conversions, see [What's new in C# 14](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14).

## Generic and type-system features

Use generic constraints and static abstract interface members when they express a real reusable type-level contract, especially for algorithms over numeric or domain-specific types.

Prefer ordinary interfaces and generic methods when they are sufficient. Avoid advanced generic machinery that makes a simple domain rule difficult to read.

Use `nameof`, target-typed `new`, `var` where the type is obvious, and target-typed expressions where they improve readability without hiding important type information.

For exact language semantics, use the [C# language reference](https://learn.microsoft.com/dotnet/csharp/language-reference/).

## Strings and formatting

Use raw string literals for multi-line strings or strings containing substantial quoting/escaping when they make the content easier to read.

Use interpolated strings for clear formatting. For performance-sensitive formatting paths, consider interpolated string handlers or other specialized APIs only when measurement or an established framework API justifies them.

Do not micro-optimize ordinary string construction without evidence.

## Exceptions and error handling

Use exceptions for exceptional failures and expected result/error models for expected application outcomes when that matches the application's architecture.

Use exception filters when they make conditional handling clearer without catching exceptions too broadly.

Do not catch `Exception` merely to return a generic error. Preserve cancellation semantics and avoid converting programming failures into misleading domain errors.

For language syntax and semantics, use the [C# language reference](https://learn.microsoft.com/dotnet/csharp/language-reference/).

## API and domain type design

Prefer types that make invalid states difficult to represent when the added complexity is justified.

Useful tools include:

- `readonly struct` / `record struct` for small value semantics
- enums or discriminated-style result models for constrained states
- `required` members for construction requirements
- private setters or init-only properties for invariants
- explicit interfaces for meaningful substitution boundaries
- generic constraints for real compile-time guarantees

Do not create custom wrapper types for every primitive value unless they enforce an important invariant or clarify a meaningful domain concept.

## Modernization guidance

When modernizing existing C#:

1. Confirm the language/runtime version.
2. Identify the concrete problem the modernization solves.
3. Prefer a localized change over broad syntax churn.
4. Preserve public behavior and API compatibility unless a breaking change is intentional.
5. Keep the repository's style consistent.
6. Compile and test after modernization.

Do not rewrite an entire codebase from older syntax to newer syntax merely to demonstrate language knowledge. Modernize where the new form materially improves the code.

For version compatibility, consult [C# language versioning](https://learn.microsoft.com/dotnet/csharp/language-reference/language-versioning) and [Configure C# language version](https://learn.microsoft.com/dotnet/csharp/language-reference/configure-language-version).

## Decision checklist

Before choosing a modern C# feature, ask:

- Is it supported by this project's effective language version?
- Does it make the intent clearer?
- Does it improve correctness or enforce a useful invariant?
- Does it reduce meaningful complexity?
- Does it have performance implications that matter here?
- Does the repository already use the feature consistently?
- Would a less advanced construct be easier for maintainers to understand?

Prefer the feature when the answers justify it; otherwise use the simpler construct.
