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
Adding a foreign key directly scans the entire table and blocks writes. Add it as `NOT VALID`, then validate asynchronously:

```csharp
// Step 1: Adds constraint without validating existing rows (instant, no table lock)
migrationBuilder.Sql(
    "ALTER TABLE \"Orders\" ADD CONSTRAINT fk_orders_customer " +
    "FOREIGN KEY (\"CustomerId\") REFERENCES \"Customers\" (\"Id\") NOT VALID;");

// Step 2: Validates existing rows without blocking concurrent writes
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

### Batched Raw SQL Updates
Run backfills in bounded key chunks with sleep intervals to allow database replication and prevent lock contention:

```sql
DO $$
DECLARE
    batch_size INT := 5000;
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

        -- Prevent lock escalation and replication lag
        PERFORM pg_sleep(0.1);
    END LOOP;
END $$;
```

---

## 5. Zero-Downtime Pre-Deployment Checklist

- [ ] Does this change rename or drop a column? If yes, split into Expand and Contract releases.
- [ ] Are new non-nullable columns added with default values or introduced as nullable first?
- [ ] Does index creation use `CONCURRENTLY` (Postgres) or `ONLINE = ON` (SQL Server)?
- [ ] Is `suppressTransaction: true` specified on migrations using `CONCURRENTLY`?
- [ ] Is `context.Database.Migrate()` absent from application startup (`Program.cs`)?
- [ ] Are bulk backfills chunked in small batches rather than loading tracked EF entities?
