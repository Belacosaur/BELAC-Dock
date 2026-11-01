$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build tests/Belac.Dock.Ui.Smoke/Belac.Dock.Ui.Smoke.csproj -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -o artifacts/ui-check-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "UI smoke build failed." }
    & ./artifacts/ui-check-build/Belac.Dock.Ui.Smoke.exe
    if ($LASTEXITCODE -ne 0) { throw "Floating UI smoke checks failed." }
} finally {
    Pop-Location
}
