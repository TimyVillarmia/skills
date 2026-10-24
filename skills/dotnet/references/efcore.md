# EF Core

Use this reference when working with Entity Framework Core, relational persistence, queries, transactions, migrations, or database-related performance.

**Official references:**

- [EF Core documentation](https://learn.microsoft.com/en-us/ef/core/)
- [EF Core what's new](https://learn.microsoft.com/en-us/ef/core/what-is-new/)

## DbContext

Treat `DbContext` as a short-lived unit of work and use dependency injection with the lifetime appropriate to the application model. Never share a context concurrently across unrelated operations.

For background services or parallel work, create an explicit scope/context per unit of work rather than reusing a request-scoped context.

## Modeling

Model actual domain and database constraints explicitly:

- required vs optional properties
- keys and alternate keys
- indexes
- foreign keys and relationships
- delete behavior
- unique constraints
- value conversions
- owned/complex types where appropriate

Prefer database-enforced constraints for invariants that must hold regardless of application path.

## Querying

Design queries around the data actually needed.

- Prefer projection over loading full entities when only a subset is required.
- Use `AsNoTracking()` for read-only entity queries when tracking provides no value.
- Tracking is appropriate when the unit of work will modify loaded entities.
- Consider identity resolution when a no-tracking query materializes repeated references that need identity consistency.
- Avoid unnecessary `Include`; projection is often a better read model.
- Bound result sets.
- Filter and order before materialization.
- Avoid loading large collections into memory when the database can perform the operation.
- Inspect generated SQL when behavior or performance is uncertain.

Good read model:

```csharp
var users = await db.Users
    .AsNoTracking()
    .Where(x => x.IsActive)
    .OrderBy(x => x.Email)
    .Select(x => new UserDto(x.Id, x.Email))
    .ToListAsync(cancellationToken);
```

Anti-pattern:

```csharp
var users = await db.Users
    .Include(x => x.Profile)
    .ToListAsync(cancellationToken);

return users.Select(x => new UserDto(x.Id, x.Email));
```

The second form can load columns/rows that the endpoint never needs.

## Related data and query shape

Be deliberate about eager, explicit, and lazy loading. Lazy loading can hide database round trips and create N+1 behavior; do not enable it without understanding the trade-off.

When multiple collection relationships are included, evaluate whether a single query creates cartesian explosion. Split queries can help but introduce additional round trips and can have consistency implications.

Do not choose `AsSplitQuery()` or `AsSingleQuery()` as a universal rule; choose based on query shape, provider behavior, consistency needs, and measurement.

## Pagination

Bound collection queries and use deterministic ordering before pagination.

Offset pagination is simple and appropriate for many cases. For large datasets or frequently changing ordered data, consider keyset/cursor pagination.

```csharp
var page = await db.Assets
    .AsNoTracking()
    .Where(x => x.OwnerId == ownerId)
    .OrderBy(x => x.Id)
    .Skip(offset)
    .Take(Math.Min(limit, 100))
    .Select(x => new AssetDto(x.Id, x.Name))
    .ToListAsync(cancellationToken);
```

Never construct SQL fragments from untrusted column names, filter expressions, or sort input.

## Efficient writes

For set-based changes where loading entities is unnecessary, consider `ExecuteUpdateAsync` or `ExecuteDeleteAsync`.

```csharp
await db.Assets
    .Where(x => x.IsArchived)
    .ExecuteDeleteAsync(cancellationToken);
```

These operations bypass normal entity tracking and `SaveChanges` behavior. Consider concurrency, interceptors, domain events, audit behavior, and other invariants before using them.

Do not use bulk/set-based operations merely because they are newer or shorter.

## Writes and transactions

EF Core's `SaveChanges` transaction behavior is often sufficient for a single save operation. Use an explicit transaction when multiple database operations must commit atomically or a workflow spans multiple saves with shared transactional invariants.

Do not wrap every operation in an explicit transaction by habit.

If the configured provider/reliability strategy uses execution strategies or transient-failure retries, understand how explicit transactions interact with that strategy and use the provider/framework-recommended pattern.

## Concurrency

Prefer optimistic concurrency when appropriate. Configure a concurrency token/version and handle `DbUpdateConcurrencyException` intentionally.

```csharp
try
{
    await db.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateConcurrencyException)
{
    // Translate according to application semantics; do not silently overwrite newer state.
}
```

Do not silently retry or overwrite conflicting state without understanding the business semantics.

## Migrations

Migrations are part of schema evolution.

- Review generated migrations instead of blindly applying them.
- Check destructive operations carefully.
- Consider deployment ordering for schema changes that must support multiple application versions.
- Keep data migrations explicit when transformations cannot safely be represented as simple schema operations.
- Do not treat production schema changes as an incidental side effect of application startup unless that is an intentional deployment strategy.

### Migration execution

Treat migration execution as a deployment/runtime concern rather than an automatic consequence of application startup.

For production systems, prefer an explicit migration mechanism when the application is deployed with multiple instances, independently scaled services, restricted database permissions, or a deployment pipeline that can execute database changes separately.

A dedicated migration project, worker/service, deployment job, CI/CD step, or equivalent mechanism can provide a useful operational boundary. Choose the mechanism that fits the deployment environment rather than requiring a specific project shape.

For small applications or simple deployments, an explicit deployment step or another established project mechanism may be simpler than introducing a dedicated migration service. Startup migration can also be reasonable when it is an intentional strategy and its operational and permission implications are understood.

When choosing a migration strategy, consider:

- deployment topology and number of application instances
- database permissions available to the application
- migration ordering and transactional requirements
- rollback and failure behavior
- startup availability requirements
- CI/CD and infrastructure capabilities
- observability and operational ownership
- whether the additional project/service materially improves the boundary

Avoid relying on multiple application instances racing to apply migrations unless the chosen mechanism and database provider make that behavior safe and intentional.

If a dedicated migration service or Aspire orchestration is already used by the repository, follow that established deployment pattern rather than introducing a second migration mechanism.

## Raw SQL

LINQ should be the default when it expresses the query clearly and efficiently. Raw SQL is appropriate for database-specific functionality, query shapes EF cannot express well, or measured performance requirements.

Always parameterize values:

```csharp
var rows = await db.Assets
    .FromSql($"SELECT * FROM Assets WHERE OwnerId = {ownerId}")
    .ToListAsync(cancellationToken);
```

Do not concatenate untrusted input into SQL or identifiers. If dynamic identifiers are required, use a strict allow-list rather than treating them as ordinary parameter values.

## Interceptors and conversions

Interceptors, value converters, conventions, and save/query hooks are powerful. Use them for cross-cutting persistence behavior that genuinely belongs at the persistence boundary.

Avoid hiding significant business behavior inside persistence hooks where control flow becomes difficult to reason about.

## Testing

Test persistence behavior that depends on relational semantics against a relational provider when practical. In-memory substitutes are not equivalent to a real relational database.

Prefer focused integration tests for mappings/constraints, important queries, transactions, concurrency, and migration/schema compatibility.

## EF Core anti-patterns

❌ Reusing one `DbContext` concurrently across tasks.

❌ Loading full entities and large navigation graphs when a projection would suffice.

❌ Lazy loading in code where query count is not observable or controlled.

❌ Unbounded `ToListAsync()` on collection endpoints.

❌ Generic repository wrappers that merely rename EF Core APIs.

❌ Raw SQL built by string concatenation.

❌ Explicit transactions around every operation without a transactional requirement.

❌ `ExecuteUpdate`/`ExecuteDelete` without considering tracking, auditing, domain events, or concurrency implications.

## Performance checklist

1. Measure the actual operation.
2. Inspect query count and generated SQL.
3. Check indexes and execution plans when appropriate.
4. Reduce unnecessary columns and rows.
5. Check tracking/materialization costs.
6. Look for N+1 queries and cartesian explosion.
7. Check pagination and result bounds.
8. Consider database-side computation before materialization.
9. Re-measure after the smallest useful change.

Do not add compiled queries, caching, raw SQL, or other optimizations without evidence that they address a real bottleneck.
