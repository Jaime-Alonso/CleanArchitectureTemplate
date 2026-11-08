[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SummaryPath,
    [string]$Profile = 'ordinary',
    [string]$ThresholdsPath = (Join-Path $PSScriptRoot 'coverage-thresholds.json'),
    [switch]$PublishSummary
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $SummaryPath -Raw | ConvertFrom-Json -AsHashtable
$policies = Get-Content -LiteralPath $ThresholdsPath -Raw | ConvertFrom-Json -AsHashtable
if (-not $policies.ContainsKey($Profile)) { throw 'Unknown coverage policy.' }
$policy = $policies[$Profile]
if (-not $report.ContainsKey('summary') -or -not $report.ContainsKey('coverage')) { throw 'Missing coverage summary.' }
$classes = @($report.coverage.assemblies | ForEach-Object { $_.classesinassembly })
$checks = @(@{ name = 'All production assemblies'; metrics = $report.summary; lines = $policy.lines; branches = $policy.branches })
foreach ($requirement in $policy.classes) {
    $matches = @($classes | Where-Object { $_.name -ceq $requirement.name })
    if ($matches.Count -ne 1) { throw "Missing or ambiguous critical class: $($requirement.name)" }
    $checks += @{ name = $requirement.name; metrics = $matches[0]; lines = $requirement.lines; branches = $requirement.branches }
}
$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add('| Coverage scope | Lines | Floor | Branches | Floor |')
$markdown.Add('| --- | ---: | ---: | ---: | ---: |')
$failures = [Collections.Generic.List[string]]::new()
foreach ($check in $checks) {
    $metrics = $check.metrics
    if ($metrics.coverablelines -le 0 -or $metrics.coveredlines -lt 0 -or $metrics.coveredlines -gt $metrics.coverablelines -or
        $metrics.totalbranches -lt 0 -or $metrics.coveredbranches -lt 0 -or $metrics.coveredbranches -gt $metrics.totalbranches) {
        throw "Invalid or empty coverage metrics: $($check.name)"
    }
    $lines = 100.0 * $metrics.coveredlines / $metrics.coverablelines
    $branches = if ($metrics.totalbranches -eq 0) { 0.0 } else { 100.0 * $metrics.coveredbranches / $metrics.totalbranches }
    $markdown.Add(('| {0} | {1:F2}% | {2}% | {3:F2}% | {4}% |' -f $check.name, $lines, $check.lines, $branches, $check.branches))
    if ($lines -lt $check.lines -or $branches -lt $check.branches) { $failures.Add($check.name) }
}
$markdown | Write-Output
if ($PublishSummary -and $env:GITHUB_STEP_SUMMARY) { $markdown | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
if ($failures.Count -gt 0) { throw "Coverage below policy: $($failures -join ', ')" }
