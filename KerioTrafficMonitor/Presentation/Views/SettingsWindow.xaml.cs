using System.Windows;
using KerioTrafficMonitor.Presentation.ViewModels;

namespace KerioTrafficMonitor.Presentation.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.RequestClose += OnRequestClose;

        Closed += (_, _) =>
        {
            _viewModel.RequestClose -= OnRequestClose;
        };
    }

    private void OnRequestClose(bool result)
    {
        DialogResult = result;
    }
}