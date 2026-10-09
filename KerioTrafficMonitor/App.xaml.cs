using KerioTrafficMonitor.Application.Interfaces;
using KerioTrafficMonitor.Application.Options;
using KerioTrafficMonitor.Application.Services;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Infrastructure.Kerio;
using KerioTrafficMonitor.Infrastructure.Persistence;
using KerioTrafficMonitor.Infrastructure.Updates;
using KerioTrafficMonitor.Presentation.ViewModels;
using KerioTrafficMonitor.Presentation.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Windows;
using Velopack;

namespace KerioTrafficMonitor;

public partial class App : System.Windows.Application
{
    private IHost? _host;


    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(
            UserSettingsPaths.DirectoryPath);
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.SetBasePath(AppContext.BaseDirectory);

                configuration.AddJsonFile(
                    "appsettings.json",
                    optional: false,
                    reloadOnChange: true);

                configuration.AddJsonFile(
                    UserSettingsPaths.FilePath,
                    optional: true,
                    reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<KerioOptions>(
                    context.Configuration.GetSection("Kerio"));

                services.Configure<MonitoringOptions>(
                    context.Configuration.GetSection("Monitoring"));

                services.AddSingleton<IUserStore, JsonUserStore>();
                services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
                services.AddSingleton<IKerioClientFactory, KerioClientFactory>();
                services.AddSingleton<IUserRotationService, UserRotationService>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsWindow>();
                services.AddTransient<MainViewModel>();
                services.AddTransient<UserDialog>();
                services.AddTransient<MainWindow>();
                services.AddSingleton<ISettingsStore, JsonSettingsStore>();
                services.AddSingleton<IUpdateService, VelopackUpdateService>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow =
            _host.Services.GetRequiredService<MainWindow>();

        MainWindow = mainWindow;

        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}