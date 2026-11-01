$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$previousHome = $env:BELAC_DOCK_HOME
try {
    dotnet build src/Belac.Dock.App/Belac.Dock.App.csproj -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw "Smoke build failed." }
    $exe = Join-Path $root "src/Belac.Dock.App/bin/Debug/net10.0-windows/BELAC.Dock.exe"
    $home = Join-Path $root ("artifacts/smoke-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $home -Force | Out-Null
    $env:BELAC_DOCK_HOME = $home
    $report = Join-Path $home "smoke.txt"
    $process = Start-Process -FilePath $exe -ArgumentList @("--smoke", "`"$report`"") -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Smoke failed: $($process.ExitCode)" }
    Get-Content -LiteralPath $report
} finally {
    $env:BELAC_DOCK_HOME = $previousHome
    Pop-Location
}
