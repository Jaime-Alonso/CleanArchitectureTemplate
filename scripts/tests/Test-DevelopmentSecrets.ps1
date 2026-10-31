#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$TemporaryRoot = (Join-Path ([IO.Path]::GetTempPath()) "opencode")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$testDirectory = Join-Path $TemporaryRoot "development-secrets-test-$([Guid]::NewGuid().ToString('N'))"
$originalAppData = $env:APPDATA
$originalHome = $env:HOME

function Get-TestSecrets([string]$Project) {
    $listing = & dotnet user-secrets list --project $Project --json 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Unable to list fixture secrets." }
    $json = ($listing | ForEach-Object { "$_" } | Where-Object { -not $_.TrimStart().StartsWith("//") }) -join "`n"
    return ConvertFrom-Json -InputObject $json -AsHashtable
}

try {
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
    # Isolate the CLI's secret store; never read or alter the developer's real user-secrets.
    $env:APPDATA = $testDirectory
    $env:HOME = $testDirectory
    $project = Join-Path $testDirectory "SecretsFixture.csproj"
    $projectContent = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><UserSecretsId>development-secrets-fixture</UserSecretsId></PropertyGroup></Project>'
    [IO.File]::WriteAllText($project, $projectContent)
    $script = Join-Path $PSScriptRoot "../Initialize-DevelopmentSecrets.ps1"

    $output = @(& $script -Project $project 6>&1) -join "`n"
    $first = Get-TestSecrets $project
    if ($first["Auth:Schemes:LocalJwt:SigningKey"].Length -lt 64 -or $first["IdentitySeed:AdminPassword"].Length -lt 64) {
        throw "Generated secrets do not meet the required lengths."
    }
    foreach ($value in $first.Values) {
        if ($output.Contains($value)) { throw "Bootstrap output disclosed a secret." }
    }

    & $script -Project $project 6>&1 | Out-Null
    $second = Get-TestSecrets $project
    foreach ($key in $first.Keys) {
        if ($first[$key] -ne $second[$key]) { throw "An existing secret was overwritten without -Rotate." }
    }

    # Regenerate only the missing/placeholder value, preserving other secrets.
    '{"IdentitySeed:AdminPassword":"__SET_VIA_USER_SECRETS__","Fixture:Unrelated":"preserve-this-value"}' |
        & dotnet user-secrets set --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Unable to seed fixture placeholder." }
    & $script -Project $project 6>&1 | Out-Null
    $partial = Get-TestSecrets $project
    if ($partial["Auth:Schemes:LocalJwt:SigningKey"] -ne $first["Auth:Schemes:LocalJwt:SigningKey"] -or
        $partial["IdentitySeed:AdminPassword"].StartsWith("__SET_VIA_") -or
        $partial["Fixture:Unrelated"] -ne "preserve-this-value") {
        throw "Partial initialization did not preserve existing settings."
    }

    & $script -Project $project -Rotate 3>&1 6>&1 | Out-Null
    $rotated = Get-TestSecrets $project
    foreach ($key in $first.Keys) {
        if ($partial[$key] -eq $rotated[$key]) { throw "Explicit rotation did not replace both secrets." }
    }
    if ($rotated["Fixture:Unrelated"] -ne "preserve-this-value") { throw "Rotation changed an unrelated setting." }

    Write-Host "Development bootstrap tests passed: generation, no disclosure, preservation, partial initialization and explicit rotation."
}
finally {
    $env:APPDATA = $originalAppData
    $env:HOME = $originalHome
    if (Test-Path -LiteralPath $testDirectory) {
        # Only this test's newly generated, uniquely named fixture directory is removed.
        Remove-Item -LiteralPath $testDirectory -Recurse -Force
    }
}
