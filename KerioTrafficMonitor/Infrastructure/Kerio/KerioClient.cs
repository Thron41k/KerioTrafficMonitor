using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;
using KerioTrafficMonitor.Infrastructure.Kerio.Models;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using KerioTrafficMonitor.Domain.Models.DTO;

namespace KerioTrafficMonitor.Infrastructure.Kerio;

internal sealed class KerioClient : IKerioClient
{
    private readonly KerioHttpSession _session;
    private readonly JsonRpcClient _jsonRpc;

    private bool _authenticated;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public KerioClient(
        Uri baseAddress,
        KerioTrafficParser parser)
    {
        _session = new KerioHttpSession(baseAddress);
        _jsonRpc = new JsonRpcClient(
            _session.Client,
            () => _session.GetToken(
                _session.Client.BaseAddress!));
    }

    public async Task LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
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
            var body =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            throw new HttpRequestException(
                $"Kerio login failed: " +
                $"{(int)response.StatusCode} " +
                $"{response.ReasonPhrase}. " +
                $"{body}");
        }

        var token =
            _session.GetToken(
                _session.Client.BaseAddress!);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Kerio не вернул TOKEN_CONTROL_WEBIFACE.");
        }
        _authenticated = true;
    }

    public async Task<TrafficInfo> GetTrafficInfoAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var parameters = new
        {
            commandList = new[]
            {
                new
                {
                    method = "MyAccount.get"
                },
                new
                {
                    method = "MyAccount.getRasIntefaces"
                }
            }
        };

        var response =
            await _jsonRpc.CallAsync<KerioBatchCommandResult[]>(
                "Batch.run",
                parameters,
                cancellationToken);

        if (response is null || response.Length == 0)
        {
            throw new InvalidOperationException(
                "Kerio не вернул результат Batch.run.");
        }

        var account =
            response[0].Result.Deserialize<KerioMyAccount>(
                JsonOptions);

        if (account?.Quota?.Day is null)
        {
            throw new InvalidOperationException(
                "Kerio не вернул данные дневной квоты.");
        }

        var day = account.Quota.Day;

        if (!long.TryParse(
                day.Down,
                out var receivedBytes))
        {
            throw new InvalidOperationException(
                $"Некорректное quota.day.down: '{day.Down}'.");
        }

        if (!long.TryParse(
                day.Up,
                out var sentBytes))
        {
            throw new InvalidOperationException(
                $"Некорректное quota.day.up: '{day.Up}'.");
        }

        if (!long.TryParse(
                day.Value,
                out var quotaBytes))
        {
            throw new InvalidOperationException(
                $"Некорректное quota.day.value: '{day.Value}'.");
        }

        var totalBytes =
            receivedBytes + sentBytes;

        var quotaUsedPercent =
            quotaBytes > 0
                ? totalBytes * 100d / quotaBytes
                : 0d;

        return new TrafficInfo(
            DateOnly.FromDateTime(DateTime.Now),
            receivedBytes,
            sentBytes,
            quotaUsedPercent);
    }
    public async Task LogoutAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_authenticated)
            return;

        try
        {
            /*
             * Используем существующий JsonRpcClient.
             * Никакого SendRpcAsync/JsonRpcRequest больше нет.
             */
            await _jsonRpc.CallAsync<object?>(
                "Session.logout",
                null,
                cancellationToken);
        }
        finally
        {
            _authenticated = false;
        }
    }

    private void EnsureAuthenticated()
    {
        if (!_authenticated)
        {
            throw new InvalidOperationException(
                "Kerio session is not authenticated.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_authenticated)
        {
            try
            {
                await LogoutAsync();
            }
            catch
            {
                // При освобождении клиента
                // ошибка logout не должна мешать Dispose.
            }
        }

        _session.Dispose();
    }
}