namespace PanelForge.Application.Common;

/// <summary>
/// Generic result wrapper — thay thế việc throw exception cho business errors.
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }

    private Result(T value)       { IsSuccess = true;  Value = value; }
    private Result(string error)  { IsSuccess = false; ErrorMessage = error; }

    public static Result<T> Success(T value)       => new(value);
    public static Result<T> Failure(string error)  => new(error);
}
