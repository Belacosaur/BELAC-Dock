param([switch]$PrepareOnly)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path
if (-not $PrepareOnly -and -not (Test-Path -LiteralPath (Join-Path $root "LICENSE"))) {
    throw "Add the owner-approved project LICENSE before packaging source. Use -PrepareOnly to inspect staged sources."
}
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$stage = Join-Path $root "dist/BELAC.Dock-source-$stamp"
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$roots = @("src", "tests", "samples", "scripts", "docs", ".github")
$rootFiles = @("BELAC.Dock.sln", "global.json", ".gitignore", "README.md", "ARCHITECTURE.md", "USAGE_INTEGRATION.md", "CONTRIBUTING.md", "SECURITY.md", "THIRD_PARTY_NOTICES.md", "LICENSE")
$extensions = @(".cs", ".csproj", ".xaml", ".manifest", ".ico", ".svg", ".md", ".txt", ".ps1", ".yml", ".yaml")
$files = @()
foreach ($name in $rootFiles) {
    $path = Join-Path $root $name
    if (Test-Path -LiteralPath $path) { $files += Get-Item -LiteralPath $path }
}
foreach ($name in $roots) {
    $path = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $files += Get-ChildItem -LiteralPath $path -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|artifacts|dist|TestResults|\.git)[\\/]' -and
        ($extensions -contains $_.Extension -or $_.Name -eq 'LICENSE')
    }
}
foreach ($file in $files) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked file: $($file.Name)" }
    $relative = $file.FullName.Substring($root.Length).TrimStart('\','/')
    $target = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
if ($PrepareOnly) { Write-Output $stage; return }
$zip = "$stage.zip"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
