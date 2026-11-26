# Modern C# Engineering

Use this reference for C# 14 specifics, nullability invariants, async/stream rules, formatting/IDs, and Native AOT `System.Text.Json` source generation. For general syntax or version-specific details, query `microsoft_docs_search(query="what's new in C# 14")`.

## Version awareness

Verify `<TargetFramework>` and `<LangVersion>` first, and match syntax to the project's effective language version while keeping runtime APIs distinct from C# language features.

## C# 14 specifics

- **`field` keyword:** Use `field` in property accessors to avoid boilerplate backing fields (`set => field = value.Trim();`). If an existing class already has a member named `field`, disambiguate inside accessors with `@field` or `this.field`.
- **Extension blocks:** Use `extension(Order order) { ... }` when grouping several related extension members for one type improves discoverability (`extension` blocks cannot declare instance fields or static state). Keep isolated extensions as ordinary static methods.
- **Null-conditional assignment & spans:** Use `customer?.LastOrder = order;` for conditional assignment and implicit collection/array-to-span conversions (`ReadOnlySpan<int> span = [1, 2, 3];`) when they clarify intent.

## Collections & hot-path lookups

- Use `ToFrozenDictionary()` and `ToFrozenSet()` (`System.Collections.Frozen`, .NET 8+) for immutable lookup tables built once at startup.
- Use `SearchValues<T>` (.NET 8/9+) for vectorized character, byte, or substring matching.
- Use `[LoggerMessage]` source generation rather than string interpolation or un-templated `ILogger` calls on hot paths.

## Nullability invariants

- Treat nullable reference types as part of the type contract: model optional values with `T?` and enforce non-null invariants at boundaries instead of scattering `!`.
- Use nullability attributes (`[NotNullWhen(true)]`, `[MemberNotNull(nameof(_field))]`) on helper/guard methods and `ArgumentNullException.ThrowIfNull(...)` rather than silencing warnings with `!`.

## Async & streams

- Return `Task` by default; use `ValueTask` only when profiling or an API contract proves `Task` allocation is a bottleneck.
- Return `IAsyncEnumerable<T>` when consumers benefit from streaming rather than materializing the full sequence, and propagate `CancellationToken` (`[EnumeratorCancellation]`) through async boundaries.
- Await async I/O end-to-end and attach explicit lifetime/error boundaries to background work (avoid `.Result`, `.Wait()`, or `Task.Run` over async I/O).

## Formatting, time, and IDs

- **Culture-invariant machine data:** Always pass `CultureInfo.InvariantCulture` (or explicit `IFormatProvider`) when parsing or formatting machine-readable numbers, dates, logs, headers, CSV, or JSON.
- **Time:** Prefer `DateTimeOffset` (stored in UTC) over `DateTime` for instants/timestamps; use `DateOnly` and `TimeOnly` for calendar dates and wall-clock times; obtain current time via `TimeProvider`.
- **Identifiers:** Prefer `Guid.CreateVersion7()` (.NET 9+) over `Guid.NewGuid()` for time-ordered entity or correlation identifiers.

## `System.Text.Json` Source Generation & Native AOT

When targeting Native AOT (`<PublishAot>true</PublishAot>`) or eliminating reflection overhead:

```csharp
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(OrderResponse))]
public sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}

// Register in Program.cs:
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
```

- **Use compile-time static types and source generators:** Prefer static generic constraints and source generators (`[GeneratedRegex]`, `[LoggerMessage]`, `[OptionsValidator]`, `Riok.Mapperly`) over `Assembly.Load`, `Activator.CreateInstance`, or unconstrained `MakeGenericType`.
- **Resolve trim warnings at compile time:** Fix `IL2026` (`RequiresUnreferencedCode`) and `IL3050` (`RequiresDynamicCode`) at the source rather than suppressing them.
