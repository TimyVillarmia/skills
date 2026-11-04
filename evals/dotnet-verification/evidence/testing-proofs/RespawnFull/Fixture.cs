using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

[TestClass]
public static class DatabaseFixture
{
    private static readonly PostgreSqlContainer Container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("test_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public static string ConnectionString => Container.GetConnectionString();

    [AssemblyInitialize]
    public static async Task AssemblyInitialize(TestContext context)
    {
        await Container.StartAsync();
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
    {
        await Container.DisposeAsync();
    }
}
