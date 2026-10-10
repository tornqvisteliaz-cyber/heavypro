param(
    [string] $DotNet = "dotnet",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repo "src\HeavyFeel.App\HeavyFeel.App.csproj"
$output = Join-Path $repo "src\HeavyFeel.App\bin\x64\$Configuration\net48"
$archive = Join-Path $PSScriptRoot "HeavyProAppPayload.zip"

& $DotNet build $project -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "HeavyPro build failed with exit code $LASTEXITCODE." }
if (-not (Test-Path (Join-Path $output "HeavyPro.exe"))) { throw "Build output not found: $output" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
$zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object {
        $_.Extension -ne ".pdb"
    } | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($output, $_.FullName).Replace("\", "/")
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
