using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
public sealed class ProofDb(DbContextOptions<ProofDb> options):DbContext(options) {
 public DbSet<Order> Orders=>Set<Order>();
 public DbSet<User> Users=>Set<User>();
 public DbSet<Asset> Assets=>Set<Asset>();
 public DbSet<EagerOrder> EagerOrders=>Set<EagerOrder>();
 public DbSet<OrderLine> OrderLines=>Set<OrderLine>();
 public DbSet<FilteredOrder> FilteredOrders=>Set<FilteredOrder>();
 public DbSet<FilteredChild> FilteredChildren=>Set<FilteredChild>();
 protected override void OnModelCreating(ModelBuilder b) {
 b.Entity<Order>().ToTable("RowOrders").Property(x=>x.TotalAmount).HasPrecision(18,2);
 b.Entity<User>().HasOne(x=>x.Profile).WithOne().HasForeignKey<Profile>(x=>x.UserId);
 b.Entity<FilteredOrder>().ToTable("FilteredOrders");
 b.Entity<FilteredChild>().ToTable("FilteredChildren").HasOne(x=>x.Order).WithMany().HasForeignKey(x=>x.OrderId).IsRequired();
 }
}
public sealed class User {public Guid Id{get;set;} public string Email{get;set;}="";public bool IsActive{get;set;} public Profile? Profile{get;set;}}
public sealed class Profile {public Guid Id{get;set;}public Guid UserId{get;set;}public string Bio{get;set;}="";}
public sealed class Asset {public Guid Id{get;set;}public string Name{get;set;}="";public Guid OwnerId{get;set;}public bool IsArchived{get;set;}}
public sealed class EagerOrder {public Guid Id{get;set;}public List<OrderLine> Lines{get;set;}=[];}
public sealed class OrderLine {public Guid Id{get;set;}public Guid EagerOrderId{get;set;}public int Quantity{get;set;}}
public sealed class FilteredOrder {public Guid Id{get;set;}public bool IsDeleted{get;set;}public Guid TenantId{get;set;}}
public sealed class FilteredChild {public Guid Id{get;set;}public Guid OrderId{get;set;}public FilteredOrder Order{get;set;}=null!;}
public sealed class CommandCounter:DbCommandInterceptor {
 public int Reads;
 public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default) {Interlocked.Increment(ref Reads);return ValueTask.FromResult(result);}
}
