using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IUserRotationService _rotation;
    private readonly IUserStore _userStore;

    public MainViewModel(
        IUserRotationService rotation,
        IUserStore userStore)
    {
        _rotation = rotation;
        _userStore = userStore;

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

        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        DeleteUserCommand.NotifyCanExecuteChanged();
    }

    public async Task StopAsync()
    {
        await _rotation.StopAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _rotation.RefreshAsync();
    }

    [RelayCommand]
    private async Task SwitchNowAsync()
    {
        await _rotation.SwitchToNextUserAsync();
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        // Здесь оставляем существующую реализацию
        // добавления пользователя из твоего текущего MainViewModel.
    }

    [RelayCommand]
    private async Task DeleteUserAsync()
    {
        if (SelectedUser is null)
            return;

        var result = MessageBox.Show(
            $"Удалить пользователя «{SelectedUser.Username}»?",
            "Удаление пользователя",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var users = Users
            .Where(x => x.Id != SelectedUser.Id)
            .Select(x => x.Model)
            .ToList();

        NormalizePriorities(users);

        await _userStore.SaveAsync(users);

        Users.Remove(SelectedUser);
        RefreshPriorities();

        SelectedUser = Users.FirstOrDefault();

        await _rotation.UpdateUsersAsync(users);
    }

    private void OnSnapshotChanged(
        object? sender,
        MonitoringSnapshot snapshot)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;

        if (dispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
        }
        else
        {
            dispatcher.Invoke(() => ApplySnapshot(snapshot));
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

        var other = Users[index - 1];

        Users.Move(index, index - 1);

        RefreshPriorities();

        await SaveUsersAndApplyAsync();

        SelectedUser = other;
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

        var other = Users[index + 1];

        Users.Move(index, index + 1);

        RefreshPriorities();

        await SaveUsersAndApplyAsync();

        SelectedUser = other;
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

        await _userStore.SaveAsync(users);

        await _rotation.UpdateUsersAsync(users);

        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private void ApplySnapshot(MonitoringSnapshot snapshot)
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
                FormatBytes(snapshot.Traffic.ReceivedBytes);

            Sent =
                FormatBytes(snapshot.Traffic.SentBytes);
        }
        else
        {
            QuotaUsedPercent = 0;
            Received = "—";
            Sent = "—";
        }

        StatusText = GetStatusText(snapshot);
        CurrentUsername =
            snapshot.CurrentUser?.Username ?? "—";
        LastUpdated =
            snapshot.UpdatedAt?.ToLocalTime()
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
            return $"Активен: {snapshot.CurrentUser.Username}";

        return
            $"Активен: {snapshot.CurrentUser.Username} " +
            $"— квота {snapshot.Traffic.QuotaUsedPercent:F1}%";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024d:F1} KB";

        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / 1024d / 1024d:F1} MB";

        return $"{bytes / 1024d / 1024d / 1024d:F2} GB";
    }

    private void RefreshPriorities()
    {
        for (var i = 0; i < Users.Count; i++)
        {
            Users[i].Priority = i + 1;
            Users[i].Model.Priority = i + 1;
        }
    }

    private async Task LoadUsersAsync()
    {
        var users = await _userStore.LoadAsync();

        Users.Clear();

        foreach (var user in users.OrderBy(x => x.Priority))
        {
            Users.Add(new UserViewModel(user));
        }

        RefreshPriorities();

        SelectedUser = Users.FirstOrDefault();

        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        DeleteUserCommand.NotifyCanExecuteChanged();
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

    partial void OnSelectedUserChanged(
        UserViewModel? value)
    {
        DeleteUserCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}