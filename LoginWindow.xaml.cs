using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShipnetOnline.Auth;
using ShipnetOnline.Config;
using ShipnetOnline.UI;
using System.Windows;

namespace ShipnetOnline;

/// <summary>
/// Code-behind is intentionally thin — all logic lives in LoginViewModel.
/// This class only wires up navigation (Login succeeded -> open MainWindow).
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;
    private readonly ILogger<LoginWindow> _log;
    private readonly ShipnetSettings _shipnetSettings;

    public LoginWindow(LoginViewModel viewModel, ILogger<LoginWindow> log, IOptions<ShipnetSettings> shipnetSettings)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _log = log;
        _shipnetSettings = shipnetSettings.Value;

        DataContext = _viewModel;
        _viewModel.LoginSucceeded += OnLoginSucceeded;

        Loaded += async (_, _) => await _viewModel.InitializeAsync(HostFromUrl(_shipnetSettings.ServerUrl));
    }

    private void OnLoginSucceeded(AuthResult result)
    {
        _log.LogInformation("Navigating to MainWindow for {User}", result.Username);

        var mainWindow = App.AppHost.Services.GetRequiredService<MainWindow>();
        mainWindow.Initialize(result, HostFromUrl(_shipnetSettings.ServerUrl), _shipnetSettings.RdpPort);
        mainWindow.Show();

        Application.Current.MainWindow = mainWindow;
        Close();
    }

    /// <summary>Extracts a bare host name from a configured URL, since the RDP
    /// control and ping diagnostics need a host, not a full https:// URL.</summary>
    private static string HostFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host;
        return url;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.LoginSucceeded -= OnLoginSucceeded;
        base.OnClosed(e);
    }
}
