[CmdletBinding()]
param([string]$ResultsRoot = 'artifacts/security')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tools = Get-Content (Join-Path $PSScriptRoot 'tools.json') -Raw | ConvertFrom-Json
$results = Join-Path ([IO.Path]::GetFullPath((Join-Path $root $ResultsRoot))) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $results | Out-Null
& docker run --rm --network none --mount "type=bind,source=$root,target=/repo,readonly" --workdir /repo `
    $tools.actionlint .github/workflows/ci.yml
if ($LASTEXITCODE -ne 0) { throw 'Workflow validation failed.' }
& docker run --rm --network none --mount "type=bind,source=$root,target=/repo,readonly" `
    --mount "type=bind,source=$results,target=/report" $tools.gitleaks dir /repo `
    --config /repo/.gitleaks.toml --redact --ignore-gitleaks-allow --no-banner --no-color `
    --report-format json --report-path /report/gitleaks.json
if ($LASTEXITCODE -ne 0) { throw 'Secret scan failed. Inspect the redacted report.' }

# A synthetic token must be detected even inside a test file; no blanket test-folder exclusions.
$fixture = Join-Path $results 'fixture/test'
New-Item -ItemType Directory -Force $fixture | Out-Null
$synthetic = 'ghp_' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(18)).ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $fixture 'canary.txt'), "token=$synthetic")
& docker run --rm --network none --mount "type=bind,source=$root,target=/repo,readonly" `
    --mount "type=bind,source=$fixture,target=/fixture,readonly" $tools.gitleaks dir /fixture `
    --config /repo/.gitleaks.toml --redact --ignore-gitleaks-allow --no-banner --no-color
if ($LASTEXITCODE -ne 1) { throw 'Secret-scanner canary was not detected, or the scanner failed to execute.' }
Write-Output 'Source scan passed and the synthetic test-file canary was detected.'
