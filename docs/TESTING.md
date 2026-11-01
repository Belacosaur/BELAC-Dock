# Testing

Use Windows and the .NET 10 SDK:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\test-ui.ps1
```

Unit tests cover settings/theme persistence, matching, ordering, usage, recovery and taskbar identity. UI smoke uses an isolated config and checks layout, resize, collapse, narrow LLM rings, stable menus during tracker refresh, logos, popouts, themes and high contrast. It requires an unlocked desktop and briefly displays test windows.

Owned-window taskbar lifecycle checks:

```powershell
dotnet build tests/Belac.Dock.Taskbar.Smoke
.\tests\Belac.Dock.Taskbar.Smoke\bin\Debug\net10.0-windows\Belac.Dock.Taskbar.Smoke.exe --lifecycle
.\tests\Belac.Dock.Taskbar.Smoke\bin\Debug\net10.0-windows\Belac.Dock.Taskbar.Smoke.exe --host-lifecycle "$PWD\tests\Belac.Dock.Ui.Smoke\bin\Debug\net10.0-windows\Belac.Dock.Ui.Smoke.exe"
```

These create their own windows/config and restore them. The optional `--process <name>` targets a real app; use only deliberately. Do not run interactive tests concurrently.

Before release, run `dotnet list BELAC.Dock.sln package --vulnerable --include-transitive`, both packaging scripts, and inspect the archives. Test the portable build on a clean account without a runtime. Hosted CI runs build/unit tests; interactive checks remain manual.
