using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed class KerioClient : IKerioClient
{
    private readonly KerioHttpSession _session;
    private readonly KerioTrafficParser _parser;
    private long _rpcId;
    private bool _authenticated;

    public KerioClient(Uri baseAddress, KerioTrafficParser parser)
    {
        _session = new KerioHttpSession(baseAddress);
        _parser = parser;
    }

    public async Task LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(
        [
            new("kerio_username", username),
            new("kerio_password", password)
        ]);

        using var response = await _session.Client.PostAsync(
            "/internal/dologin.php?NTLM=0&hash=myAccount_status_all",
            content,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.Found)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Kerio login failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        var token = _session.GetToken(_session.Client.BaseAddress!);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Kerio не вернул TOKEN_CONTROL_WEBIFACE.");

        _authenticated = true;
    }

    public async Task<TrafficInfo> GetTrafficInfoAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        using var response = await _session.Client.GetAsync("/", cancellationToken);

        if (response.StatusCode == HttpStatusCode.Found)
            throw new UnauthorizedAccessException("Kerio session expired.");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return await _parser.ParseAsync(html, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (!_authenticated)
            return;

        try
        {
            await SendRpcAsync<object?>("Session.logout", null, cancellationToken);
        }
        finally
        {
            _authenticated = false;
        }
    }

    private async Task<T?> SendRpcAsync<T>(string method, object? parameters, CancellationToken cancellationToken)
    {
        EnsureAuthenticated();

        var token = _session.GetToken(_session.Client.BaseAddress!);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Kerio token отсутствует.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/lib/api/jsonrpc/");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Token", token);
        request.Content = JsonContent.Create(new JsonRpcRequest(
            "2.0",
            Interlocked.Increment(ref _rpcId),
            method,
            parameters));

        using var response = await _session.Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rpc = await response.Content.ReadFromJsonAsync<JsonRpcResponse<T>>(cancellationToken);
        if (rpc is null)
            throw new InvalidOperationException("Пустой ответ JSON-RPC.");

        if (rpc.Error is not null)
            throw new KerioApiException(rpc.Error.Code, rpc.Error.Message);

        return rpc.Result;
    }

    private void EnsureAuthenticated()
    {
        if (!_authenticated)
            throw new InvalidOperationException("Kerio session is not authenticated.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_authenticated)
        {
            try { await LogoutAsync(); } catch { }
        }

        _session.Dispose();
    }
}
