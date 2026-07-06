namespace CursorUsageNotify.Core;

/// <summary>
/// Result 模式：明确区分成功与失败，避免依赖异常控制流。
/// 失败时携带错误消息，调用方决定如何呈现给用户。
/// </summary>
/// <typeparam name="T">成功时携带的值类型。</typeparam>
public readonly record struct Result<T>
{
    /// <summary>是否成功。</summary>
    public bool IsSuccess { get; init; }

    /// <summary>成功时的值；失败时为 default。</summary>
    public T? Value { get; init; }

    /// <summary>失败时的错误消息；成功时为 null。</summary>
    public string? Error { get; init; }

    /// <summary>成功时抛出异常；失败时返回错误消息。</summary>
    public T ValueOrThrow() => IsSuccess
        ? Value!
        : throw new InvalidOperationException(Error ?? "Result was not successful.");

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Error = null;
    }

    private Result(string error)
    {
        IsSuccess = false;
        Value = default;
        Error = error;
    }

    /// <summary>构造成功结果。</summary>
    public static Result<T> Ok(T value) => new(value);

    /// <summary>构造失败结果。</summary>
    public static Result<T> Fail(string error) => new(error);
}
