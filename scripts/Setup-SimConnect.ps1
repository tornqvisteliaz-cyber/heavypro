# Copies MSFS 2024 SimConnect libraries into HeavyFeel\lib
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$lib = Join-Path $root "lib"
New-Item -ItemType Directory -Force -Path $lib | Out-Null

$candidates = @(
    $env:MSFS2024_SDK,
    $env:MSFS_SDK,
    "C:\MSFS 2024 SDK",
    "C:\MSFS2024 SDK",
    "$env:LOCALAPPDATA\Programs\Microsoft Flight Simulator 2024 SDK",
    "C:\Program Files\Microsoft Flight Simulator 2024 SDK"
)

$foundManaged = $null
$foundNative = $null

foreach ($base in $candidates) {
    if ([string]::IsNullOrWhiteSpace($base) -or -not (Test-Path $base)) { continue }
    $managed = Join-Path $base "SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
    $native = Join-Path $base "SimConnect SDK\lib\SimConnect.dll"
    if ((Test-Path $managed) -and (Test-Path $native)) {
        $foundManaged = $managed
        $foundNative = $native
        break
    }
}

if (-not $foundManaged) {
    Write-Host "Could not find the MSFS 2024 SDK automatically."
    Write-Host "Install the SDK from MSFS 2024 Developer Mode, then run this script again."
    Write-Host "Or copy these two files into: $lib"
    Write-Host "  Microsoft.FlightSimulator.SimConnect.dll"
    Write-Host "  SimConnect.dll"
    exit 1
}

Copy-Item $foundManaged (Join-Path $lib "Microsoft.FlightSimulator.SimConnect.dll") -Force
Copy-Item $foundNative (Join-Path $lib "SimConnect.dll") -Force
Write-Host "Copied SimConnect libraries from:"
Write-Host "  $foundManaged"
Write-Host "  $foundNative"
Write-Host "Now open HeavyFeel.sln in Visual Studio and press F5."
