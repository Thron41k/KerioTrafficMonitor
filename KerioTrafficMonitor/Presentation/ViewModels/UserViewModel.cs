using CommunityToolkit.Mvvm.ComponentModel;
using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Presentation.ViewModels;

public partial class UserViewModel : ObservableObject
{
    public UserViewModel(KerioUser model)
    {
        Model = model;
    }

    public KerioUser Model { get; }

    public Guid Id => Model.Id;
    public string Username => Model.Username;

    public int Priority
    {
        get => Model.Priority;
        set
        {
            if (Model.Priority == value) return;
            Model.Priority = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private KerioUserStatus status = KerioUserStatus.Waiting;

    [ObservableProperty]
    private bool isCurrent;

    public string StatusText => Status switch
    {
        KerioUserStatus.Active => "Активен",
        KerioUserStatus.LimitReached => "Лимит",
        KerioUserStatus.AuthenticationFailed => "Ошибка входа",
        KerioUserStatus.Error => "Ошибка",
        KerioUserStatus.Disabled => "Отключен",
        _ => "Ожидает"
    };

    partial void OnStatusChanged(KerioUserStatus value) => OnPropertyChanged(nameof(StatusText));
}
