using AxMSTSCLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShipnetOnline.Auth;
using ShipnetOnline.RDP;
using ShipnetOnline.Telemetry;
using System.Windows;
using System.Windows.Media;

namespace ShipnetOnline;

public partial class MainWindow : Window
{
    private readonly RdpSessionManager _rdpManager;
    private readonly TelemetryService _telemetry;
    private readonly ILogger<MainWindow> _log;

    private AxMsRdpClient9NotSafeForScripting? _rdpControl;
    private RdpConnectionInfo? _pendingConnection;

    public MainWindow(RdpSessionManager rdpManager, TelemetryService telemetry, ILogger<MainWindow> log)
    {
        InitializeComponent();

        _rdpManager = rdpManager;
        _telemetry = telemetry;
        _log = log;

        _rdpManager.StateChanged += OnStateChanged;
        _rdpManager.HealthScoreChanged += OnHealthScoreChanged;
        _rdpManager.StatusMessage += OnStatusMessage;
    }

    /// <summary>
    /// Call after construction, before Show(), to supply the authenticated
    /// user and target host. Actually starts the ActiveX control + connection
    /// once the window is loaded (the control must be in the visual tree first).
    /// </summary>
    public void Initialize(AuthResult authResult, string host, int port)
    {
        // NOTE: Azure AD sign-in (MsalAuthService) authenticates the *person* to
        // Shipnet's identity layer, but the RDP protocol itself authenticates to
        // the *remote Windows host*, which is a separate credential. Unless the
        // target machines are Entra-joined with Windows 365 / AVD-style
        // token-based RDP auth wired up server-side, the native RDP control below
        // will still show its own Windows credential prompt on connect — that is
        // expected, not a bug in this wrapper.
        _pendingConnection = new RdpConnectionInfo
        {
            Host = host,
            Username = authResult.Username,
            Port = port
        };

        Title = $"Shipnet Online — {authResult.DisplayName}";
        _telemetry.TrackPageView("MainWindow");
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_rdpControl is not null || _pendingConnection is null) return;

        CreateRdpControl();
        await ConnectAsync();
    }

    private void CreateRdpControl()
    {
        _rdpControl = new AxMsRdpClient9NotSafeForScripting();
        // Hosting an ActiveX control inside WindowsFormsHost requires adding it
        // to a WinForms Control collection first so the control's site is created.
        var hostPanel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
        hostPanel.Controls.Add(_rdpControl);
        RdpHost.Child = hostPanel;

        _rdpManager.AttachControl(_rdpControl);
    }

    private async Task ConnectAsync()
    {
        if (_pendingConnection is null) return;
        try
        {
            await _rdpManager.ConnectAsync(_pendingConnection);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Initial RDP connection failed");
            _telemetry.TrackException(ex);
            Dispatcher.Invoke(() =>
            {
                PlaceholderText.Text = "Unable to connect. Check network and try again.";
                PlaceholderText.Visibility = Visibility.Visible;
            });
        }
    }

    // ── RdpSessionManager event handlers (fire on background/COM threads —
    //    always marshal to the UI thread via Dispatcher) ─────────────────────

    private void OnStateChanged(ConnectionState state)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = state.ToString();
            PlaceholderText.Visibility = state == ConnectionState.Connected
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (state == ConnectionState.Failed)
                PlaceholderText.Text = "Connection failed after multiple attempts.";
        });
    }

    private void OnHealthScoreChanged(int score)
    {
        Dispatcher.Invoke(() =>
        {
            HealthScoreText.Text = $"Health: {score}/100";
            HealthScoreText.Foreground = score switch
            {
                >= 70 => (Brush)FindResource("StatusGoodBrush"),
                >= 40 => (Brush)FindResource("StatusWarnBrush"),
                _ => (Brush)FindResource("StatusBadBrush")
            };
        });
    }

    private void OnStatusMessage(string message)
    {
        Dispatcher.Invoke(() => StatusText.Text = message);
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _rdpManager.Disconnect();
        Close();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _rdpManager.Disconnect();
        _rdpManager.StateChanged -= OnStateChanged;
        _rdpManager.HealthScoreChanged -= OnHealthScoreChanged;
        _rdpManager.StatusMessage -= OnStatusMessage;
    }
}
