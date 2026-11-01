param([switch]$BuildOnly)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $BuildOnly -and -not (Test-Path -LiteralPath (Join-Path $root "LICENSE"))) {
    throw "Add the owner-approved project LICENSE before packaging a release. Use -BuildOnly for local validation."
}
Push-Location $root
try {
    # Unique output avoids stale files and never deletes an existing release.
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $out = Join-Path $root "dist/BELAC.Dock-win-x64-$stamp"
    dotnet publish src/Belac.Dock.App/Belac.Dock.App.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:DebugType=None -p:PublishTrimmed=false -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed; no archive was created." }
    foreach ($file in @("README.md", "THIRD_PARTY_NOTICES.md", "SECURITY.md", "LICENSE")) {
        if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $out }
    }
    Copy-Item -LiteralPath "docs/TROUBLESHOOTING.md" -Destination $out
    $licenses = Join-Path $out "licenses"
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item -LiteralPath "src/Belac.Dock.App/Assets/Providers/LICENSE" -Destination (Join-Path $licenses "provider-icons-LICENSE.txt")
    $assets = Get-Content -LiteralPath "src/Belac.Dock.App/obj/project.assets.json" -Raw | ConvertFrom-Json
    $packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    $runtime = Get-Content -LiteralPath (Join-Path $out "BELAC.Dock.runtimeconfig.json") -Raw | ConvertFrom-Json
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
        $packName = $framework.name.ToLowerInvariant() + ".runtime.win-x64"
        $pack = Join-Path $packageRoot "$packName/$($framework.version)"
        $destination = Join-Path $licenses "$packName-$($framework.version)"
        $runtimeNotices = @(Get-ChildItem -LiteralPath $pack -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY)' })
        if ($runtimeNotices.Count -eq 0) { throw "Missing runtime license for $packName" }
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $runtimeNotices | Copy-Item -Destination $destination
    }
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne "package") { continue }
        $package = Join-Path $packageRoot $library.Value.path
        $destination = Join-Path $licenses ("packages/" + ($library.Name -replace '/', '-'))
        $files = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY)' })
        if ($files.Count -gt 0) {
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($package.Length).TrimStart('\','/')
                $target = Join-Path $destination $relative
                New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target
            }
        }
    }
    if ($BuildOnly) { Write-Output $out; return }
    $zip = "$out.zip"
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
    Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
} finally { Pop-Location }
