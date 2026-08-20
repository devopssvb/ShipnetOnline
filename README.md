# ShipnetOnline — Enhanced RDP Wrapper
## Next-Generation Build | March 2026

---

## ⚠️ What was fixed / added in this pass

The service layer (Auth, RDP, Telemetry, Config) was already solid, but the
app couldn't actually launch or show a window. This pass added the missing
UI layer and fixed a few bugs so the project builds end-to-end in Visual
Studio on Windows:

| Issue | Fix |
|---|---|
| No `App.xaml` (only `.xaml.cs` existed) | Added `App.xaml` with shared styles/brushes, no `StartupUri` (startup is handled in code via DI) |
| No `LoginWindow` at all | Added `LoginWindow.xaml` + code-behind, bound to the existing `LoginViewModel` |
| No `MainWindow` at all | Added `MainWindow.xaml` + code-behind — hosts the RDP ActiveX control via `WindowsFormsHost`, shows connection status + health score, wired to `RdpSessionManager` events |
| `LoginViewModel` was never registered in DI | Added `services.AddTransient<UI.LoginViewModel>()` |
| `AxInterop.MSTSCLib` / `Interop.MSTSCLib` referenced as **NuGet packages** that don't exist | Removed the bogus `PackageReference`s; csproj now documents the two real ways to get these (VS "Add COM Reference" or `aximp.exe`) |
| `ApplicationIcon` pointed at a missing `Assets\shipnet.ico` | Added a placeholder icon; the csproj reference is now conditional so its absence won't break a build |
| `MsalAuthService` called `WithRedirectUri(...)` then `WithDefaultRedirectUri()` immediately after (the second call silently wins) | Now picks one based on whether you've set a custom `RedirectUri` in config |
| `appsettings.json` wasn't marked to copy to the output folder | Added `CopyToOutputDirectory` — without this, startup throws `FileNotFoundException` since `AddJsonFile(..., optional: false)` |
| Stray literal folder named `{Core,Auth,Telemetry,RDP,UI,Config}` | Removed (leftover from an unexpanded `mkdir {a,b,c}`) |

**Important architectural note added in `MainWindow.xaml.cs`:** Azure AD
sign-in authenticates the *person* to your identity layer, but the RDP
protocol authenticates to the *remote Windows host* — a separate credential.
Unless your target machines are Entra-joined with Windows 365/AVD-style
token-based RDP auth wired up server-side, the RDP ActiveX control will still
show its own native Windows credential prompt on connect. That's expected
behavior, not a bug in this wrapper — plan for it in your rollout.

**Known constraint:** this is a Windows-only desktop app (WPF + DPAPI + the
RDP ActiveX control). It can only be built and run on Windows with Visual
Studio — there is no cross-platform path here.

---

## Project Structure

```
ShipnetOnline/
├── App.xaml / App.xaml.cs       # DI host, Serilog bootstrap, app resources
├── LoginWindow.xaml(.cs)        # Sign-in screen, bound to LoginViewModel
├── MainWindow.xaml(.cs)         # Hosts the RDP ActiveX control + status bar
├── appsettings.json             # All config (no secrets here)
├── ShipnetOnline.csproj         # NuGet references
├── Assets/
│   └── shipnet.ico              # Placeholder — swap for your real icon
│
├── Config/
│   └── Settings.cs              # Typed config POCOs
│
├── Auth/
│   ├── MsalAuthService.cs       # Azure AD SSO via MSAL.NET
│   └── CredentialVaultService.cs # DPAPI encrypted credential storage
│
├── RDP/
│   ├── RdpSessionManager.cs     # Auto-reconnect, heartbeat, health scoring
│   └── NetworkHealthService.cs  # Pre-connection diagnostics
│
├── Telemetry/
│   └── TelemetryService.cs      # Azure Application Insights wrapper
│
└── UI/
    └── LoginViewModel.cs        # MVVM login with SSO + network check
```

---

## Quick Start

### 0. Get the RDP COM interop assemblies (do this first)
`AxInterop.MSTSCLib` and `Interop.MSTSCLib` are **not** NuGet packages — they
wrap a native Windows COM control (`mstscax.dll`). In Visual Studio:
1. Right-click the project → **Add → COM Reference…**
2. Check **"Microsoft Terminal Services Client Control 2.0"** → OK

VS generates the interop DLLs automatically; nothing to add to the `.csproj`.
(See the comment block at the bottom of `ShipnetOnline.csproj` for the manual
`aximp.exe` alternative if you need a CI/CLI build.)

