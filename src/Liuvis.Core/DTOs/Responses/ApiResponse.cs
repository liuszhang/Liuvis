namespace Liuvis.Core.DTOs.Responses;

/// <summary>Standard API response wrapper.</summary>
public record ApiResponse<T>
{
    public int Code { get; init; } = 200;
    public T? Data { get; init; }
    public string Message { get; init; } = "success";

    public static ApiResponse<T> Ok(T data, string message = "success") =>
        new() { Code = 200, Data = data, Message = message };

    public static ApiResponse<T> Error(int code, string message) =>
        new() { Code = code, Data = default, Message = message };
}
