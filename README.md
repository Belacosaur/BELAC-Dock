# BELAC Dock

A thin, floating taskbar for Windows 11, with local app shortcuts and LLM usage at the bottom.

- Drag the top handle to move it; resize from the lower corner. Geometry survives restarts.
- Six live colour themes: Mint, Ocean, Violet, Rose, Amber and Slate. Open **Settings → Appearance → Colour theme**. Menus and usage popouts follow the theme; Windows high contrast takes priority.
- Click to launch, focus or minimize an app. Multiple windows open a window picker.
- Drag executable files or Windows shortcuts onto the dock. Advanced right-click actions are under **More**.
- Optional per-app **Dock-only** mode hides running taskbar buttons, with recovery and a configurable compatibility fallback.
- Provider logos and usage rings use the local OpenTokenUsage service at `http://127.0.0.1:6736`. No provider credentials are required by the dock.

## Requirements and running

Windows 11 x64. The portable release includes the .NET 10 runtime: unzip it and run `BELAC.Dock.exe`. The build is unsigned.

First launch offers monitor and application setup. The tray icon and `Ctrl+Alt+Shift+D` bring the dock back. Sign-in startup is opt-in.

Settings, logs and recovery information live in `%LOCALAPPDATA%\BELAC\Dock`. `BELAC_DOCK_HOME` overrides this directory for isolated testing. Do not publish that directory or diagnostic exports: they can contain application paths and window information.

## Build

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run from the repository root:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\test-ui.ps1
.\scripts\publish.ps1
.\scripts\source-release.ps1
```

The publish script creates a self-contained Windows x64 zip in `dist`. The source script exports project sources, tests, docs and release files; local artifacts and configuration are excluded. Nothing is uploaded automatically. Both archives include the project license and third-party notices.

## Dock-only behavior

Mirror is the default. Dock-only is persisted per app and reapplied to new windows after app or dock restarts. The dock journals original state before modifying a window and verifies the running taskbar button using Windows UI Automation. Unsupported or unverifiable windows report an error rather than claiming success.

The optional **Style compatibility** setting can also remove an app from Alt+Tab. Disable it, choose Mirror, pause separation or exit the dock to restore managed windows. Pinned shortcuts remain pinned; unpin them yourself if you want no idle shortcut. Elevated/protected apps and some custom taskbars cannot be managed reliably. Support for every Windows app is not guaranteed.

The dock does not inject into or restart Explorer. An optional read-only Shell identity resolver uses a non-public interface, with documented identity/path fallbacks. See [compatibility](docs/COMPATIBILITY.md), [testing](docs/TESTING.md) and [troubleshooting](docs/TROUBLESHOOTING.md).

## Development and release

See [architecture](ARCHITECTURE.md), [contributing](CONTRIBUTING.md), [security](SECURITY.md), and [usage integration](USAGE_INTEGRATION.md).

MIT licensed; see [LICENSE](LICENSE). Third-party provider marks retain their own license and trademark rights; see [third-party notices](THIRD_PARTY_NOTICES.md).
