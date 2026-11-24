# Caching

Use this reference when caching application data or HTTP responses, designing L1/L2 invalidation, preventing cache stampedes, or choosing between `HybridCache` and Output Caching.

## Contents

- [Data caching vs HTTP output caching](#data-caching-vs-http-output-caching)
- [Application data caching with HybridCache](#application-data-caching-with-hybridcache)
- [L1/L2 coherence, staleness, and immutable entries](#l1l2-coherence-staleness-and-immutable-entries)
- [Tag-based invalidation and tenant isolation](#tag-based-invalidation-and-tenant-isolation)
- [HTTP Output Caching](#http-output-caching)
- [Caching anti-patterns](#caching-anti-patterns)

---

## Data caching vs HTTP output caching

| Concern | `HybridCache` (.NET 9+) | HTTP Output Caching (`UseOutputCache`) |
|---|---|---|
| **Layer** | Application / domain DTOs | Rendered HTTP responses (status, headers, body) |
| **Granularity** | Arbitrary typed objects by key | Route/query/header-varying HTTP responses |
| **Stampede protection** | Built-in per-key factory coalescing | Built-in request coalescing |
| **When to choose** | Shared domain/query data reused across endpoints or workers | Public or cacheable `GET`/`HEAD` endpoints returning full HTTP payloads |

Never cache cheap queries merely because caching looks faster in isolation; caching introduces staleness and invalidation complexity.

---

## Application data caching with `HybridCache`

For application-level data caching in .NET 9+, prefer `Microsoft.Extensions.Caching.Hybrid` (`HybridCache`) over raw `IMemoryCache` or `IDistributedCache`. It unifies in-process L1 and distributed L2 (e.g., Redis via `IDistributedCache`) behind one API with automatic stampede protection:

```csharp
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),          // L2 (distributed) TTL
        LocalCacheExpiration = TimeSpan.FromMinutes(1) // L1 (in-process) TTL
    };
});
```

### Stampede protection semantics

When multiple concurrent callers request the same missing key, `HybridCache` executes the factory delegate once and coalesces the remaining callers onto that single in-flight result. Keep factory delegates side-effect-free and pass the `CancellationToken` provided to the factory lambda (`token`), not the outer caller's token, so one cancelled request does not abort a shared factory execution for other waiting callers:

```csharp
public sealed class GetUserHandler(HybridCache cache, IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<UserResponse?> HandleAsync(Guid id, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            $"user:{id}",
            async token =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(token);
                return await db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == id)
                    .Select(UserResponse.Project)
                    .FirstOrDefaultAsync(token);
            },
            tags: ["users"],
            cancellationToken: ct);
    }
}
```

Use `IDbContextFactory<AppDbContext>` or `IServiceScopeFactory` inside the factory so coalesced database queries survive individual caller cancellation when the initiating HTTP request scope ends early.

Use `HybridCacheEntryFlags` on `HybridCacheEntryOptions.Flags` when a call needs to bypass or restrict a tier (for example, `HybridCacheEntryFlags.DisableLocalCacheRead` for strict L2 reads, or `DisableDistributedCache` for L1-only entries).

---

## L1/L2 coherence, staleness, and immutable entries

1. **Keep `LocalCacheExpiration < Expiration`:** Because L1 lives in each instance's memory, keep L1 TTL shorter than L2 TTL (e.g., 1 min vs 5 min) to bound stale-read windows across horizontally scaled instances.
2. **Avoid L1 deserialization overhead:** Decorate immutable cached DTOs with `[ImmutableObject(true)]` on `sealed record` types (`System.ComponentModel`). Without it, `HybridCache` defensively re-serializes/copies L1 entries on reads to prevent callers from mutating shared in-memory state:
   ```csharp
   [ImmutableObject(true)]
   public sealed record UserResponse(Guid Id, string Email, string FullName);
   ```
3. **Cross-node invalidation caveat:** `RemoveAsync(key)` and `RemoveByTagAsync(tag)` evict L2 and the **local** instance's L1 immediately, but peer instances continue serving their L1 cache until `LocalCacheExpiration` elapses unless a distributed invalidation backplane is wired.

---

## Tag-based invalidation and tenant isolation

Evict a single entry by key via `RemoveAsync`, or associate related entries with group tags (`tags: ["users"]`) and evict logically on write via `RemoveByTagAsync`:

```csharp
await cache.RemoveAsync($"user:{id}", ct);
// Or evict a group tag:
await cache.RemoveByTagAsync("users", ct);
```

- **Centralized keys & write-side invalidation:** Define keys and tags in a per-feature static helper (e.g., `OrderCacheKeys`) shared by readers and writers rather than duplicating interpolated strings. Every command that mutates cached state must invalidate the affected key or tag (`RemoveAsync` / `RemoveByTagAsync`); if you cannot enumerate the affected keys or tags, do not cache the read.
- **Tenant-scoped keys:** In multi-tenant applications, always namespace cache keys and tags with `tenant:{tenantId}:` so one tenant never reads or evicts another tenant's cached data. See [Multi-Tenancy & Tenant Isolation](multi-tenancy.md#4-multi-tenant-cache-partitioning) for the full key pattern.

---

## HTTP Output Caching

For caching full HTTP responses on the server side, prefer `app.UseOutputCache()` over legacy `ResponseCaching` (which trusts client `Cache-Control` headers). Output Caching supports distributed stores, tag eviction (`OutputCachePolicyBuilder.Tag`), and request coalescing:

```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(policy => policy.Expire(TimeSpan.FromSeconds(30)));
    options.AddPolicy("Products", policy => policy
        .Expire(TimeSpan.FromMinutes(5))
        .SetVaryByQuery("page", "pageSize")
        .Tag("products"));
});

// Middleware order: UseOutputCache must run after UseCors and UseAuthentication/UseAuthorization
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapGet("/api/products", GetProducts)
    .CacheOutput("Products");
```

> [!CAUTION]
> **Shared output cache poisoning:** Never cache authenticated, per-user, or tenant-specific endpoints in a shared output cache without explicitly varying the cache key (`SetVaryByHeader` / custom `VaryByValue` on user/tenant claims) or opting out. By default, ASP.NET Core Output Caching skips responses when `Authorization` headers or `Set-Cookie` are present—do not override that safeguard blindly.

---

## Caching anti-patterns

❌ Cancelling a shared `HybridCache` factory with an individual request's `CancellationToken` instead of the factory's `token` parameter.

❌ Using global, un-namespaced cache keys in a multi-tenant system.

❌ Caching mutable entities or forgetting `[ImmutableObject(true)]` on hot L1 DTOs.

❌ Calling `UseOutputCache()` before `UseCors()` or `UseAuthentication()`/`UseAuthorization()`.

❌ Caching per-user or authorized HTTP responses without varying the key by user/tenant identity.

❌ Caching a read with `HybridCache` without matching key/tag invalidation in commands that mutate the underlying data.
