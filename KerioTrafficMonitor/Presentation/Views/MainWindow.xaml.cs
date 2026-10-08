using System.ComponentModel;
using System.Windows;
using KerioTrafficMonitor.Presentation.ViewModels;

namespace KerioTrafficMonitor.Presentation.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.LoadUsersAsyncCommand.ExecuteAsync(null);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.StopAsync();
    }
}
