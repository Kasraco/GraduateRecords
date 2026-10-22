namespace GraduateRecords.Application.Common.Results;

/// <summary>
/// AGENTS §4: expected failures return a Result; exceptions are reserved for
/// unexpected failures.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) =>
        new(false, error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>A result that carries a value on success.</summary>
public sealed class Result<T> : Result
{
    private Result(T? value, bool isSuccess, Error? error)
        : base(isSuccess, error) => Value = value;

    public T? Value { get; }

    public static Result<T> Success(T value) => new(value, true, null);

    public static new Result<T> Failure(Error error) =>
        new(default, false, error ?? throw new ArgumentNullException(nameof(error)));
}
