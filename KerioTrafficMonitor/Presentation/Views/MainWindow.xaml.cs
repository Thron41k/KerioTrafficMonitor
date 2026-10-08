using KerioTrafficMonitor.Presentation.ViewModels;
using System.Windows;

namespace KerioTrafficMonitor.Presentation.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;
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