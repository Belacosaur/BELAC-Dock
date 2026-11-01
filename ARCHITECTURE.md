# Architecture

BELAC Dock is a Windows x64 WPF application targeting .NET 10.

- **Core**: settings, themes, app matching, ordering, recovery identities and usage normalization.
- **Infrastructure**: atomic JSON settings/recovery storage, bounded local HTTP usage polling and rolling logs.
- **Windows**: window tracking, shortcuts, icons, monitors, hotkeys, taskbar inspection and reversible mutations.
- **App**: WPF views and view models, tray integration and lifecycle orchestration.
- **Integration / SampleApp**: optional cooperative app integration.

## Presentation

The default dock is a thin left-side floating window. Geometry is stored separately in `floating-layout.json`. It does not reserve desktop work area. App rows retain visual instances during tracker updates so hover and context menus remain stable. LLM rings stay at the bottom; details use a compact popout.

`DockThemes` defines six palettes. The selected ID is normalized and saved in settings. `Palette` updates shared bound brushes so custom ring drawings and existing controls update together. High contrast temporarily overrides the theme; reduced motion disables animation.

## Taskbar lifecycle

The primary-instance check runs before AppHost starts tracking. Window identity includes HWND, PID and process start time. Reconciliation handles disabled/removed apps, Mirror, paused separation, new windows and stale records.

Before a mutation, the recovery journal is persisted. Normal separation uses `ITaskbarList.DeleteTab`. UI Automation verifies taskbar button state using application identity, with conservative path fallbacks. Grouped windows are applied in batches so verification does not invalidate sibling observations. A configurable tool-window style fallback may hide Alt+Tab entries.

The optional Shell resolver uses a non-public read-only COM interface. Failures fall back to property stores, process identities and resolved shortcuts. It is a compatibility risk across Windows versions, not an injection mechanism. Unknown/localized taskbar representations remain conservative.

Clean shutdown restores managed windows. A crash leaves the journal intact so the next primary instance can reconcile persisted preferences and original styles. Never delete a live recovery journal during development.

## Local data and network

Data uses `%LOCALAPPDATA%\BELAC\Dock`, or an explicit `BELAC_DOCK_HOME` for tests. The usage client targets only `http://127.0.0.1:6736`, bypasses proxies, rejects redirects, limits responses to 1 MB while streaming, and times out the whole request. It does not store provider keys or send telemetry. Diagnostic exports are local and must be reviewed before sharing.
