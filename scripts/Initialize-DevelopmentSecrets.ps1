#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Project = (Join-Path $PSScriptRoot "../src/CleanTemplate.Host/CleanTemplate.Host.csproj"),
    [switch]$Rotate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Project -PathType Leaf)) {
    throw "Host project not found: $Project"
}

# Capture output: listing secrets must never write their values to this script's output.
$listing = & dotnet user-secrets list --project $Project --json 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read user-secrets. Check the .NET SDK and the project's UserSecretsId."
}

$json = ($listing | ForEach-Object { "$_" } | Where-Object { -not $_.TrimStart().StartsWith("//") }) -join "`n"
$existing = ConvertFrom-Json -InputObject $json -AsHashtable
$updates = @{}

foreach ($name in @("Auth:Schemes:LocalJwt:SigningKey", "IdentitySeed:AdminPassword")) {
    $value = $existing[$name]
    if ($Rotate -or [string]::IsNullOrWhiteSpace($value) -or $value.StartsWith("__SET_VIA_", [StringComparison]::Ordinal)) {
        $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(64)
        $generated = [Convert]::ToBase64String($bytes)
        # Guarantee Identity's character-class requirements, retaining cryptographic randomness.
        $updates[$name] = if ($name -eq "IdentitySeed:AdminPassword") { "Aa1!$generated" } else { $generated }
    }
}

if ($updates.Count -eq 0) {
    Write-Host "Development secrets already exist; no values changed."
    return
}

# Send JSON through stdin, not command-line arguments or a repository file.
$payload = ConvertTo-Json -InputObject $updates -Compress
$result = $payload | & dotnet user-secrets set --project $Project 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Unable to save development secrets. No secret values have been printed."
}

Write-Host "Development secrets saved to user-secrets ($($updates.Count) values); no values printed."
Write-Host "User-secrets are for local development only and are not encrypted. Use a secret manager in production."
if ($Rotate) {
    Write-Warning "Signing-key rotation invalidates existing local access tokens. An existing database user's password is not changed by updating the seed secret."
}
