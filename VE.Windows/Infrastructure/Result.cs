namespace VE.Windows.Infrastructure;

/// <summary>
/// Unified result type for operations that can fail.
/// Replaces ad-hoc null returns and bare catch blocks with explicit success/failure signaling.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public Exception? Exception { get; }

    protected Result(bool isSuccess, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        Error = error;
        Exception = exception;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, Exception? ex = null) => new(false, error, ex);

    public static Result<T> Success<T>(T value) => new(value, true, null, null);
    public static Result<T> Failure<T>(string error, Exception? ex = null) => new(default, false, error, ex);
}

/// <summary>
/// Generic result type carrying a value on success, or an error message on failure.
/// </summary>
public class Result<T> : Result
{
    public T? Value { get; }

    internal Result(T? value, bool isSuccess, string? error, Exception? exception)
        : base(isSuccess, error, exception)
    {
        Value = value;
    }

    /// <summary>
    /// Get the value or a fallback if the result is a failure.
    /// </summary>
    public T GetValueOrDefault(T fallback) => IsSuccess && Value != null ? Value : fallback;
}
