# Zero-Downtime Database Migrations & Schema Evolution

Use this reference when designing, applying, or reviewing database schema changes, migrations, and index operations in production .NET applications.

---

## 1. The Expand / Contract (Parallel Run) Pattern

During a rolling deployment or blue/green release, old (v1) and new (v2) application instances run simultaneously. **Never rename, delete, or alter column types in a single migration.** Apply the 3-phase Expand/Contract pattern:

```text
Release N (Expand)     -> Release N+1 (Dual-Write/Backfill) -> Release N+2 (Contract)
├── Add new column        ├── App writes to Old & New          ├── App reads/writes New only
└── Keep old column       └── Background backfill old data     └── Drop old column & constraints
```

### Example: Renaming `FullName` to `DisplayName`

#### Phase 1: Expand (Release 1)
Add the new column as **nullable** in the database. Do not touch the old column:
```csharp
migrationBuilder.AddColumn<string>(
    name: "DisplayName",
    table: "Users",
    type: "text",
    nullable: true);
```

#### Phase 2: Dual-Write & Backfill (Release 2)
Update the application code to write to both columns and fall back to the old column on read:
```csharp
// Read: Prefer new column, fallback to old
public string DisplayName => NewDisplayName ?? OldFullName;

// Write: Populate both columns on insert/update
user.OldFullName = request.DisplayName;
user.NewDisplayName = request.DisplayName;
```
Run a background batch script to backfill existing historical records (see Section 4).

#### Phase 3: Contract (Release 3)
Switch the application to read and write **only** to the new column. Remove old property from C# entity. Drop the old column in a final migration:
```csharp
migrationBuilder.DropColumn(
    name: "FullName",
    table: "Users");
```

---

## 2. Lock-Safe DDL Recipes

DDL operations that lock entire tables with an `ACCESS EXCLUSIVE` lock cause connection pool exhaustion and downtime.

### PostgreSQL: Concurrent Index Creation
Standard `CREATE INDEX` locks the table against writes. Use `CREATE INDEX CONCURRENTLY` and disable EF Core's enclosing transaction:

```csharp
public partial class AddUserEmailIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Must suppress transaction: CONCURRENTLY cannot run inside a transaction block
        migrationBuilder.Sql(
            "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_users_email ON \"Users\" (\"Email\");",
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP INDEX CONCURRENTLY IF EXISTS ix_users_email;",
            suppressTransaction: true);
    }
}
```

### PostgreSQL: Non-Blocking Foreign Keys
Add the foreign key as `NOT VALID`, commit that migration, then validate in a separate migration invocation. PostgreSQL retains the strongest acquired lock until commit; putting both statements in one transaction blocks writers throughout validation.

```csharp
// Migration A: acquire SHARE ROW EXCLUSIVE without scanning existing rows.
migrationBuilder.Sql(
    "ALTER TABLE \"Orders\" ADD CONSTRAINT fk_orders_customer " +
    "FOREIGN KEY (\"CustomerId\") REFERENCES \"Customers\" (\"Id\") NOT VALID;");
```

Apply migration A to its target and let that invocation commit before applying migration B. Two migration classes submitted together can still share an outer transaction. Configure deployment lock timeouts/retries because adding the constraint can wait behind existing transactions.

```csharp
// Migration B: validate under SHARE UPDATE EXCLUSIVE after A has committed.
// Ordinary reads/writes can proceed; conflicting DDL may still wait.
migrationBuilder.Sql(
    "ALTER TABLE \"Orders\" VALIDATE CONSTRAINT fk_orders_customer;");
```

### SQL Server: Online Index Creation
In SQL Server Enterprise, always specify `ONLINE = ON`:
```csharp
migrationBuilder.Sql(
    "CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders (CustomerId) WITH (ONLINE = ON);");
```

---

## 3. Production Migration Deployment

❌ **Antipattern:** Never call `context.Database.Migrate()` or `Database.EnsureCreated()` in application startup code (`Program.cs`). Multi-pod deployments cause startup races, deadlocks, and require application containers to hold high-privilege DDL credentials.

### Recommended Approaches

#### 1. Idempotent SQL Scripts (CI/CD Pipeline)
Generate idempotent migration scripts during the build stage and apply them via deployment pipeline:
```bash
dotnet ef migrations script --idempotent --output migrate.sql --project src/MyProject
```
The script uses `IF NOT EXISTS` checks against the `__EFMigrationsHistory` table and runs safely on any environment.

#### 2. Migration Bundles (Containers & Kubernetes)
Compile migrations into a standalone, self-executing binary:
```bash
dotnet ef migrations bundle --self-contained -r linux-x64 -o ./efbundle
```
Run `./efbundle` as an independent Kubernetes `Job` or init container prior to rolling out new application pods.

---

## 4. High-Performance Bulk Backfills

❌ **Never use EF Core `SaveChanges()` to backfill large datasets.** Loading 500,000 tracked entities into memory causes out-of-memory crashes and transaction log overflow.

### Batched Updates with Committed Transaction Boundaries
> [!CAUTION]
> In PostgreSQL, a standard `DO $$ ... $$` block runs inside **one single transaction** regardless of loops or sleeps. It holds locks and accumulates undo across all iterations.
> To release locks and allow WAL recycling between batches, use a `PROCEDURE` with an explicit `COMMIT` inside the loop, or batch from application code.

```sql
CREATE OR REPLACE PROCEDURE backfill_user_display_names(batch_size INT DEFAULT 5000)
LANGUAGE plpgsql
AS $$
DECLARE
    rows_updated INT;
BEGIN
    LOOP
        UPDATE "Users"
        SET "DisplayName" = "FullName"
        WHERE "Id" IN (
            SELECT "Id" FROM "Users"
            WHERE "DisplayName" IS NULL AND "FullName" IS NOT NULL
            LIMIT batch_size
        );

        GET DIAGNOSTICS rows_updated = ROW_COUNT;
        EXIT WHEN rows_updated = 0;

        -- Essential: commits progress and releases row/table locks per batch
        COMMIT;
    END LOOP;
END;
$$;

CALL backfill_user_display_names(5000);
DROP PROCEDURE backfill_user_display_names;
```

Run `CALL` at the top level outside an explicit/ambient transaction. An EF migration transaction or a client transaction wrapping the call prevents the procedure's internal commits.

Alternatively, orchestrate chunked updates from a background worker using `ExecuteUpdateAsync` to commit each chunk independently:

```csharp
while (!ct.IsCancellationRequested)
{
    var count = await db.Users
        .Where(u => u.DisplayName == null && u.FullName != null)
        .Take(5000)
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, u => u.FullName), ct);

    if (count == 0) break;
    await Task.Delay(100, ct); // Yield to other database traffic
}
```

---

## 5. Zero-Downtime Pre-Deployment Checklist

- [ ] Does this change rename or drop a column? If yes, split into Expand and Contract releases.
- [ ] Are new non-nullable columns added with default values or introduced as nullable first?
- [ ] Does index creation use `CONCURRENTLY` (Postgres) or `ONLINE = ON` (SQL Server)?
- [ ] Is `suppressTransaction: true` specified on migrations using `CONCURRENTLY`?
- [ ] Is `context.Database.Migrate()` absent from application startup (`Program.cs`)?
- [ ] Are bulk backfills chunked in small batches rather than loading tracked EF entities?
