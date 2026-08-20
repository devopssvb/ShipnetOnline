using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ShipnetOnline.Auth;
using ShipnetOnline.Config;
using ShipnetOnline.RDP;
using ShipnetOnline.Telemetry;
using System.Windows;

namespace ShipnetOnline;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        // Bootstrap Serilog early so startup errors are captured
        var appInsightsConnStr = config["ApplicationInsights:ConnectionString"] ?? "";
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .WriteTo.Console()
            .WriteTo.File(
                path: Environment.ExpandEnvironmentVariables(
                    config["Logging:LogFilePath"] ?? "%LOCALAPPDATA%\\ShipnetOnline\\logs\\shipnet-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .WriteTo.ApplicationInsights(appInsightsConnStr, TelemetryConverter.Traces)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentUserName()
            .CreateLogger();

        AppHost = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((ctx, services) =>
            {
                services.AddSingleton<IConfiguration>(config);

                // Settings
                services.Configure<ShipnetSettings>(config.GetSection("Shipnet"));
                services.Configure<AzureAdSettings>(config.GetSection("AzureAd"));
                services.Configure<ReconnectSettings>(config.GetSection("Reconnect"));

                // Core services
                services.AddSingleton<CredentialVaultService>();
                services.AddSingleton<MsalAuthService>();
                services.AddSingleton<TelemetryService>();
                services.AddSingleton<NetworkHealthService>();
                services.AddSingleton<RdpSessionManager>();

                // ViewModels
                services.AddTransient<UI.LoginViewModel>();

                // Windows
                services.AddTransient<MainWindow>();
                services.AddTransient<LoginWindow>();
            })
            .Build();

        await AppHost.StartAsync();

        // Initialise telemetry first so all subsequent events are captured
        var telemetry = AppHost.Services.GetRequiredService<TelemetryService>();
        telemetry.TrackEvent("AppStartup");

        var loginWindow = AppHost.Services.GetRequiredService<LoginWindow>();
        loginWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        var telemetry = AppHost.Services.GetService<TelemetryService>();
        telemetry?.TrackEvent("AppShutdown");
        telemetry?.Flush();

        await AppHost.StopAsync();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
