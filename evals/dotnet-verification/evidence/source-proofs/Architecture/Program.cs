using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.SectionName)
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();


builder.Services.AddOptions<StorageOptions>()
    .BindConfiguration(StorageOptions.SectionName)
    .Validate(opts =>
    {
        if (opts.UseS3 && string.IsNullOrWhiteSpace(opts.BucketName))
            return false;
        return true;
    }, "BucketName is required when UseS3 is true.")
    .ValidateOnStart();

var validator = new DatabaseOptionsValidator();
if(validator.Validate(null,new DatabaseOptions()).Succeeded) throw new Exception("invalid options accepted");
if(!validator.Validate(null,new DatabaseOptions{ConnectionString="db"}).Succeeded) throw new Exception("valid options denied");
if(UserResponse.From(new User(Guid.Empty,"a","b")) != new UserResponse(Guid.Empty,"a","b")) throw new Exception("mapping failure");
Console.WriteLine("Architecture: options and mapping checks passed");
public interface IAssetNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken);
}
public interface IRepository<T>
{
    Task<T?> GetAsync(Guid id);
    Task AddAsync(T entity);
    Task SaveAsync();
}
public sealed record UserResponse(Guid Id, string Email, string FullName)
{
    // Single instance factory (e.g. after domain operations)
    public static UserResponse From(User user) =>
        new(user.Id, user.Email, user.FullName);

    // Reusable EF Core Projection expression (zero tracking, SQL-translated)
    public static Expression<Func<User, UserResponse>> Project =>
        u => new UserResponse(u.Id, u.Email, u.FullName);
}
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required, MinLength(1)]
    public string ConnectionString { get; init; } = string.Empty;

    [Range(1, 100)]
    public int MaxRetryCount { get; init; } = 3;
}

public sealed record User(Guid Id,string Email,string FullName);
public sealed class StorageOptions { public const string SectionName="Storage";public bool UseS3 {get;set;} public string? BucketName{get;set;} }

[OptionsValidator]
public partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
}