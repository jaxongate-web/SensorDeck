<#
.SYNOPSIS
  Builds a SensorDeck release: runs tests, publishes a self-contained single-file exe,
  and produces a portable .zip, an Inno Setup installer and SHA-256 checksums in .\dist.
  Upload everything in .\dist to the GitHub release (gh release create vX.Y.Z dist\*); the
  unversioned SensorDeck-setup.exe / SensorDeck-portable.zip keep the website's download links working.

.EXAMPLE
  .\build\release.ps1
  .\build\release.ps1 -SkipInstaller      # zip only (no Inno Setup needed)
#>
param(
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    # Freshly installed SDKs aren't on PATH in shells opened before the install.
    $sdk = Join-Path $env:ProgramFiles 'dotnet'
    if (Test-Path (Join-Path $sdk 'dotnet.exe')) { $env:PATH = "$sdk;$env:PATH" }
    else { throw '.NET 10 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0' }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

# Version comes from Directory.Build.props so there's a single source of truth.
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version not found in Directory.Build.props' }
Write-Host "SensorDeck $version" -ForegroundColor White

$publishDir = Join-Path $root 'publish\app'
$dist = Join-Path $root 'dist'
Remove-Item $publishDir, $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

if (-not $SkipTests) {
    Step 'Running tests'
    dotnet test tests/SensorDeck.Core.Tests -c Release --artifacts-path artifacts/build -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}

Step 'Publishing self-contained single-file exe (win-x64)'
# --artifacts-path keeps release output separate from dev builds (and from any running copy).
dotnet publish src/SensorDeck.App -c Release -r win-x64 -p:PublishSingleFile=true -p:DebugType=none `
    --artifacts-path artifacts/build -o $publishDir -nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Get-ChildItem $publishDir -Filter *.pdb | Remove-Item -Force

Step 'Creating portable zip'
$stage = Join-Path $root 'artifacts\portable\SensorDeck'
Remove-Item (Split-Path $stage) -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $publishDir 'SensorDeck.exe') $stage
Copy-Item LICENSE (Join-Path $stage 'LICENSE.txt')
Copy-Item THIRD-PARTY-NOTICES.txt $stage
@"
SensorDeck $version — free, lightweight hardware monitor for Windows
=====================================================================

Run SensorDeck.exe. No installation is needed. Windows will ask for administrator
permission because reading hardware sensors requires it.

For CPU temperature, CPU clock speed and motherboard fan readings, also install the
free, signed PawnIO driver from https://pawnio.eu/ (SensorDeck shows a banner if it's missing).

Settings are stored in %AppData%\SensorDeck\settings.json.
To uninstall the portable version, delete this folder (and optionally that settings folder).
If you turned on "Start with Windows", turn it off in Settings first.

Licence: MIT (see LICENSE.txt). Third-party components: see THIRD-PARTY-NOTICES.txt.
"@ | Set-Content -Encoding utf8 (Join-Path $stage 'README.txt')
$zip = Join-Path $dist "SensorDeck-$version-win-x64-portable.zip"
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

if (-not $SkipInstaller) {
    Step 'Building installer (Inno Setup)'
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or use -SkipInstaller.' }
    & $iscc /Qp "/DAppVersion=$version" "/DSourceDir=$publishDir" (Join-Path $root 'installer\SensorDeck.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
}

Step 'Adding stable download names'
# Unversioned copies give the website a permanent link that always serves the newest release:
#   https://github.com/<owner>/SensorDeck/releases/latest/download/SensorDeck-setup.exe
if (-not $SkipInstaller) {
    Copy-Item (Join-Path $dist "SensorDeck-$version-setup.exe") (Join-Path $dist 'SensorDeck-setup.exe')
}
Copy-Item $zip (Join-Path $dist 'SensorDeck-portable.zip')

Step 'Writing checksums'
$sums = Get-ChildItem $dist -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$sums | Set-Content -Encoding ascii (Join-Path $dist 'SHA256SUMS.txt')

Step 'Done'
Get-ChildItem $dist | Select-Object Name, @{ n = 'Size (MB)'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
