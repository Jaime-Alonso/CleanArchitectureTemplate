[CmdletBinding()]
param([string]$ResultsRoot = 'artifacts/quality')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
try {
    $sdk = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
    if ((& dotnet --version) -ne $sdk) { throw 'The SDK must match global.json exactly.' }
    # A fresh directory prevents stale TRX/coverage from making a failed collector appear successful.
    $resultsBase = if ([IO.Path]::IsPathRooted($ResultsRoot)) { $ResultsRoot } else { Join-Path $root $ResultsRoot }
    $results = Join-Path ([IO.Path]::GetFullPath($resultsBase)) ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $results | Out-Null
    & "$PSScriptRoot/Test-TextFiles.ps1"
    & dotnet restore CleanArchitectureTemplate.slnx --locked-mode --configfile NuGet.Config --nologo
    & dotnet tool restore --configfile NuGet.Config
    & dotnet format CleanArchitectureTemplate.slnx --no-restore --verify-no-changes --report (Join-Path $results 'format')
    & dotnet build CleanArchitectureTemplate.slnx -c Release --no-restore --nologo -p:ContinuousIntegrationBuild=true
    & dotnet list CleanArchitectureTemplate.slnx package --vulnerable --include-transitive --no-restore --format json --output-version 1 |
        Set-Content -LiteralPath (Join-Path $results 'dependencies.json') -Encoding utf8
    & "$PSScriptRoot/Assert-Dependencies.ps1" -ReportPath (Join-Path $results 'dependencies.json')
    & dotnet test CleanArchitectureTemplate.slnx -c Release --no-build --no-restore --nologo --settings coverage.runsettings `
        --collect 'XPlat Code Coverage' --logger trx --results-directory (Join-Path $results 'tests')
    # VSTest may copy attachments into TRX/In as well. Count each collector session once.
    $reports = @(Get-ChildItem (Join-Path $results 'tests') -Directory |
        Where-Object { $_.Name -match '^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$' } |
        ForEach-Object { Get-Item (Join-Path $_.FullName 'coverage.cobertura.xml') -ErrorAction Stop })
    if ($reports.Count -ne 5) { throw 'Coverage collector did not produce all five ordinary project reports.' }
    & dotnet reportgenerator "-reports:$($reports.FullName -join ';')" "-targetdir:$results/coverage" `
        '-reporttypes:HtmlInline;Cobertura;JsonSummary;MarkdownSummaryGithub' "-sourcedirs:$root"
    & "$PSScriptRoot/Assert-Coverage.ps1" -SummaryPath (Join-Path $results 'coverage/Summary.json') -PublishSummary
    & "$root/scripts/tests/Test-HttpHostIsolation.ps1"
    Write-Output "Quality results: $results"
}
finally { Pop-Location }
