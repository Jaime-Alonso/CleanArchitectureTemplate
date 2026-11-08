Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path $root "artifacts/gate-fixtures/$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $fixture | Out-Null
$report = Join-Path $fixture 'report.json'
$thresholds = Join-Path $fixture 'thresholds.json'
'{"ordinary":{"lines":85,"branches":70,"classes":[{"name":"Critical","lines":95,"branches":95}]}}' |
    Set-Content $thresholds -Encoding utf8

function Expect-Failure([scriptblock]$Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw 'A failing quality fixture was accepted.' }
}
function Write-Report($Value) { $Value | ConvertTo-Json -Depth 20 | Set-Content $report -Encoding utf8 }
$coverage = @{
    summary = @{ coveredlines = 90; coverablelines = 100; coveredbranches = 80; totalbranches = 100 }
    coverage = @{ assemblies = @(@{ classesinassembly = @(@{ name = 'Critical'; coveredlines = 96; coverablelines = 100; coveredbranches = 98; totalbranches = 100 }) }) }
}
Write-Report $coverage
& "$root/scripts/ci/Assert-Coverage.ps1" -SummaryPath $report -ThresholdsPath $thresholds | Out-Null
$coverage.summary.coveredlines = 80
Write-Report $coverage
Expect-Failure { & "$root/scripts/ci/Assert-Coverage.ps1" -SummaryPath $report -ThresholdsPath $thresholds }
$coverage.summary.coveredlines = 90
$coverage.coverage.assemblies[0].classesinassembly[0].coveredbranches = 50
Write-Report $coverage
Expect-Failure { & "$root/scripts/ci/Assert-Coverage.ps1" -SummaryPath $report -ThresholdsPath $thresholds }
$coverage.coverage.assemblies = @()
Write-Report $coverage
Expect-Failure { & "$root/scripts/ci/Assert-Coverage.ps1" -SummaryPath $report -ThresholdsPath $thresholds }
Expect-Failure { & "$root/scripts/ci/Assert-Coverage.ps1" -SummaryPath "$fixture/missing.json" -ThresholdsPath $thresholds }

$audit = @{ version = 1; projects = @(@{ frameworks = @(@{ topLevelPackages = @(); transitivePackages = @() }) }) }
Write-Report $audit
& "$root/scripts/ci/Assert-Dependencies.ps1" -ReportPath $report | Out-Null
$audit.projects[0].frameworks[0].transitivePackages = @(@{ id = 'Synthetic.Vulnerable'; vulnerabilities = @(@{ severity = 'Low' }) })
Write-Report $audit
Expect-Failure { & "$root/scripts/ci/Assert-Dependencies.ps1" -ReportPath $report }
$audit.projects[0].frameworks[0].transitivePackages = @()
$audit.errors = @('Synthetic audit source failure')
Write-Report $audit
Expect-Failure { & "$root/scripts/ci/Assert-Dependencies.ps1" -ReportPath $report }
'invalid JSON' | Set-Content $report
Expect-Failure { & "$root/scripts/ci/Assert-Dependencies.ps1" -ReportPath $report }
Write-Output 'Quality gates reject low/missing coverage, uncovered critical branches, vulnerable transitives, failed audits and malformed reports.'
