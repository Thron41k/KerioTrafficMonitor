using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;
using WpfApplication = System.Windows.Application;

namespace KerioTrafficMonitor.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IUserStore _userStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IUserRotationService _rotation;

    public MainViewModel(
        IUserStore userStore,
        ICredentialStore credentialStore,
        IUserRotationService rotation)
    {
        _userStore = userStore;
        _credentialStore = credentialStore;
        _rotation = rotation;

        _rotation.SnapshotChanged +=
            OnSnapshotChanged;
    }

    public ObservableCollection<UserViewModel> Users { get; } = [];

    [ObservableProperty]
    private UserViewModel? _selectedUser;

    [ObservableProperty]
    private string? _currentUsername;

    [ObservableProperty]
    private TrafficInfo? _currentTraffic;

    [ObservableProperty]
    private KerioUserStatus _currentStatus =
        KerioUserStatus.Disabled;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private DateTimeOffset? _updatedAt;

    [ObservableProperty]
    private bool _automaticSwitching = true;

    [ObservableProperty]
    private int _switchThresholdPercent = 95;

    [ObservableProperty]
    private int _intervalSeconds = 30;

    [ObservableProperty]
    private bool _isMonitoring;

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var users =
            (await _userStore.LoadAsync(
                cancellationToken))
            .OrderBy(x => x.Priority)
            .ToList();

        NormalizePriorities(users);

        Users.Clear();

        foreach (var user in users)
        {
            Users.Add(
                new UserViewModel(user));
        }

        await _rotation.UpdateUsersAsync(users, CancellationToken.None);
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsMonitoring)
            return;

        ErrorMessage = null;

        try
        {
            IsMonitoring = true;

            await _rotation.StartAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            IsMonitoring = false;
        }
    }

    [RelayCommand]
    public async Task StopAsync()
    {
        if (!IsMonitoring)
            return;

        try
        {
            await _rotation.StopAsync();

            IsMonitoring = false;
            CurrentUsername = null;
            CurrentTraffic = null;
            CurrentStatus = KerioUserStatus.Disabled;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task MoveUpAsync()
    {
        if (SelectedUser is null)
            return;

        var index = Users.IndexOf(
            SelectedUser);

        if (index <= 0)
            return;

        var items = Users
            .Select(x => x.Model)
            .ToList();

        (
            items[index - 1],
            items[index]
        ) =
        (
            items[index],
            items[index - 1]
        );

        NormalizePriorities(items);

        await _userStore.SaveAsync(items);

        Users.Move(
            index,
            index - 1);

        RefreshPriorities();

        SelectedUser = Users[index - 1];

        await _rotation.UpdateUsersAsync(
            items, CancellationToken.None);
    }

    [RelayCommand]
    private async Task MoveDownAsync()
    {
        if (SelectedUser is null)
            return;

        var index = Users.IndexOf(
            SelectedUser);

        if (index < 0 ||
            index >= Users.Count - 1)
        {
            return;
        }

        var items = Users
            .Select(x => x.Model)
            .ToList();

        (
            items[index],
            items[index + 1]
        ) =
        (
            items[index + 1],
            items[index]
        );

        NormalizePriorities(items);

        await _userStore.SaveAsync(items);

        Users.Move(
            index,
            index + 1);

        RefreshPriorities();

        SelectedUser = Users[index + 1];

        await _rotation.UpdateUsersAsync(
            items);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedUser is null)
            return;

        var selected = SelectedUser;

        var users = Users
            .Where(x => x != selected)
            .Select(x => x.Model)
            .ToList();

        NormalizePriorities(users);

        await _userStore.SaveAsync(users);

        Users.Remove(selected);

        RefreshPriorities();

        SelectedUser = null;

        await _rotation.UpdateUsersAsync(
            users);
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        // Здесь остается существующая логика
        // открытия диалога добавления пользователя.
        await Task.CompletedTask;
    }

    private void OnSnapshotChanged(
        object? sender,
        MonitoringSnapshot snapshot)
    {
        WpfApplication.Current.Dispatcher.Invoke(
            () =>
            {
                CurrentUsername =
                    snapshot.CurrentUser?.Username;

                CurrentTraffic =
                    snapshot.Traffic;

                ErrorMessage =
                    snapshot.Error;

                UpdatedAt =
                    snapshot.UpdatedAt;

                if (snapshot.CurrentUser is not null &&
                    snapshot.UserStatuses.TryGetValue(
                        snapshot.CurrentUser.Id,
                        out var currentStatus))
                {
                    CurrentStatus =
                        currentStatus;
                }
                else
                {
                    CurrentStatus =
                        KerioUserStatus.Disabled;
                }

                foreach (var user in Users)
                {
                    user.IsCurrent =
                        snapshot.CurrentUser?.Id ==
                        user.Id;

                    if (snapshot.UserStatuses.TryGetValue(
                            user.Id,
                            out var status))
                    {
                        user.Status = status;
                    }
                }
            });
    }

    private void RefreshPriorities()
    {
        for (var i = 0; i < Users.Count; i++)
        {
            Users[i].Priority = i + 1;
        }
    }

    private static void NormalizePriorities(
        IList<KerioUser> users)
    {
        for (var i = 0; i < users.Count; i++)
        {
            users[i].Priority = i + 1;
        }
    }
}