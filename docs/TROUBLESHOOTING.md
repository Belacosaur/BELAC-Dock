# Troubleshooting

- **Dock hidden:** use the tray icon or Ctrl+Alt+Shift+D. Fullscreen apps can temporarily hide it; change this in Settings → Position.
- **Colour unchanged:** Windows high contrast takes priority; switching it off restores the theme.
- **Taskbar icon remains:** distinguish a running button from a pinned shortcut. The dock never unpins shortcuts. Check the app tooltip and Settings → Taskbar for verification errors.
- **Resistant app:** enable Style compatibility only if removal from Alt+Tab is acceptable. Protected apps can remain unsupported.
- **Restore buttons:** choose Mirror, pause separation, Restore all, or exit from the tray. After a crash restart with the same data directory. Do not delete the recovery journal while managed windows are alive.
- **Usage unavailable:** OpenTokenUsage must expose `/v1/usage` on http://127.0.0.1:6736. No API keys are needed by the dock.
- **Bad settings:** unreadable JSON is preserved and the last good backup is loaded where possible. Data is under `%LOCALAPPDATA%\BELAC\Dock` unless BELAC_DOCK_HOME is set.
- **Bug reports:** include app/Windows versions, monitor scaling and reproduction steps. Review diagnostics before sharing: paths and window information can be private.
