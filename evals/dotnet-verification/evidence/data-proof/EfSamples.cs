using Microsoft.EntityFrameworkCore;
public sealed record UserDto(Guid Id,string Email);
public sealed record AssetDto(Guid Id,string Name);
public static class EfSamples {
 public static async Task<List<UserDto>> ReadUsers(ProofDb db,CancellationToken cancellationToken) {
var users = await db.Users
    .AsNoTracking()
    .Where(x => x.IsActive)
    .OrderBy(x => x.Email)
    .Select(x => new UserDto(x.Id, x.Email))
    .ToListAsync(cancellationToken);

 return users;
 }
 public static async Task<List<AssetDto>> Page(ProofDb db,Guid ownerId,int offset,int limit,CancellationToken cancellationToken) {
var page = await db.Assets
    .AsNoTracking()
    .Where(x => x.OwnerId == ownerId)
    .OrderBy(x => x.Id)
    .Skip(offset)
    .Take(Math.Min(limit, 100))
    .Select(x => new AssetDto(x.Id, x.Name))
    .ToListAsync(cancellationToken);

 return page;
 }
 public static async Task DeleteArchived(ProofDb db,CancellationToken cancellationToken) {
await db.Assets
    .Where(x => x.IsArchived)
    .ExecuteDeleteAsync(cancellationToken);

 }
 public static async Task Save(ProofDb db,CancellationToken cancellationToken) {
try
{
    await db.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateConcurrencyException)
{
    // Translate according to application semantics; do not silently overwrite newer state.
}

 }
 public static async Task<List<Asset>> Raw(ProofDb db,Guid ownerId,CancellationToken cancellationToken) {
var rows = await db.Assets
    .FromSql($"SELECT * FROM Assets WHERE OwnerId = {ownerId}")
    .ToListAsync(cancellationToken);

 return rows;
 }
}