### 1. Prerequisites
- Visual Studio 2022 (v17.8+), with the ".NET Desktop Development" workload
- .NET 8 SDK
- Windows 10/11 (DPAPI and the RDP ActiveX control are Windows-only)
- Azure subscription (for AD + App Insights)

### 2. Azure AD App Registration
1. Go to **Azure Portal → Entra ID → App Registrations → New**
2. Set Redirect URI to `http://localhost` (Public client / native)
3. Under **API Permissions**, add your Shipnet API scope
4. Copy **Tenant ID** and **Client (App) ID** into `appsettings.json`

```json
"AzureAd": {
    "TenantId":  "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "ClientId":  "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "Authority": "https://login.microsoftonline.com/YOUR_TENANT_ID",
    "Scopes":    [ "openid", "profile", "offline_access" ]
}
```

### 3. Application Insights
1. Create an **Application Insights** resource in Azure
2. Copy the **Connection String** into `appsettings.json`

```json
"ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=..."
}
```

### 4. Build & Run
```bash
dotnet restore
dotnet build
dotnet run
```
(From Visual Studio: just hit F5 after step 0 — that's the easier path since
`dotnet restore`/CLI builds can't add the COM reference for you.)

---

## Module Summary

### 🔐 Auth — MsalAuthService
- Silent SSO with cached tokens (users rarely see a login prompt)
- Falls back to system browser interactive login
- Token cache encrypted with DPAPI via MSAL Extensions
- Sign-out clears both MSAL cache and credential vault

### 🔒 Auth — CredentialVaultService
- **Replaces** the plain-text `Username`/`Password`/`PassCode` in the old `.exe.config`
- Encrypted with Windows DPAPI (CurrentUser scope)
- Per-machine entropy — vault is non-portable between machines
- Zero-overwrites file before deletion to prevent disk recovery

### 📡 RDP — RdpSessionManager
- **Auto-reconnect** with exponential back-off: 2s → 4s → 8s → 30s → 60s
- **Network pre-check** before every connection attempt
- **Heartbeat** every 30s — proactively renegotiates before silent drops
- **Dynamic quality** — reduces colour depth and disables animations on poor links
- **Health score** (0–100) exposed for system tray / status bar display
- **Session persistence** via NLA + reconnect cookies

### 🖥️ UI — LoginWindow / MainWindow
- `LoginWindow` drives `LoginViewModel` (MVVM, no logic in code-behind)
- On successful login, `MainWindow` is resolved from the DI container,
  initialized with the target host + authenticated user, and shown
- `MainWindow` creates the `AxMsRdpClient9NotSafeForScripting` control at
  runtime, hosts it via `WindowsFormsHost`, and attaches it to
  `RdpSessionManager` so auto-reconnect/health-scoring work automatically
- A bottom status bar shows live connection state, health score, and a
  Disconnect button

### 📊 Telemetry — TelemetryService
Tracks automatically:
| Event | When |
|---|---|
| `AppStartup` / `AppShutdown` | Process lifecycle |
| `LoginAttempt` / `LoginSuccess` | Auth flow |
| `RdpConnected` / `RdpDisconnected` | Session lifecycle |
| `RdpReconnectFailed` / `RdpReconnectGaveUp` | Stability issues |
| `RdpHealthScore` (metric) | Every heartbeat |
| `NetworkHealthScore` (metric) | Pre-connection check |
| Exceptions | Unhandled errors |

---

## Security Notes

| Old Behaviour | New Behaviour |
|---|---|
| Username in plain XML | DPAPI encrypted vault |
| Password in plain XML | Never stored — SSO token only |
| PassCode in plain XML | Replaced by MFA via Azure AD |
| RdpInfo string | Host stored only (no credentials) |
| SaveSetting=True (plain text) | RememberMe stores only token hint |

---

## Next Steps (Phase 2)

- [ ] Add system tray icon showing live `HealthScore`
- [ ] Add TOTP MFA via `MsalAuthService` additional claims request
- [ ] Add Azure Front Door failover URL to `ReconnectSettings`
- [ ] Set up Azure Monitor alert when `RdpReconnectGaveUp` fires
- [ ] Decide how RDP-host authentication maps to your Azure AD identity
      (see the note in `MainWindow.xaml.cs` — native RDP creds vs.
      Entra-joined token auth are two very different rollout paths)
- [ ] Replace the placeholder `Assets/shipnet.ico` with your real branding

