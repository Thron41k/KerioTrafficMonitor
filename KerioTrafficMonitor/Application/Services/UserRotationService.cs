using System.Net.Http;
using KerioTrafficMonitor.Application.Options;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;
using Microsoft.Extensions.Options;

namespace KerioTrafficMonitor.Application.Services;

internal sealed class UserRotationService : IUserRotationService, IAsyncDisposable
{
    private readonly IUserStore _userStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IKerioClientFactory _clientFactory;
    private readonly MonitoringOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, KerioUserStatus> _statuses = [];
    private readonly CancellationTokenSource _lifetime = new();

    private List<KerioUser> _users = [];
    private IKerioClient? _client;
    private KerioUser? _currentUser;
    private TrafficInfo? _traffic;
    private Task? _monitorTask;
    private string? _error;
    private DateTimeOffset? _updatedAt;

    public event EventHandler<MonitoringSnapshot>? SnapshotChanged;

    public UserRotationService(
        IUserStore userStore,
        ICredentialStore credentialStore,
        IKerioClientFactory clientFactory,
        IOptions<MonitoringOptions> options)
    {
        _userStore = userStore;
        _credentialStore = credentialStore;
        _clientFactory = clientFactory;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_monitorTask is not null)
            return;

        _users = (await _userStore.LoadAsync(cancellationToken)).OrderBy(x => x.Priority).ToList();
        NormalizePriorities();
        Publish();

        _monitorTask = MonitorLoopAsync(_lifetime.Token);
        await Task.Yield();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_monitorTask is null)
            return;

        _lifetime.Cancel();
        try { await _monitorTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        finally { _monitorTask = null; }

        await LogoutAndDisposeAsync(cancellationToken);
    }

    public async Task UpdateUsersAsync(IReadOnlyCollection<KerioUser> users, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var currentId = _currentUser?.Id;
            _users = users.OrderBy(x => x.Priority).ToList();
            NormalizePriorities();

            foreach (var user in _users)
                _statuses.TryAdd(user.Id, user.IsEnabled ? KerioUserStatus.Waiting : KerioUserStatus.Disabled);

            if (currentId is not null && _users.All(x => x.Id != currentId.Value))
            {
                await LogoutAndDisposeAsync(cancellationToken);
                _currentUser = null;
                _traffic = null;
            }

            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SwitchToNextUserAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var next = GetNextUser();
            if (next is null)
            {
                _error = "Нет доступных учетных записей.";
                Publish();
                return;
            }

            await SwitchToAsync(next, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_currentUser is null)
            {
                var next = GetNextUser();
                if (next is not null)
                    await SwitchToAsync(next, cancellationToken);
                return;
            }

            try
            {
                _traffic = await _client!.GetTrafficInfoAsync(cancellationToken);
                _error = null;
                _updatedAt = DateTimeOffset.Now;
                Publish();
            }
            catch (Exception ex) when (ex is HttpRequestException or UnauthorizedAccessException or InvalidOperationException)
            {
                _error = ex.Message;
                Publish();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SwitchToFirstAvailableAsync(cancellationToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.IntervalSeconds)));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await MonitorOnceAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _error = ex.Message;
            Publish();
        }
    }

    private async Task MonitorOnceAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_currentUser is null)
            {
                await SwitchToFirstAvailableUnsafeAsync(cancellationToken);
                return;
            }

            try
            {
                _traffic = await _client!.GetTrafficInfoAsync(cancellationToken);
                _updatedAt = DateTimeOffset.Now;
                _error = null;
                _statuses[_currentUser.Id] = KerioUserStatus.Active;
                Publish();

                if (_options.AutomaticSwitching &&
                    _traffic.QuotaUsedPercent >= _options.SwitchThresholdPercent)
                {
                    _statuses[_currentUser.Id] = KerioUserStatus.LimitReached;
                    var next = GetNextUser();
                    if (next is null)
                    {
                        _error = "Все доступные учетные записи исчерпали лимит.";
                        Publish();
                        return;
                    }

                    await SwitchToAsync(next, cancellationToken);
                }
            }
            catch (UnauthorizedAccessException)
            {
                var next = GetNextUser();
                if (next is not null)
                    await SwitchToAsync(next, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _error = ex.Message;
                Publish();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SwitchToFirstAvailableAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await SwitchToFirstAvailableUnsafeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task SwitchToFirstAvailableUnsafeAsync(CancellationToken cancellationToken)
    {
        foreach (var candidate in _users.Where(x => x.IsEnabled).OrderBy(x => x.Priority))
        {
            var status = _statuses.GetValueOrDefault(candidate.Id, KerioUserStatus.Waiting);
            if (status is KerioUserStatus.LimitReached or KerioUserStatus.AuthenticationFailed)
                continue;

            try
            {
                await SwitchToAsync(candidate, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _error = $"{candidate.Username}: {ex.Message}";
                Publish();
            }
        }

        _error = "Не удалось авторизовать ни одну доступную учетную запись.";
        Publish();
    }

    private async Task SwitchToAsync(KerioUser user, CancellationToken cancellationToken)
    {
        var password = await _credentialStore.GetPasswordAsync(user.Id, cancellationToken);
        if (password is null)
        {
            _statuses[user.Id] = KerioUserStatus.AuthenticationFailed;
            _error = $"Для пользователя {user.Username} не найден пароль.";
            Publish();
            return;
        }

        await LogoutAndDisposeAsync(cancellationToken);

        var client = _clientFactory.Create();
        try
        {
            await client.LoginAsync(user.Username, password, cancellationToken);
            var traffic = await client.GetTrafficInfoAsync(cancellationToken);

            _client = client;
            _currentUser = user;
            _traffic = traffic;
            _updatedAt = DateTimeOffset.Now;
            _error = null;
            _statuses[user.Id] = traffic.QuotaUsedPercent >= _options.SwitchThresholdPercent
                ? KerioUserStatus.LimitReached
                : KerioUserStatus.Active;

            Publish();

            if (_options.AutomaticSwitching &&
                traffic.QuotaUsedPercent >= _options.SwitchThresholdPercent)
            {
                var next = GetNextUser();
                if (next is not null)
                    await SwitchToAsync(next, cancellationToken);
            }
        }
        catch
        {
            await client.DisposeAsync();
            _statuses[user.Id] = KerioUserStatus.AuthenticationFailed;
            throw;
        }
    }

    private KerioUser? GetNextUser()
    {
        var enabled = _users
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToList();

        if (enabled.Count == 0)
            return null;

        var currentIndex = _currentUser is null
            ? -1
            : enabled.FindIndex(x => x.Id == _currentUser.Id);

        for (var offset = 1; offset <= enabled.Count; offset++)
        {
            var index = (currentIndex + offset) % enabled.Count;
            var candidate = enabled[index];

            if (!_statuses.TryGetValue(candidate.Id, out var status) ||
                status is KerioUserStatus.Waiting or KerioUserStatus.Active)
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task LogoutAndDisposeAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
            return;

        try { await _client.LogoutAsync(cancellationToken); }
        catch { }
        finally
        {
            await _client.DisposeAsync();
            _client = null;
            _currentUser = null;
            _traffic = null;
        }
    }

    private void NormalizePriorities()
    {
        for (var i = 0; i < _users.Count; i++)
            _users[i].Priority = i + 1;
    }

    private void Publish() => SnapshotChanged?.Invoke(
        this,
        new MonitoringSnapshot(_currentUser, _traffic,
            _currentUser is null ? null : _statuses.GetValueOrDefault(_currentUser.Id),
            _error,
            _updatedAt));

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifetime.Dispose();
        _gate.Dispose();
    }
}
