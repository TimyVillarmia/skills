using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
migrationBuilder.AddColumn<string>(
    name: "DisplayName",
    table: "Users",
    type: "text",
    nullable: true);
migrationBuilder.DropColumn(
    name: "FullName",
    table: "Users");
// Step 1: Adds constraint without validating existing rows (instant, no table lock)
migrationBuilder.Sql(
    "ALTER TABLE \"Orders\" ADD CONSTRAINT fk_orders_customer " +
    "FOREIGN KEY (\"CustomerId\") REFERENCES \"Customers\" (\"Id\") NOT VALID;");

// Step 2: Validates existing rows without blocking concurrent writes
migrationBuilder.Sql(
    "ALTER TABLE \"Orders\" VALIDATE CONSTRAINT fk_orders_customer;");
migrationBuilder.Sql(
    "CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders (CustomerId) WITH (ONLINE = ON);");

var example = new AddUserEmailIndex();
if (example.UpOperations.OfType<SqlOperation>().Single().SuppressTransaction != true || example.DownOperations.OfType<SqlOperation>().Single().SuppressTransaction != true) throw new Exception("CONCURRENTLY transaction suppression missing");
var user = new User(); user.Apply(new Request("changed"));
if (user.DisplayName != "changed" || user.OldFullName != "changed") throw new Exception("Dual-write assignment differs");
Console.WriteLine("Six C# migration snippets compile; suppression and dual-write assignments observed. SQL strings are not database execution proof.");
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

public record Request(string DisplayName);
public class User { public string? NewDisplayName {get;set;} public string OldFullName {get;set;} = "old";
// Read: Prefer new column, fallback to old
public string DisplayName => NewDisplayName ?? OldFullName;

public void Apply(Request request) { var user=this;

user.OldFullName = request.DisplayName;
user.NewDisplayName = request.DisplayName;
 } }
