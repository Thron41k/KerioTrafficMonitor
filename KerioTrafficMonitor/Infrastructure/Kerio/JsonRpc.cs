namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed record JsonRpcRequest(
    string Jsonrpc,
    long Id,
    string Method,
    object? Params);

internal sealed record JsonRpcResponse<T>(
    string? Jsonrpc,
    long Id,
    T? Result,
    JsonRpcError? Error);

internal sealed record JsonRpcError(int Code, string Message);

internal sealed class KerioApiException(int code, string message)
    : Exception($"Kerio API error {code}: {message}")
{
    public int Code { get; } = code;
}
