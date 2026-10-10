using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;

namespace KerioTrafficMonitor.Presentation.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isApplicationClosing;
    private readonly IServiceScopeFactory _scopeFactory;
    public MainWindow(MainViewModel viewModel,
        IUserRotationService rotationService, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();
        _scopeFactory = scopeFactory;
        _viewModel = viewModel;
        DataContext = _viewModel;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_StateChanged(
        object? sender,
        EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
            return;

        // Убираем окно с панели задач.
        Hide();

        // Оставляем приложение работать.
        // UserRotationService продолжает мониторинг.
    }

    private void TrayCheckUpdates_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.CheckForUpdatesCommand.CanExecute(null))
        {
            _viewModel.CheckForUpdatesCommand.Execute(null);
        }
    }

    private void TrayOpen_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        Show();

        WindowState = WindowState.Normal;

        Activate();

        // Иногда Windows не отдаёт фокус сразу.
        if (!IsActive)
        {
            Topmost = true;
            Topmost = false;
            Activate();
        }
    }

    private async void MainWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_isApplicationClosing)
            return;

        // Первый запрос на закрытие откладываем до окончания StopAsync.
        e.Cancel = true;
        _isApplicationClosing = true;

        try
        {
            await _viewModel.StopAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Ошибка остановки приложения: {ex}");
        }
        finally
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    private void TrayExit_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void TrayIcon_TrayMouseDoubleClick(
        object sender,
        RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        TrayIcon.Dispose();
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        await _viewModel.StartAsync();
    }

    private void Settings_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenSettingsWindow();
    }

    private void TraySettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowMainWindow();
        OpenSettingsWindow();
    }

    private void OpenSettingsWindow()
    {
        using var scope = _scopeFactory.CreateScope();

        var window =
            scope.ServiceProvider.GetRequiredService<SettingsWindow>();

        window.Owner = this;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        window.ShowDialog();
    }
}