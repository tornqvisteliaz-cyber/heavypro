param(
    [string] $DotNet = "dotnet",
    [string] $Configuration = "Release",
    [switch] $SkipObfuscation,
    # The caller already built (and, in CI, signed) HeavyPro.exe; building again here would
    # overwrite that signature with a fresh unsigned binary.
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repo "src\HeavyPro.App\HeavyPro.App.csproj"
$output = Join-Path $repo "src\HeavyPro.App\bin\x64\$Configuration\net48"
$archive = Join-Path $PSScriptRoot "HeavyProAppPayload.zip"

if (-not $SkipBuild) {
    & $DotNet build $project -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "HeavyPro build failed with exit code $LASTEXITCODE." }
}
if (-not (Test-Path (Join-Path $output "HeavyPro.exe"))) { throw "Build output not found: $output" }

# Obfuscate the shipped support DLLs (identifier renaming): the licence verification and the
# physics internals become unreadable while the public surface, strings and machine id are
# untouched. The WPF exe is left as-is (XAML binds and event handlers by name). Release builds pass
# -SkipObfuscation since 0.9.2: the obfuscated HeavyPro.Core broke the app at start-up, and plain,
# signed binaries are what antivirus products trust. Run without it only together with a start-up test.
if (-not $SkipObfuscation) {
    $obfOut = Join-Path $repo "obj-obf"
    if (Test-Path $obfOut) { Remove-Item -LiteralPath $obfOut -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $obfOut | Out-Null
    $template = Get-Content (Join-Path $PSScriptRoot "obfuscar.payload.xml") -Raw
    $configFile = Join-Path $obfOut "obfuscar.run.xml"
    $template.Replace("__INPATH__", $output).Replace("__OUTPATH__", $obfOut) | Set-Content $configFile -Encoding utf8

    & $DotNet tool restore
    if ($LASTEXITCODE -ne 0) { throw "Could not restore the Obfuscar tool (dotnet tool restore)." }
    # Obfuscar targets .NET 6; roll it forward to whatever runtime is installed (CI has .NET 8).
    $prev = $env:DOTNET_ROLL_FORWARD
    $env:DOTNET_ROLL_FORWARD = "LatestMajor"
    try {
        & $DotNet tool run obfuscar.console -- $configFile
        if ($LASTEXITCODE -ne 0) { throw "Obfuscar failed with exit code $LASTEXITCODE." }
    } finally {
        $env:DOTNET_ROLL_FORWARD = $prev
    }
    foreach ($dll in @("Rotaidem.Licensing.dll", "HeavyPro.Core.dll", "HeavyPro.Logging.dll")) {
        $obf = Join-Path $obfOut $dll
        if (-not (Test-Path $obf)) { throw "Obfuscation did not produce $dll." }
        Copy-Item -LiteralPath $obf -Destination (Join-Path $output $dll) -Force
    }
    Write-Host "Obfuscated the support DLLs (Rotaidem.Licensing, HeavyPro.Core, HeavyPro.Logging)."
}

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
$zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object {
        $_.Extension -ne ".pdb"
    } | ForEach-Object {
        # Substring instead of Path.GetRelativePath so Windows PowerShell 5.1 works too.
        $relative = $_.FullName.Substring($output.Length).TrimStart("\").Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $_.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
} finally {
    $zip.Dispose()
}

$zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    foreach ($name in @("SimConnect.dll", "Microsoft.FlightSimulator.SimConnect.dll", "NOTICE-MSFS-SimConnect.txt")) {
        $source = if ($name -eq "NOTICE-MSFS-SimConnect.txt") {
            Join-Path $PSScriptRoot $name
        } else {
            Join-Path $repo "lib\$name"
        }
        if (-not (Test-Path $source)) { throw "Missing payload file: $source" }
        $existing = $zip.GetEntry($name)
        if ($null -ne $existing) { $existing.Delete() }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $source, $name, [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
} finally {
    $zip.Dispose()
}

Write-Host "Created $archive with the HeavyPro app payload and SimConnect runtime files."
