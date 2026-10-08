using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;
using KerioTrafficMonitor.Presentation.Views;

namespace KerioTrafficMonitor.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IUserStore _userStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IUserRotationService _rotation;
    private readonly SemaphoreSlim _uiGate = new(1, 1);

    public ObservableCollection<UserViewModel> Users { get; } = [];

    [ObservableProperty] private string currentUsername = "—";
    [ObservableProperty] private double quotaUsedPercent;
    [ObservableProperty] private string received = "—";
    [ObservableProperty] private string sent = "—";
    [ObservableProperty] private string total = "—";
    [ObservableProperty] private string remaining = "—";
    [ObservableProperty] private string statusText = "Остановлено";
    [ObservableProperty] private string lastUpdated = "—";
    [ObservableProperty] private string errorText = string.Empty;

    public MainViewModel(
        IUserStore userStore,
        ICredentialStore credentialStore,
        IUserRotationService rotation)
    {
        _userStore = userStore;
        _credentialStore = credentialStore;
        _rotation = rotation;
        _rotation.SnapshotChanged += OnSnapshotChanged;

        LoadUsersAsyncCommand = new AsyncRelayCommand(InitializeAsync);
        AddUserCommand = new AsyncRelayCommand(AddUserAsync);
        DeleteUserCommand = new AsyncRelayCommand(DeleteSelectedUserAsync, () => SelectedUser is not null);
        MoveUpCommand = new AsyncRelayCommand(MoveUpAsync, () => SelectedUser is not null && Users.IndexOf(SelectedUser) > 0);
        MoveDownCommand = new AsyncRelayCommand(MoveDownAsync, () => SelectedUser is not null && Users.IndexOf(SelectedUser) >= 0 && Users.IndexOf(SelectedUser) < Users.Count - 1);
        SwitchNowCommand = new AsyncRelayCommand(() => _rotation.SwitchToNextUserAsync());
        RefreshCommand = new AsyncRelayCommand(() => _rotation.RefreshAsync());
    }

    public IAsyncRelayCommand LoadUsersAsyncCommand { get; }
    public IAsyncRelayCommand AddUserCommand { get; }
    public IAsyncRelayCommand DeleteUserCommand { get; }
    public IAsyncRelayCommand MoveUpCommand { get; }
    public IAsyncRelayCommand MoveDownCommand { get; }
    public IAsyncRelayCommand SwitchNowCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    public Task StopAsync() => _rotation.StopAsync();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    private UserViewModel? selectedUser;

    private async Task InitializeAsync()
    {
        var users = await _userStore.LoadAsync();
        ReplaceUsers(users);
        await _rotation.UpdateUsersAsync(users);
        await _rotation.StartAsync();
    }

    private async Task AddUserAsync()
    {
        var dialog = new UserDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        var users = Users.Select(x => x.Model).ToList();
        var user = new KerioUser
        {
            Username = dialog.Username,
            Priority = users.Count + 1,
            IsEnabled = true
        };

        users.Add(user);
        await _credentialStore.SetPasswordAsync(user.Id, dialog.Password);
        await SaveAndApplyAsync(users);
        SelectedUser = Users.LastOrDefault();
    }

    private async Task DeleteSelectedUserAsync()
    {
        if (SelectedUser is null)
            return;

        var selected = SelectedUser;
        var result = MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            $"Удалить учетную запись «{selected.Username}»?",
            "Удаление",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        var users = Users.Select(x => x.Model).Where(x => x.Id != selected.Id).ToList();
        await _credentialStore.DeletePasswordAsync(selected.Id);
        await SaveAndApplyAsync(users);
        SelectedUser = Users.FirstOrDefault();
    }

    private async Task MoveUpAsync()
    {
        if (SelectedUser is null) return;
        var index = Users.IndexOf(SelectedUser);
        if (index <= 0) return;

        var list = Users.Select(x => x.Model).ToList();
        (list[index - 1], list[index]) = (list[index], list[index - 1]);
        NormalizePriorities(list);
        await SaveAndApplyAsync(list);
        SelectedUser = Users[index - 1];
    }

    private async Task MoveDownAsync()
    {
        if (SelectedUser is null) return;
        var index = Users.IndexOf(SelectedUser);
        if (index < 0 || index >= Users.Count - 1) return;

        var list = Users.Select(x => x.Model).ToList();
        (list[index], list[index + 1]) = (list[index + 1], list[index]);
        NormalizePriorities(list);
        await SaveAndApplyAsync(list);
        SelectedUser = Users[index + 1];
    }

    private async Task SaveAndApplyAsync(List<KerioUser> users)
    {
        NormalizePriorities(users);
        await _userStore.SaveAsync(users);
        ReplaceUsers(users);
        await _rotation.UpdateUsersAsync(users);
    }

    private void ReplaceUsers(IEnumerable<KerioUser> users)
    {
        Users.Clear();
        foreach (var user in users.OrderBy(x => x.Priority))
            Users.Add(new UserViewModel(user));

        SelectedUser = Users.FirstOrDefault();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        DeleteUserCommand.NotifyCanExecuteChanged();
    }

    private static void NormalizePriorities(IList<KerioUser> users)
    {
        for (var i = 0; i < users.Count; i++)
            users[i].Priority = i + 1;
    }

    private void OnSnapshotChanged(object? sender, MonitoringSnapshot snapshot)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentUsername = snapshot.CurrentUser?.Username ?? "—";
            QuotaUsedPercent = snapshot.Traffic?.QuotaUsedPercent ?? 0;
            Received = FormatBytes(snapshot.Traffic?.ReceivedBytes);
            Sent = FormatBytes(snapshot.Traffic?.SentBytes);
            Total = FormatBytes(snapshot.Traffic?.TotalBytes);
            Remaining = snapshot.Traffic is null ? "—" : $"{snapshot.Traffic.QuotaRemainingPercent:F2}%";
            StatusText = snapshot.Status switch
            {
                KerioUserStatus.Active => "Авторизован",
                KerioUserStatus.LimitReached => "Лимит достигнут",
                KerioUserStatus.AuthenticationFailed => "Ошибка авторизации",
                _ => snapshot.CurrentUser is null ? "Остановлено" : "Ошибка"
            };
            LastUpdated = snapshot.UpdatedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
            ErrorText = snapshot.Error ?? string.Empty;

            foreach (var user in Users)
            {
                user.IsCurrent = snapshot.CurrentUser?.Id == user.Id;
                if (user.IsCurrent && snapshot.Status is not null)
                    user.Status = snapshot.Status.Value;
                else if (!user.IsCurrent)
                    user.Status = KerioUserStatus.Waiting;
            }
        });
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null) return "—";
        const double K = 1024;
        var value = bytes.Value;
        return value switch
        {
            < 1024 => $"{value} B",
            < 1024 * 1024 => $"{value / K:F2} KB",
            < 1024L * 1024 * 1024 => $"{value / K / K:F2} MB",
            _ => $"{value / K / K / K:F2} GB"
        };
    }
}
