var builder=WebApplication.CreateBuilder(args); var app=builder.Build();
app.MapPut("/users/{id:guid}", async Task<IResult> (
    Guid id,
    UpdateUserRequest request,
    UserDbContext db,
    CancellationToken ct) =>
{
    var user = await db.Users.FindAsync([id], ct);
    if (user is null) return TypedResults.NotFound();

    // Explicit field assignment
    user.DisplayName = request.DisplayName;
    user.PhoneNumber = request.PhoneNumber;

    await db.SaveChangesAsync(ct);
    return TypedResults.NoContent();
});

app.Run();
public sealed record UpdateUserRequest(string DisplayName,string PhoneNumber);
public sealed class UserDbContext { public UserSet Users{get;}=new(); public Task SaveChangesAsync(CancellationToken ct)=>Task.CompletedTask; }
public sealed class UserSet { public ValueTask<User?> FindAsync(object[] id,CancellationToken ct)=>ValueTask.FromResult<User?>(null); }
public sealed class User {public string DisplayName{get;set;}="";public string PhoneNumber{get;set;}="";}
