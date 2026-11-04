namespace SliceTemplate;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Unexpected
}

public readonly record struct AppError(string Code, string Description, ErrorType Type = ErrorType.Validation);

public readonly record struct Result<TValue>
{
    private readonly TValue? _value;
    private readonly AppError? _error;

    public bool IsSuccess { get; }
    public TValue Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot access value of a failure result.");
    public AppError Error => !IsSuccess ? _error! : throw new InvalidOperationException("Cannot access error of a success result.");

    private Result(TValue value)
    {
        IsSuccess = true;
        _value = value;
        _error = null;
    }

    private Result(AppError error)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
    }

    public static Result<TValue> Success(TValue value) => new(value);
    public static Result<TValue> Failure(AppError error) => new(error);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<AppError, TResult> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(_error!);
}
