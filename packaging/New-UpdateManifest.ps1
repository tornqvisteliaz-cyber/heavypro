param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Setup,
    [Parameter(Mandatory)] [string] $Url,
    [Parameter(Mandatory)] [string] $Out,
    # The update signing key as RSA XML (RSACryptoServiceProvider.ToXmlString($true)).
    # In CI it comes from the UPDATE_SIGNING_KEY secret; never commit it.
    [string] $KeyXml = $env:UPDATE_SIGNING_KEY
)

# Writes update.jws: a compact JWS (RS256) the app's Update button trusts, naming the
# version, where the installer is and its SHA-256. Works in Windows PowerShell 5.1 and pwsh 7.
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($KeyXml)) { throw "No update signing key (UPDATE_SIGNING_KEY)." }
if (-not [Version]::TryParse($Version, [ref]$null)) { throw "Not a version: $Version" }

function ConvertTo-Base64Url([byte[]] $bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$sha = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
$payload = [ordered]@{
    product   = "HeavyPro"
    version   = $Version
    url       = $Url
    sha256    = $sha
    published = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
} | ConvertTo-Json -Compress

$utf8 = New-Object System.Text.UTF8Encoding($false)
$head = ConvertTo-Base64Url $utf8.GetBytes('{"alg":"RS256","typ":"JWT"}')
$body = ConvertTo-Base64Url $utf8.GetBytes($payload)

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
try {
    $rsa.FromXmlString($KeyXml)
    if ($rsa.PublicOnly) { throw "UPDATE_SIGNING_KEY holds only a public key." }
    # The app only trusts packaging/update-public-key.json: signing with any other key
    # would publish a release nobody's Update button accepts.
    $jwkFile = Join-Path $PSScriptRoot "update-public-key.json"
    $expected = (Get-Content -LiteralPath $jwkFile -Raw | ConvertFrom-Json).n
    if ((ConvertTo-Base64Url $rsa.ExportParameters($false).Modulus) -ne $expected) {
        throw "UPDATE_SIGNING_KEY does not match packaging/update-public-key.json."
    }
    $signature = $rsa.SignData([Text.Encoding]::ASCII.GetBytes("$head.$body"),
        [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
} finally {
    $rsa.Dispose()
}

[IO.File]::WriteAllText($Out, "$head.$body." + (ConvertTo-Base64Url $signature), $utf8)
Write-Host "Signed update manifest for HeavyPro $Version ($sha)"
