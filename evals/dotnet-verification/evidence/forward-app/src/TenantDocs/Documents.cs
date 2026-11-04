using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace TenantDocs;

public sealed class StoreOptions
{
    public string ConnectionString { get; set; } = "";
}

public sealed class TenantContext(IHttpContextAccessor accessor)
{
    public Guid Id => Guid.TryParse(accessor.HttpContext?.User.FindFirst("tenant_id")?.Value, out var id)
        && id != Guid.Empty ? id : throw new InvalidOperationException("An authenticated tenant is required.");
}

public sealed class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Title { get; set; }
}

public sealed class DocumentsDb(DbContextOptions<DocumentsDb> options, TenantContext tenant) : DbContext(options)
{
    private Guid CurrentTenantId => tenant.Id;
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();
        document.HasKey(x => new { x.TenantId, x.Id });
        document.Property(x => x.Title).HasMaxLength(200);
        document.HasQueryFilter(x => x.TenantId == CurrentTenantId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Document>())
        {
            if (entry.State == EntityState.Added) entry.Entity.TenantId = CurrentTenantId;
            else if (entry.State is EntityState.Modified or EntityState.Deleted &&
                (entry.Entity.TenantId != CurrentTenantId || entry.Property(x => x.TenantId).OriginalValue != CurrentTenantId))
                throw new InvalidOperationException("Cross-tenant writes are forbidden.");
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

public sealed record DocumentResponse(Guid Id, string Title);
public sealed record UpdateDocumentRequest(string Title);

public sealed class DocumentService(DocumentsDb db)
{
    public Task<DocumentResponse?> GetAsync(Guid id, CancellationToken ct) => db.Documents.AsNoTracking()
        .Where(x => x.Id == id).Select(x => new DocumentResponse(x.Id, x.Title)).SingleOrDefaultAsync(ct);

    public async Task<bool> UpdateAsync(Guid id, string title, CancellationToken ct)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (document is null) return false;
        document.Title = title;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documents");
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        return group;
    }

    private static async Task<Results<Ok<DocumentResponse>, NotFound>> GetAsync(Guid id, DocumentService service, CancellationToken ct)
    {
        var document = await service.GetAsync(id, ct);
        return document is null ? TypedResults.NotFound() : TypedResults.Ok(document);
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> UpdateAsync(Guid id,
        UpdateDocumentRequest request, DocumentService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title must contain 1 to 200 nonblank characters."] });
        return await service.UpdateAsync(id, request.Title, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
