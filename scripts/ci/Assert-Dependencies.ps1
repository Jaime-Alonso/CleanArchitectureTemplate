[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReportPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -AsHashtable
if ($report.version -ne 1 -or -not $report.ContainsKey('projects') -or @($report.projects).Count -eq 0) {
    throw 'Missing or unsupported dependency audit report.'
}
if ($report.ContainsKey('errors') -and @($report.errors).Count -gt 0) { throw 'Dependency audit could not complete.' }
$findings = [Collections.Generic.List[string]]::new()
foreach ($project in $report.projects) {
    if ($project.ContainsKey('errors') -and @($project.errors).Count -gt 0) { throw 'Project dependency audit could not complete.' }
    foreach ($framework in $project.frameworks) {
        foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
            if (-not $framework.ContainsKey($kind)) { continue }
            foreach ($package in $framework[$kind]) {
                if ($package.ContainsKey('vulnerabilities') -and @($package.vulnerabilities).Count -gt 0) {
                    $findings.Add($package.id)
                }
            }
        }
    }
}
if ($findings.Count -gt 0) { throw "Vulnerable dependencies: $(($findings | Sort-Object -Unique) -join ', ')" }
Write-Output 'Dependency audit passed, including transitive packages.'
