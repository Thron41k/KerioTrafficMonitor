using KerioTrafficMonitor.Application.Options;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    // Сериализует операции над текущим клиентом, пользователями и статусами.
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    // Не допускает одновременный StartAsync и StopAsync.
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private readonly Dictionary<Guid, KerioUserStatus> _statuses = [];

    private List<KerioUser> _users = [];
    private IKerioClient? _client;
    private KerioUser? _currentUser;
    private TrafficInfo? _traffic;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;
    private string? _error;
    private DateTimeOffset? _updatedAt;
    private volatile bool _isStopping;

    public event EventHandler<MonitoringSnapshot>? SnapshotChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            if (_monitoringCts is not null)
                return;

            await _operationGate.WaitAsync(cancellationToken);

            CancellationTokenSource? monitoringCts = null;

            try
            {
                _isStopping = false;

                _users = (await userStore.LoadAsync(cancellationToken))
                    .OrderBy(x => x.Priority)
                    .ToList();

                NormalizePriorities(_users);
                InitializeStatuses();

                // Токен вызова StartAsync отменяет только запуск.
                // Жизненным циклом фонового мониторинга управляет StopAsync.
                monitoringCts = new CancellationTokenSource();
                _monitoringCts = monitoringCts;

                await SwitchToFirstAvailableCoreAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                _monitoringTask = MonitorAsync(monitoringCts.Token);
            }
            catch
            {
                if (monitoringCts is not null)
                {
                    try
                    {
                        await monitoringCts.CancelAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Ошибка отмены запуска мониторинга.");
                    }
                }

                _monitoringTask = null;
                _monitoringCts = null;
                _isStopping = false;

                await LogoutAndDisposeAsync(CancellationToken.None);
                monitoringCts?.Dispose();
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            var monitoringCts = _monitoringCts;
            var monitoringTask = _monitoringTask;

            if (monitoringCts is null && monitoringTask is null && _client is null)
                return;

            // Запрещаем новым публичным операциям начинать работу.
            _isStopping = true;

            if (monitoringCts is not null)
            {
                try
                {
                    await monitoringCts.CancelAsync();
                }
                catch (Exception ex)
                {
                    // Ошибка callback отмены не должна мешать закрытию клиента.
                    logger.LogWarning(ex, "Ошибка отмены фонового мониторинга.");
                }
            }

            // Не удерживаем _operationGate во время ожидания таймера:
            // MonitorAsync может ждать этот же gate внутри RefreshAsync.
            if (monitoringTask is not null)
            {
                try
                {
                    await monitoringTask;
                }
                catch (OperationCanceledException)
                {
                    // Ожидаемый результат остановки таймера.
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка фонового мониторинга при остановке.");
                }
            }

            // После начала остановки cleanup должен завершиться даже при отмене
            // токена вызывающей стороны.
            await _operationGate.WaitAsync(CancellationToken.None);

            try
            {
                await LogoutAndDisposeAsync(CancellationToken.None);
            }
            finally
            {
                _monitoringTask = null;
                _monitoringCts = null;
                monitoringCts?.Dispose();
                _isStopping = false;
                _operationGate.Release();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task UpdateUsersAsync(
        IReadOnlyCollection<KerioUser> users,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(users);

        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_isStopping)
                return;

            var previousCurrentUser = _currentUser;

            _users = users
                .OrderBy(x => x.Priority)
                .ToList();

            NormalizePriorities(_users);

            foreach (var user in _users)
            {
                if (!user.IsEnabled)
                {
                    _statuses[user.Id] = KerioUserStatus.Disabled;
                }
                else if (!_statuses.TryGetValue(user.Id, out var status) ||
                         status == KerioUserStatus.Disabled)
                {
                    _statuses[user.Id] = KerioUserStatus.Waiting;
                }
            }

            var validIds = _users.Select(x => x.Id).ToHashSet();

            foreach (var id in _statuses.Keys.Where(id => !validIds.Contains(id)).ToList())
                _statuses.Remove(id);

            var updatedCurrentUser = previousCurrentUser is null
                ? null
                : _users.FirstOrDefault(x => x.Id == previousCurrentUser.Id);

            var currentUserWasRemovedOrDisabled =
                previousCurrentUser is not null &&
                (updatedCurrentUser is null || !updatedCurrentUser.IsEnabled);

            if (currentUserWasRemovedOrDisabled)
            {
                if (_statuses.ContainsKey(previousCurrentUser!.Id))
                    _statuses[previousCurrentUser.Id] = KerioUserStatus.Disabled;

                await LogoutAndDisposeAsync(cancellationToken);

                if (_options.CurrentValue.AutomaticSwitching)
                {
                    await SwitchToFirstAvailableCoreAsync(cancellationToken);
                }
                else
                {
                    _error = "Активная учетная запись удалена или отключена.";
                    Publish();
                }

                return;
            }

            // Сохраняем актуальную модель пользователя (например, новый приоритет).
            if (updatedCurrentUser is not null)
                _currentUser = updatedCurrentUser;

            Publish();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_isStopping)
                return;

            await RefreshCoreAsync(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SwitchToNextUserAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_isStopping)
                return;

            await SwitchToNextUserCoreAsync(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (_currentUser is null || _client is null)
        {
            if (_options.CurrentValue.AutomaticSwitching)
                await SwitchToFirstAvailableCoreAsync(cancellationToken);

            return;
        }

        try
        {
            var traffic = await _client.GetTrafficInfoAsync(cancellationToken);

            _traffic = traffic;
            _updatedAt = DateTimeOffset.Now;
            _error = null;

            if (traffic.QuotaUsedPercent >= _options.CurrentValue.SwitchThresholdPercent)
            {
                _statuses[_currentUser.Id] = KerioUserStatus.LimitReached;
                Publish();

                if (_options.CurrentValue.AutomaticSwitching)
                    await SwitchToNextUserCoreAsync(cancellationToken);

                return;
            }

            _statuses[_currentUser.Id] = KerioUserStatus.Active;
            Publish();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка обновления состояния Kerio.");
            _error = ex.Message;

            if (_currentUser is not null)
                _statuses[_currentUser.Id] = KerioUserStatus.Error;

            Publish();
        }
    }

    private async Task SwitchToNextUserCoreAsync(CancellationToken cancellationToken)
    {
        var candidates = _users
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToList();

        if (candidates.Count == 0)
        {
            await LogoutAndDisposeAsync(cancellationToken);
            _error = "Нет активных учетных записей.";
            Publish();
            return;
        }

        var currentId = _currentUser?.Id;
        var currentIndex = currentId.HasValue
            ? candidates.FindIndex(x => x.Id == currentId.Value)
            : -1;

        // Если текущая учётная запись есть в списке, обходим остальные по кругу.
        // Если её уже нет среди кандидатов, пробуем весь список с начала.
        var nextCandidates = currentIndex >= 0
            ? candidates.Skip(currentIndex + 1)
                .Concat(candidates.Take(currentIndex))
                .ToList()
            : candidates;

        if (nextCandidates.Count == 0)
        {
            var currentUserReachedLimit =
                _currentUser is not null &&
                _statuses.TryGetValue(_currentUser.Id, out var status) &&
                status == KerioUserStatus.LimitReached;

            // При автоматическом переключении не оставляем подключение
            // к аккаунту, у которого исчерпана квота.
            if (currentUserReachedLimit)
                await LogoutAndDisposeAsync(cancellationToken);

            _error = currentUserReachedLimit
                ? "У всех доступных учетных записей исчерпан лимит трафика."
                : "Нет другой активной учетной записи для переключения.";

            Publish();
            return;
        }

        foreach (var candidate in nextCandidates)
        {
            if (await TrySwitchToCoreAsync(candidate, cancellationToken))
                return;
        }

        // Все кандидаты отклонены; не оставляем старое соединение незамеченным.
        await LogoutAndDisposeAsync(CancellationToken.None);
        _error = "Не удалось подключить ни одну доступную учетную запись.";
        Publish();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.IntervalSeconds)));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Остановка через StopAsync.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Фоновый мониторинг неожиданно завершился.");

            await _operationGate.WaitAsync(CancellationToken.None);

            try
            {
                if (!_isStopping)
                {
                    _error = $"Фоновый мониторинг остановлен: {ex.Message}";

                    if (_currentUser is not null)
                        _statuses[_currentUser.Id] = KerioUserStatus.Error;

                    Publish();
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }
    }

    private async Task SwitchToFirstAvailableCoreAsync(CancellationToken cancellationToken)
    {
        var candidates = _users
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToList();

        if (candidates.Count == 0)
        {
            _error = "Нет активных учетных записей.";
            Publish();
            return;
        }

        foreach (var candidate in candidates)
        {
            if (await TrySwitchToCoreAsync(candidate, cancellationToken))
                return;
        }

        await LogoutAndDisposeAsync(CancellationToken.None);
        _error = "Не удалось подключить ни одну доступную учетную запись.";
        Publish();
    }

    private async Task<bool> TrySwitchToCoreAsync(
        KerioUser user,
        CancellationToken cancellationToken)
    {
        string? password;

        try
        {
            password = await credentialStore.GetPasswordAsync(user.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _statuses[user.Id] = KerioUserStatus.Error;
            _error = $"{user.Username}: не удалось прочитать сохранённый пароль: {ex.Message}";
            logger.LogWarning(ex, "Не удалось получить пароль пользователя {Username}.", user.Username);
            Publish();
            return false;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            _statuses[user.Id] = KerioUserStatus.AuthenticationFailed;
            _error = $"Для пользователя {user.Username} не найден пароль.";
            Publish();
            return false;
        }

        var previousUser = _currentUser;

        if (previousUser is not null &&
            previousUser.Id != user.Id &&
            _statuses.TryGetValue(previousUser.Id, out var previousStatus) &&
            previousStatus == KerioUserStatus.Active)
        {
            _statuses[previousUser.Id] = KerioUserStatus.Waiting;
        }

        await LogoutAndDisposeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _statuses[user.Id] = KerioUserStatus.Waiting;
        Publish();

        IKerioClient client;

        try
        {
            client = clientFactory.Create();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _statuses[user.Id] = KerioUserStatus.Error;
            _error = $"{user.Username}: {ex.Message}";
            logger.LogWarning(ex, "Не удалось создать Kerio-клиент для {Username}.", user.Username);
            Publish();
            return false;
        }

        try
        {
            logger.LogInformation("Авторизация пользователя {Username}.", user.Username);
            await client.LoginAsync(user.Username, password, cancellationToken);
            logger.LogInformation("Авторизация пользователя {Username} выполнена.", user.Username);
        }
        catch (OperationCanceledException)
        {
            await DisposeClientSafelyAsync(client);
            throw;
        }
        catch (Exception ex)
        {
            await DisposeClientSafelyAsync(client);

            _statuses[user.Id] = KerioUserStatus.AuthenticationFailed;
            _error = $"{user.Username}: {ex.Message}";
            logger.LogWarning(ex, "Ошибка авторизации {Username}.", user.Username);
            Publish();
            return false;
        }

        TrafficInfo traffic;

        try
        {
            logger.LogInformation("Получение трафика пользователя {Username}.", user.Username);
            traffic = await client.GetTrafficInfoAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation(
                "Трафик пользователя {Username}: {QuotaUsed:F2}%.",
                user.Username,
                traffic.QuotaUsedPercent);
        }
        catch (OperationCanceledException)
        {
            await LogoutAndDisposeClientAsync(client, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await DisposeClientSafelyAsync(client);

            // Авторизация прошла успешно; ошибка получения трафика — не ошибка пароля.
            _statuses[user.Id] = KerioUserStatus.Error;
            _error = $"{user.Username}: {ex.Message}";
            logger.LogWarning(ex, "Ошибка получения трафика {Username}.", user.Username);
            Publish();
            return false;
        }

        if (traffic.QuotaUsedPercent >= _options.CurrentValue.SwitchThresholdPercent)
        {
            _statuses[user.Id] = KerioUserStatus.LimitReached;
            _error = $"{user.Username}: достигнут лимит трафика ({traffic.QuotaUsedPercent:F2}%).";
            await LogoutAndDisposeClientAsync(client, cancellationToken);
            Publish();
            return false;
        }

        _client = client;
        _currentUser = user;
        _traffic = traffic;
        _updatedAt = DateTimeOffset.Now;
        _error = null;
        _statuses[user.Id] = KerioUserStatus.Active;
        Publish();

        return true;
    }

    private async Task LogoutAndDisposeAsync(CancellationToken cancellationToken)
    {
        var client = _client;

        _client = null;
        _currentUser = null;
        _traffic = null;

        if (client is not null)
            await LogoutAndDisposeClientAsync(client, cancellationToken);
    }

    private async Task LogoutAndDisposeClientAsync(
        IKerioClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.LogoutAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Cleanup не должен блокировать Dispose, в том числе при отмене.
            logger.LogDebug(ex, "Ошибка при выходе из Kerio.");
        }
        finally
        {
            await DisposeClientSafelyAsync(client);
        }
    }

    private async Task DisposeClientSafelyAsync(IKerioClient client)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка освобождения Kerio-клиента.");
        }
    }

    private void InitializeStatuses()
    {
        _statuses.Clear();

        foreach (var user in _users)
        {
            _statuses[user.Id] = user.IsEnabled
                ? KerioUserStatus.Waiting
                : KerioUserStatus.Disabled;
        }
    }

    private void Publish()
    {
        var handler = SnapshotChanged;

        if (handler is null)
            return;

        var snapshot = new MonitoringSnapshot(
            _currentUser,
            _traffic,
            new Dictionary<Guid, KerioUserStatus>(_statuses),
            _error,
            _updatedAt);

        foreach (var @delegate in handler.GetInvocationList())
        {
            var subscriber = (EventHandler<MonitoringSnapshot>)@delegate;
            try
            {
                subscriber(this, snapshot);
            }
            catch (Exception ex)
            {
                // Ошибка UI-подписчика не должна прерывать работу сервиса.
                logger.LogError(ex, "Ошибка обработчика события SnapshotChanged.");
            }
        }
    }

    private static void NormalizePriorities(IList<KerioUser> users)
    {
        for (var i = 0; i < users.Count; i++)
            users[i].Priority = i + 1;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
