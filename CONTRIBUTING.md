# Contributing

Use Windows 11 x64 and the .NET 10 SDK. Build with `scripts/build.ps1` and run unit tests with `scripts/test.ps1`. UI and taskbar lifecycle checks require an interactive desktop; see `docs/TESTING.md`.

Keep changes focused and describe changed behavior and checks run. Preserve settings compatibility, reduced motion, high contrast, narrow layouts and recovery-before-mutation behavior. Do not add provider secrets or machine-specific paths. Use isolated BELAC_DOCK_HOME directories for manual tests.

Before release, run tests, the NuGet vulnerability audit, and packaging scripts. Review archives and third-party notices. Publish prepared archives or a clean source checkout, never the entire working folder. Contributions are provided under the project MIT license.
