using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

public class CustomWebApplicationFactory<TProgram>(string connectionString) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override connection string via configuration rather than mutating DI service descriptors.
        // This preserves DbContext pooling, interceptors, and provider registrations.
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
    }
}
