# Compatibility

Target: Windows 11 x64, .NET 10 LTS. Source builds need SDK 10.0.100 or a newer 10.0 feature band. Portable releases include the runtime. Interactive checks used Windows 11 25H2; other Windows builds, mixed-DPI multi-monitor setups and localized taskbars need broader testing.

The dock floats and does not reserve desktop work area. The legacy appbar self-test exercises only that Windows component.

Taskbar separation uses taskbar APIs, UI Automation and an optional non-public read-only Shell identity resolver. Custom taskbars, elevated/protected apps and unsupported automation structures may fail verification. Pinned-only shortcuts are recognized conservatively on the tested English taskbar.

Style compatibility is configurable per app and may affect Alt+Tab. Grouped windows, app/dock restarts and recovery have owned-window lifecycle fixtures; this does not prove universal application compatibility.

Check Microsoft's [.NET support policy](https://dotnet.microsoft.com/platform/support/policy) before publishing future runtime bundles.
