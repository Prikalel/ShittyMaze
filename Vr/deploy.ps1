# Deploys ShittyMaze VR to a VR headset connected via ADB (USB).
# Usage:
#   .\deploy.ps1            install the existing APK and launch it
#   .\deploy.ps1 -Rebuild   rebuild (Release) first, then install and launch
param(
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'

$adb = 'd:\android-sdk\platform-tools\adb.exe'
$csproj = Join-Path $PSScriptRoot 'ShittyMaze.Vr.csproj'
$apk = Join-Path $PSScriptRoot 'bin\AnyCPU\Release\net10.0-android\ShittyMaze.ShittyMazeVR-Signed.apk'
$pkg = 'ShittyMaze.ShittyMazeVR'

if ($Rebuild) {
    # Build the csproj directly (NOT the solution) with explicit SDK paths,
    # matching the command in VR_ADAPTATION_PLAN.md section 6.
    dotnet build $csproj -c Release -p:AndroidSdkDirectory=d:\android-sdk -p:JavaSdkDirectory=d:\jdk
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

if (-not (Test-Path $apk)) {
    throw "APK not found: $apk`nRun first: dotnet build $csproj -c Release -p:AndroidSdkDirectory=d:\android-sdk -p:JavaSdkDirectory=d:\jdk"
}

Write-Host "==> Waiting for device..." -ForegroundColor Cyan
& $adb wait-for-device

$serials = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' } | ForEach-Object { ($_ -split "`t")[0] })
if ($serials.Count -eq 0) {
    throw "No authorized devices connected. Enable Developer Mode on the headset, connect it via USB and accept the 'Allow USB debugging' prompt inside the headset."
}

foreach ($serial in $serials) {
    Write-Host "==> Installing to $serial ..." -ForegroundColor Cyan
    & $adb -s $serial install -r "$apk"
    if ($LASTEXITCODE -ne 0) { throw "Install failed on $serial" }

    Write-Host "==> Launching on $serial ..." -ForegroundColor Cyan
    & $adb -s $serial shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 | Out-Null
}

Write-Host "Done." -ForegroundColor Green
