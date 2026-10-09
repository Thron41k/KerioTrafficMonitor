
using KerioTrafficMonitor.Application.Options;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static System.Net.Mime.MediaTypeNames;

namespace KerioTrafficMonitor.Application.Services;

public sealed class UserRotationService(
    IKerioClientFactory clientFactory,
    ICredentialStore credentialStore,
    IUserStore userStore,
    IOptionsMonitor<MonitoringOptions> options,
    ILogger<UserRotationService> logger)
    : IUserRotationService, IAsyncDisposable
{
    private readonly IOptionsMonitor<MonitoringOptions> _options = options;

    private readonly Lock _sync = new();

    private readonly Dictionary<Guid, KerioUserStatus> _statuses = [];

    private List<KerioUser> _users = [];

    private IKerioClient? _client;
    private KerioUser? _currentUser;
    private TrafficInfo? _traffic;

    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;

    private string? _error;
    private DateTimeOffset? _updatedAt;

    public event EventHandler<MonitoringSnapshot>? SnapshotChanged;

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        if (_monitoringTask is not null)
            return;

        _users = (await userStore.LoadAsync(cancellationToken))
            .OrderBy(x => x.Priority)
            .ToList();

        NormalizePriorities(_users);

        InitializeStatuses();

        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        _monitoringTask = MonitorAsync(_monitoringCts.Token);

        await SwitchToFirstAvailableAsync(
            _monitoringCts.Token);
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (_monitoringCts is null)
            return;

        await _monitoringCts.CancelAsync();

        if (_monitoringTask is not null)
        {
            try
            {
                await _monitoringTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _monitoringTask = null;

        await LogoutAndDisposeAsync(cancellationToken);

        _monitoringCts.Dispose();
        _monitoringCts = null;
    }

    public async Task UpdateUsersAsync(
        IReadOnlyCollection<KerioUser> users,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _users = users
                .OrderBy(x => x.Priority)
                .ToList();

            NormalizePriorities(_users);

            foreach (var user in _users)
            {
                if (!_statuses.ContainsKey(user.Id))
                {
                    _statuses[user.Id] =
                        KerioUserStatus.Waiting;
                }
            }

            var validIds = _users
                .Select(x => x.Id)
                .ToHashSet();

            foreach (var id in _statuses.Keys
                         .Where(x => !validIds.Contains(x))
                         .ToList())
            {
                _statuses.Remove(id);
            }
        }

        Publish();

        await Task.CompletedTask;
    }

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_currentUser is null || _client is null)
        {
            if (_options.CurrentValue.AutomaticSwitching)
            {
                await SwitchToFirstAvailableAsync(
                    cancellationToken);
            }

            return;
        }

        try
        {
            var traffic =
                await _client.GetTrafficInfoAsync(
                    cancellationToken);

            _traffic = traffic;
            _updatedAt = DateTimeOffset.Now;
            _error = null;

            if (traffic.QuotaUsedPercent >=
                _options.CurrentValue.SwitchThresholdPercent)
            {
                _statuses[_currentUser.Id] =
                    KerioUserStatus.LimitReached;

                Publish();

                if (_options.CurrentValue.AutomaticSwitching)
                {
                    await SwitchToNextUserAsync(
                        cancellationToken);
                }

                return;
            }

            _statuses[_currentUser.Id] =
                KerioUserStatus.Active;

            Publish();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Ошибка обновления состояния Kerio.");

            _error = ex.Message;

            if (_currentUser is not null)
            {
                _statuses[_currentUser.Id] =
                    KerioUserStatus.Error;
            }

            Publish();
        }
    }

    public async Task SwitchToNextUserAsync(
        CancellationToken cancellationToken = default)
    {
        List<KerioUser> candidates;

        lock (_sync)
        {
            candidates = _users
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.Priority)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            _error = "Нет активных учетных записей.";
            Publish();
            return;
        }

        var currentId = _currentUser?.Id;

        var nextCandidates = currentId.HasValue
            ? candidates
                .SkipWhile(x => x.Id != currentId.Value)
                .Skip(1)
                .Concat(candidates.TakeWhile(x => x.Id != currentId.Value))
                .ToList()
            : candidates;

        foreach (var candidate in nextCandidates)
        {
            if (await TrySwitchToAsync(
                    candidate,
                    cancellationToken))
            {
                return;
            }
        }

        _currentUser = null;
        _traffic = null;

        _error =
            "Не удалось подключить ни одну доступную учетную запись.";

        Publish();
    }

    private async Task MonitorAsync(
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(
                Math.Max(1, _options.CurrentValue.IntervalSeconds)));

        while (await timer.WaitForNextTickAsync(
                   cancellationToken))
        {
            await RefreshAsync(
                cancellationToken);
        }
    }

    private async Task SwitchToFirstAvailableAsync(
        CancellationToken cancellationToken)
    {
        List<KerioUser> candidates;

        lock (_sync)
        {
            candidates = _users
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.Priority)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            _error = "Нет активных учетных записей.";
            Publish();
            return;
        }

        foreach (var candidate in candidates)
        {
            if (await TrySwitchToAsync(
                    candidate,
                    cancellationToken))
            {
                return;
            }
        }

        _currentUser = null;
        _traffic = null;

        _error =
            "Не удалось подключить ни одну доступную учетную запись.";

        Publish();
    }

    private async Task<bool> TrySwitchToAsync(
        KerioUser user,
        CancellationToken cancellationToken)
    {
        var password =
            await credentialStore.GetPasswordAsync(
                user.Id,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(password))
        {
            _statuses[user.Id] =
                KerioUserStatus.AuthenticationFailed;

            _error =
                $"Для пользователя {user.Username} не найден пароль.";

            Publish();

            return false;
        }
        var previousUser = _currentUser;

        if (previousUser is not null &&
            previousUser.Id != user.Id &&
            _statuses.TryGetValue(
                previousUser.Id,
                out var previousStatus) &&
            previousStatus == KerioUserStatus.Active)
        {
            _statuses[previousUser.Id] =
                KerioUserStatus.Waiting;
        }
        await LogoutAndDisposeAsync(
            cancellationToken);

        var client = clientFactory.Create();

        _statuses[user.Id] =
            KerioUserStatus.Waiting;

        Publish();

        // =========================================================
        // 1. АВТОРИЗАЦИЯ
        // =========================================================

        try
        {
            logger.LogInformation(
                "Авторизация пользователя {Username}.",
                user.Username);

            await client.LoginAsync(
                user.Username,
                password,
                cancellationToken);

            logger.LogInformation(
                "Авторизация пользователя {Username} выполнена.",
                user.Username);
        }
        catch (OperationCanceledException)
        {
            await client.DisposeAsync();
            throw;
        }
        catch (Exception ex)
        {
            await client.DisposeAsync();

            _statuses[user.Id] =
                KerioUserStatus.AuthenticationFailed;

            _error =
                $"{user.Username}: {ex.Message}";

            logger.LogWarning(
                ex,
                "Ошибка авторизации {Username}.",
                user.Username);

            Publish();

            return false;
        }

        // =========================================================
        // 2. ПОЛУЧЕНИЕ ТРАФИКА
        // =========================================================

        TrafficInfo traffic;

        try
        {
            logger.LogInformation(
                "Получение трафика пользователя {Username}.",
                user.Username);

            traffic =
                await client.GetTrafficInfoAsync(
                    cancellationToken);

            logger.LogInformation(
                "Трафик пользователя {Username}: {QuotaUsed:F2}%.",
                user.Username,
                traffic.QuotaUsedPercent);
        }
        catch (OperationCanceledException)
        {
            await client.DisposeAsync();
            throw;
        }
        catch (Exception ex)
        {
            await client.DisposeAsync();

            // ВАЖНО:
            // LoginAsync() уже успешно завершился.
            // Поэтому ошибка MyAccount.get / JSON-RPC / HTTP
            // НЕ является ошибкой авторизации.
            _statuses[user.Id] =
                KerioUserStatus.Error;

            _error =
                $"{user.Username}: {ex.Message}";

            logger.LogWarning(
                ex,
                "Ошибка получения трафика {Username}.",
                user.Username);

            Publish();

            return false;
        }

        // =========================================================
        // 3. ПРОВЕРКА ЛИМИТА
        // =========================================================

        if (traffic.QuotaUsedPercent >=
            _options.CurrentValue.SwitchThresholdPercent)
        {
            _statuses[user.Id] =
                KerioUserStatus.LimitReached;

            _error =
                $"{user.Username}: достигнут лимит трафика " +
                $"({traffic.QuotaUsedPercent:F2}%).";

            await client.LogoutAsync(
                cancellationToken);

            await client.DisposeAsync();

            Publish();

            return false;
        }

        // =========================================================
        // 4. УСПЕШНОЕ ПОДКЛЮЧЕНИЕ
        // =========================================================

        _client = client;
        _currentUser = user;
        _traffic = traffic;
        _updatedAt = DateTimeOffset.Now;
        _error = null;

        _statuses[user.Id] =
            KerioUserStatus.Active;

        Publish();

        return true;
    }

    private async Task LogoutAndDisposeAsync(
        CancellationToken cancellationToken)
    {
        var client = _client;

        _client = null;
        _currentUser = null;
        _traffic = null;

        if (client is null)
            return;

        try
        {
            await client.LogoutAsync(
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Ошибка при выходе из Kerio.");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private void InitializeStatuses()
    {
        lock (_sync)
        {
            _statuses.Clear();

            foreach (var user in _users)
            {
                _statuses[user.Id] =
                    KerioUserStatus.Waiting;
            }
        }
    }

    private void Publish()
    {
        Dictionary<Guid, KerioUserStatus> statuses;

        lock (_sync)
        {
            statuses =
                new Dictionary<Guid, KerioUserStatus>(
                    _statuses);
        }

        SnapshotChanged?.Invoke(
            this,
            new MonitoringSnapshot(
                _currentUser,
                _traffic,
                statuses,
                _error,
                _updatedAt));
    }

    private static void NormalizePriorities(
        IList<KerioUser> users)
    {
        for (var i = 0; i < users.Count; i++)
        {
            users[i].Priority = i + 1;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_monitoringCts is not null)
        {
            await _monitoringCts.CancelAsync();
        }

        if (_monitoringTask is not null)
        {
            try
            {
                await _monitoringTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await LogoutAndDisposeAsync(
            CancellationToken.None);

        _monitoringCts?.Dispose();
        _monitoringCts = null;
        _monitoringTask = null;
    }
}