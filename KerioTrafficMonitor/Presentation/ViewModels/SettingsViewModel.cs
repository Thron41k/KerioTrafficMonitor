using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KerioTrafficMonitor.Application.Interfaces;
using KerioTrafficMonitor.Application.Options;
using KerioTrafficMonitor.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace KerioTrafficMonitor.Presentation.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IOptionsMonitor<KerioOptions> _kerioOptions;
    private readonly IOptionsMonitor<MonitoringOptions> _monitoringOptions;
    private readonly IUserRotationService _rotationService;

    public event Action<bool>? RequestClose;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _intervalSeconds = "30";

    [ObservableProperty]
    private string _thresholdPercent = "95";

    [ObservableProperty]
    private bool _automaticSwitching;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(
        ISettingsStore settingsStore,
        IOptionsMonitor<KerioOptions> kerioOptions,
        IOptionsMonitor<MonitoringOptions> monitoringOptions,
        IUserRotationService rotationService)
    {
        _settingsStore = settingsStore;
        _kerioOptions = kerioOptions;
        _monitoringOptions = monitoringOptions;
        _rotationService = rotationService;

        LoadSettings();
    }

    private void LoadSettings()
    {
        var kerio = _kerioOptions.CurrentValue;
        var monitoring = _monitoringOptions.CurrentValue;

        ServerUrl = kerio.BaseUrl;
        IntervalSeconds =
            monitoring.IntervalSeconds.ToString(
                CultureInfo.InvariantCulture);

        ThresholdPercent =
            monitoring.SwitchThresholdPercent.ToString(
                "0.##",
                CultureInfo.InvariantCulture);

        AutomaticSwitching = monitoring.AutomaticSwitching;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = null;

        if (!TryValidate(
                out var normalizedUrl,
                out var interval,
                out var threshold))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var kerio = new KerioOptions
            {
                BaseUrl = normalizedUrl
            };

            var monitoring = new MonitoringOptions
            {
                IntervalSeconds = interval,
                SwitchThresholdPercent = threshold,
                AutomaticSwitching = AutomaticSwitching
            };

            // Сохраняем файл и перезагружаем IConfiguration.
            await _settingsStore.SaveAsync(
                kerio,
                monitoring);

            // Перезапускаем мониторинг, чтобы новый интервал
            // применился сразу, а новое подключение использовало
            // обновлённый адрес сервера.
            await _rotationService.StopAsync();

            await _rotationService.StartAsync();

            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage =
                $"Не удалось применить настройки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    private bool TryValidate(
        out string normalizedUrl,
        out int interval,
        out double threshold)
    {
        normalizedUrl = string.Empty;
        interval = 0;
        threshold = 0;

        var url = ServerUrl.Trim();

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            ErrorMessage =
                "Введите корректный HTTP/HTTPS адрес Kerio без логина, " +
                "пароля, query-параметров и fragment.";

            return false;
        }

        normalizedUrl = uri.AbsoluteUri;

        if (!normalizedUrl.EndsWith('/'))
            normalizedUrl += "/";

        if (!int.TryParse(
                IntervalSeconds,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out interval) ||
            interval is < 5 or > 3600)
        {
            ErrorMessage =
                "Интервал должен быть целым числом от 5 до 3600 секунд.";

            return false;
        }

        var normalizedThreshold =
            ThresholdPercent.Trim().Replace(',', '.');

        if (!double.TryParse(
                normalizedThreshold,
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture,
                out threshold) ||
            double.IsNaN(threshold) ||
            double.IsInfinity(threshold) ||
            threshold <= 0 ||
            threshold > 100)
        {
            ErrorMessage =
                "Порог квоты должен быть больше 0 и не превышать 100%.";

            return false;
        }

        return true;
    }
}