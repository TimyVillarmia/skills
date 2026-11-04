using Microsoft.EntityFrameworkCore;
namespace FilterSnippet;
using Order=global::FilteredOrder;
public sealed class FilterDb(DbContextOptions<FilterDb> options,Guid tenant):DbContext(options) {
 private Guid currentTenantId => tenant;
 public DbSet<FilteredChild> Children=>Set<FilteredChild>();
 protected override void OnModelCreating(ModelBuilder modelBuilder) {
modelBuilder.Entity<Order>()
    .HasQueryFilter(o => !o.IsDeleted && o.TenantId == currentTenantId);

 modelBuilder.Entity<Order>().ToTable("FilteredOrders");
 modelBuilder.Entity<FilteredChild>().ToTable("FilteredChildren").HasOne(x=>x.Order).WithMany().HasForeignKey(x=>x.OrderId).IsRequired();
 }
}
