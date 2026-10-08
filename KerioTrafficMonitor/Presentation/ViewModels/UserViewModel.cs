using CommunityToolkit.Mvvm.ComponentModel;
using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Presentation.ViewModels;

public partial class UserViewModel : ObservableObject
{
    public UserViewModel(KerioUser model)
    {
        Model = model;

        Priority = model.Priority;
        IsEnabled = model.IsEnabled;
    }

    public KerioUser Model { get; }

    public Guid Id => Model.Id;

    public string Username => Model.Username;

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private KerioUserStatus _status = KerioUserStatus.Waiting;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isEnabled;

    public string StatusText => Status switch
    {
        KerioUserStatus.Disabled =>
            "Отключен",

        KerioUserStatus.Waiting =>
            "Ожидание",

        KerioUserStatus.Active =>
            "Активен",

        KerioUserStatus.LimitReached =>
            "Лимит достигнут",

        KerioUserStatus.AuthenticationFailed =>
            "Ошибка авторизации",

        KerioUserStatus.Error =>
            "Ошибка",

        _ =>
            Status.ToString()
    };

    public void RefreshFromModel()
    {
        Priority = Model.Priority;
        IsEnabled = Model.IsEnabled;
    }

    partial void OnStatusChanged(KerioUserStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
    }
}