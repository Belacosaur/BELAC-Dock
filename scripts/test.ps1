$ErrorActionPreference = "Stop"
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet test BELAC.Dock.sln -c Release -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
} finally { Pop-Location }
