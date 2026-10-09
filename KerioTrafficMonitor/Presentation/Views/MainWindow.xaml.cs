using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Presentation.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace KerioTrafficMonitor.Presentation.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isApplicationClosing;
    private readonly IUserRotationService _rotationService;
    public MainWindow(MainViewModel viewModel,
        IUserRotationService rotationService)
    {
        InitializeComponent();
        _rotationService = rotationService;
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

        /*
         * Closing у WPF синхронный, а StopAsync асинхронный.
         * Поэтому первый Closing отменяем, корректно останавливаем
         * мониторинг, затем повторно вызываем Shutdown().
         */
        e.Cancel = true;

        _isApplicationClosing = true;

        try
        {
            await _rotationService.StopAsync(
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Ошибка остановки мониторинга: {ex}");
        }
        finally
        {
            TrayIcon.Dispose();

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

    protected override async void OnClosed(EventArgs e)
    {
        await _viewModel.StopAsync();

        base.OnClosed(e);
    }
}