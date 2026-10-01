namespace HadalStream.Domain.Common;

public sealed record Error(string Message);

// Expected failures travel as values; exceptions stay for the unexpected.
public class Result
{
    private static readonly Result Ok = new(null);

    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result Success() => Ok;

    public static implicit operator Result(Error error) => new(error);
}

public sealed class Result<T> : Result
{
    private readonly T? value;

    private Result(T value) : base(null) => this.value = value;

    private Result(Error error) : base(error) { }

    public T Value => IsSuccess ? value! : throw new InvalidOperationException($"No value: {Error!.Message}");

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);

    public Result Bind(Func<T, Result> next) => IsSuccess ? next(value!) : Error!;
}
