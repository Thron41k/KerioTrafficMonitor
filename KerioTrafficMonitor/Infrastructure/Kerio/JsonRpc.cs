using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed class JsonRpcClient
{
    private readonly HttpClient _client;
    private readonly Func<string?> _getToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public JsonRpcClient(
        HttpClient client,
        Func<string?> getToken)
    {
        _client = client;
        _getToken = getToken;
    }

    public async Task<T?> CallAsync<T>(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        var rpcRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(
            rpcRequest,
            JsonOptions);

        Console.WriteLine("========== KERIO RPC ==========");
        Console.WriteLine($"URL: {_client.BaseAddress}lib/api/jsonrpc/");
        Console.WriteLine($"Method: {method}");
        Console.WriteLine($"JSON length: {Encoding.UTF8.GetByteCount(json)}");
        Console.WriteLine($"JSON: {json}");

        var token = _getToken();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Kerio TOKEN_CONTROL_WEBIFACE отсутствует.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/lib/api/jsonrpc/");

        request.Headers.TryAddWithoutValidation(
            "X-Requested-With",
            "XMLHttpRequest");

        request.Headers.TryAddWithoutValidation(
            "X-Token",
            token);

        request.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        Console.WriteLine(
            $"Token: {token[..Math.Min(token.Length, 10)]}...");

        // Показываем cookies для диагностики.
        var cookieHeader = request.Headers.TryGetValues(
            "Cookie",
            out var requestCookies);

        Console.WriteLine(
            $"Cookie header already present: {cookieHeader}");

        Console.WriteLine("BEFORE SEND");

        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        Console.WriteLine("AFTER SEND");

        var responseText =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        Console.WriteLine(
            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        Console.WriteLine(
            $"Response: {responseText}");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Kerio HTTP error: " +
                $"{(int)response.StatusCode} " +
                $"{response.ReasonPhrase}. " +
                $"{responseText}");
        }

        var rpcResponse =
            JsonSerializer.Deserialize<JsonRpcResponse<T>>(
                responseText,
                JsonOptions);

        if (rpcResponse is null)
        {
            throw new InvalidOperationException(
                "Kerio вернул пустой JSON-RPC ответ.");
        }

        if (rpcResponse.Error is not null)
        {
            throw new InvalidOperationException(
                $"Kerio JSON-RPC error " +
                $"{rpcResponse.Error.Code}: " +
                $"{rpcResponse.Error.Message}");
        }

        return rpcResponse.Result;
    }
}

internal sealed class JsonRpcResponse<T>
{
    public string Jsonrpc { get; set; } = string.Empty;

    public int Id { get; set; }

    public T? Result { get; set; }

    public JsonRpcError? Error { get; set; }
}

internal sealed class JsonRpcError
{
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;

    public JsonElement? Data { get; set; }
}