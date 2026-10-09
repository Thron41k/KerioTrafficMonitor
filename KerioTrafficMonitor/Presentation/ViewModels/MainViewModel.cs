using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KerioTrafficMonitor.Application.Interfaces;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace KerioTrafficMonitor.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IUserRotationService _rotation;
    private readonly IUserStore _userStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IUpdateService _updateService;
    public MainViewModel(
        IUserRotationService rotation,
        IUserStore userStore, ICredentialStore credentialStore, IUpdateService updateService)
    {
        _rotation = rotation;
        _userStore = userStore;
        _credentialStore = credentialStore;
        _updateService = updateService;
        _rotation.SnapshotChanged += OnSnapshotChanged;
    }

    public ObservableCollection<UserViewModel> Users { get; } = [];

    [ObservableProperty]
    private UserViewModel? selectedUser;

    [ObservableProperty]
    private string statusText = "Ожидание";

    [ObservableProperty]
    private double quotaUsedPercent;

    [ObservableProperty]
    private string received = "—";

    [ObservableProperty]
    private string sent = "—";

    [ObservableProperty]
    private string lastUpdated = "—";

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private string currentUsername = "—";

    [ObservableProperty] private string _trayToolTipText;

    public double QuotaRemainingPercent =>
        Math.Max(0, 100 - QuotaUsedPercent);

    public async Task StartAsync()
    {
        var users = await _userStore.LoadAsync();

        Users.Clear();

        foreach (var user in users.OrderBy(x => x.Priority))
        {
            Users.Add(new UserViewModel(user));
        }

        RefreshPriorities();

        SelectedUser = Users.FirstOrDefault();

        await _rotation.UpdateUsersAsync(users);

        await _rotation.StartAsync();

        NotifyCommandStates();
    }

    public async Task StopAsync()
    {
        await _rotation.StopAsync();

        _rotation.SnapshotChanged -= OnSnapshotChanged;
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var result =
                await _updateService.CheckForUpdatesAsync();

            if (!result.IsInstalled)
            {
                MessageBox.Show(
                    "Автообновление доступно только для установленной " +
                    "версии приложения.\n\n" +
                    "Сначала установите приложение из Velopack Setup.",
                    "Проверка обновлений",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (!result.UpdateAvailable)
            {
                MessageBox.Show(
                    $"Установлена последняя версия: " +
                    $"{result.CurrentVersion}.",
                    "Проверка обновлений",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var notes = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                ? "Описание изменений не указано."
                : result.ReleaseNotes;

            if (notes.Length > 1500)
            {
                notes = notes[..1500] + "...";
            }

            var answer = MessageBox.Show(
                $"Доступна новая версия: {result.AvailableVersion}\n" +
                $"Текущая версия: {result.CurrentVersion}\n\n" +
                $"Изменения:\n{notes}\n\n" +
                "Скачать и установить обновление сейчас?",
                "Доступно обновление",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            await _updateService.DownloadUpdateAsync();

            // Закрываем сессию Kerio и останавливаем мониторинг.
            await _rotation.StopAsync();

            _updateService.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось обновить приложение:\n{ex.Message}",
                "Ошибка обновления",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorText = null;

        try
        {
            await _rotation.RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SwitchNowAsync()
    {
        ErrorText = null;

        try
        {
            await _rotation.SwitchToNextUserAsync();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        var dialog = new Views.UserDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        var username = dialog.Username;
        var password = dialog.Password;

        if (Users.Any(x =>
                string.Equals(
                    x.Username,
                    username,
                    StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                $"Пользователь «{username}» уже добавлен.",
                "Пользователь существует",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var user = new KerioUser
        {
            Id = Guid.NewGuid(),
            Username = username,
            Priority = Users.Count + 1,
            IsEnabled = true
        };

        var userViewModel = new UserViewModel(user);

        try
        {
            await _credentialStore.SavePasswordAsync(
                user.Id,
                password);

            Users.Add(userViewModel);

            RefreshPriorities();

            await SaveUsersAndApplyAsync();

            SelectedUser = userViewModel;
        }
        catch (Exception ex)
        {
            Users.Remove(userViewModel);

            RefreshPriorities();

            await _credentialStore.DeletePasswordAsync(
                user.Id);

            MessageBox.Show(
                $"Не удалось добавить пользователя:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteUserAsync()
    {
        if (SelectedUser is null)
            return;

        var userToDelete = SelectedUser;

        var result = MessageBox.Show(
            $"Удалить пользователя «{userToDelete.Username}»?",
            "Удаление пользователя",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var users = Users
            .Where(x => x.Id != userToDelete.Id)
            .Select(x => x.Model)
            .ToList();

        NormalizePriorities(users);

        try
        {
            await _userStore.SaveAsync(users);
            await _credentialStore.DeletePasswordAsync(
                userToDelete.Id);
            Users.Remove(userToDelete);

            RefreshPriorities();

            SelectedUser = Users.FirstOrDefault();

            await _rotation.UpdateUsersAsync(users);

            NotifyCommandStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось удалить пользователя:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private async Task MoveUpAsync()
    {
        if (SelectedUser is null)
            return;

        var index = Users.IndexOf(SelectedUser);

        if (index <= 0)
            return;

        var selected = SelectedUser;

        Users.Move(index, index - 1);

        RefreshPriorities();

        try
        {
            await SaveUsersAndApplyAsync();

            SelectedUser = selected;
        }
        catch (Exception ex)
        {
            // Возвращаем пользователя обратно,
            // если сохранение не удалось.

            Users.Move(index - 1, index);

            RefreshPriorities();

            MessageBox.Show(
                $"Не удалось изменить приоритет:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool CanMoveUp()
    {
        return SelectedUser is not null
               && Users.IndexOf(SelectedUser) > 0;
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private async Task MoveDownAsync()
    {
        if (SelectedUser is null)
            return;

        var index = Users.IndexOf(SelectedUser);

        if (index < 0 || index >= Users.Count - 1)
            return;

        var selected = SelectedUser;

        Users.Move(index, index + 1);

        RefreshPriorities();

        try
        {
            await SaveUsersAndApplyAsync();

            SelectedUser = selected;
        }
        catch (Exception ex)
        {
            // Возвращаем пользователя обратно,
            // если сохранение не удалось.

            Users.Move(index + 1, index);

            RefreshPriorities();

            MessageBox.Show(
                $"Не удалось изменить приоритет:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool CanMoveDown()
    {
        return SelectedUser is not null
               && Users.IndexOf(SelectedUser) >= 0
               && Users.IndexOf(SelectedUser) < Users.Count - 1;
    }

    private async Task SaveUsersAndApplyAsync()
    {
        var users = Users
            .Select(x => x.Model)
            .ToList();

        NormalizePriorities(users);

        await _userStore.SaveAsync(users);

        await _rotation.UpdateUsersAsync(users);

        NotifyCommandStates();
    }

    private void OnSnapshotChanged(
        object? sender,
        MonitoringSnapshot snapshot)
    {
        var dispatcher =
            System.Windows.Application.Current.Dispatcher;

        if (dispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
        }
        else
        {
            dispatcher.Invoke(
                () => ApplySnapshot(snapshot));
        }
    }

    private void ApplySnapshot(
        MonitoringSnapshot snapshot)
    {
        foreach (var user in Users)
        {
            if (snapshot.UserStatuses.TryGetValue(
                    user.Id,
                    out var status))
            {
                user.Status = status;
            }

            user.IsCurrent =
                snapshot.CurrentUser?.Id == user.Id;
        }

        if (snapshot.Traffic is not null)
        {
            QuotaUsedPercent =
                snapshot.Traffic.QuotaUsedPercent;

            Received =
                FormatBytes(
                    snapshot.Traffic.ReceivedBytes);

            Sent =
                FormatBytes(
                    snapshot.Traffic.SentBytes);
        }
        else
        {
            QuotaUsedPercent = 0;
            Received = "—";
            Sent = "—";
        }

        OnPropertyChanged(
            nameof(QuotaRemainingPercent));

        StatusText = GetStatusText(snapshot);
        TrayToolTipText = GetTrayToolTipText(snapshot);
        CurrentUsername =
            snapshot.CurrentUser?.Username ?? "—";

        LastUpdated =
            snapshot.UpdatedAt?
                .ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm:ss")
            ?? "—";

        ErrorText = snapshot.Error;
    }

    private static string GetStatusText(
        MonitoringSnapshot snapshot)
    {
        if (snapshot.CurrentUser is null)
            return "Нет активного пользователя";

        if (snapshot.Traffic is null)
        {
            return
                $"Активен: {snapshot.CurrentUser.Username}";
        }

        return
            $"Активен: {snapshot.CurrentUser.Username} " +
            $"— квота {snapshot.Traffic.QuotaUsedPercent:F1}%";
    }

    private string GetTrayToolTipText(MonitoringSnapshot snapshot)
    {
        if (snapshot.CurrentUser is null)
        {
            return "Kerio Traffic Monitor\n" +
                   "Активная учетная запись отсутствует.";
        }

        if (snapshot.Traffic is null)
        {
            return $"Пользователь: {snapshot.CurrentUser.Username}\n" +
                   $"Получение данных...";
        }

        return $"Пользователь: {snapshot.CurrentUser.Username}\n" +
               $"Получено: {FormatBytes(snapshot.Traffic.ReceivedBytes)}\n" +
               $"Отправлено: {FormatBytes(snapshot.Traffic.SentBytes)}\n" +
               $"Всего: {FormatBytes(snapshot.Traffic.TotalBytes)}\n" +
               $"Квота: {snapshot.Traffic.QuotaUsedPercent:F1}%\n" +
               $"Остаток: {snapshot.Traffic.QuotaRemainingPercent:F1}%\n" +
               $"Обновлено: {snapshot.UpdatedAt:HH:mm:ss}";
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;

        return bytes switch
        {
            >= (long)gb => $"{bytes / gb:F2} ГБ",
            >= (long)mb => $"{bytes / mb:F2} МБ",
            >= (long)kb => $"{bytes / kb:F2} КБ",
            _ => $"{bytes:N0} Б"
        };
    }

    private void RefreshPriorities()
    {
        for (var i = 0; i < Users.Count; i++)
        {
            var priority = i + 1;

            Users[i].Priority = priority;
            Users[i].Model.Priority = priority;
        }
    }

    private static void NormalizePriorities(
        IList<KerioUser> users)
    {
        var ordered = users
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Username)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Priority = i + 1;
        }
    }

    private void NotifyCommandStates()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        DeleteUserCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedUserChanged(
        UserViewModel? value)
    {
        NotifyCommandStates();
    }

    partial void OnQuotaUsedPercentChanged(
        double value)
    {
        OnPropertyChanged(
            nameof(QuotaRemainingPercent));
    }
}