[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Poison only the child test runner. Never mutate the caller's process environment.
$isolationStart = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$isolationStart.WorkingDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$isolationStart.UseShellExecute = $false
$isolationStart.CreateNoWindow = $true
$isolationStart.RedirectStandardOutput = $true
$isolationStart.RedirectStandardError = $true
foreach ($argument in @(
    'test', 'test/CleanTemplate.Api.IntegrationTests/CleanTemplate.Api.IntegrationTests.csproj',
    '-c', $Configuration, '--no-restore', '--nologo',
    '--filter', 'FullyQualifiedName~HostIsolationTests'
)) {
    $isolationStart.ArgumentList.Add($argument)
}

# Scalar values, nested collections and obsolete settings must be removed before
# Program's preflight/registrations, rather than merely hidden in the final DI configuration.
$isolationStart.Environment['DatabaseProvider'] = 'CANARY_INVALID_PROVIDER'
$isolationStart.Environment['ConnectionStrings__DefaultConnection'] = 'CANARY_EXTERNAL_DATABASE'
$isolationStart.Environment['ConnectionStrings__ReadConnection'] = 'CANARY_LEGACY_DATABASE'
$isolationStart.Environment['Auth__Schemes__LocalJwt__SigningKeys__0__KeyId'] = 'CANARY_INCOMPLETE_KEY'
$isolationStart.Environment['Auth__Schemes__LocalJwt__AccessTokenMinutes'] = 'not-a-number'
$isolationStart.Environment['ForwardedHeaders__KnownProxies__0'] = 'CANARY_INVALID_PROXY'
$isolationStart.Environment['RateLimiting__Global__QueueLimit'] = 'not-a-number'

$isolationProcess = [System.Diagnostics.Process]::Start($isolationStart)
$isolationOutput = $isolationProcess.StandardOutput.ReadToEndAsync()
$isolationError = $isolationProcess.StandardError.ReadToEndAsync()
$isolationTimeout = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromMinutes(2))
try {
    $isolationProcess.WaitForExitAsync($isolationTimeout.Token).GetAwaiter().GetResult()
    Write-Output $isolationOutput.GetAwaiter().GetResult()
    Write-Output $isolationError.GetAwaiter().GetResult()
    if ($isolationProcess.ExitCode -ne 0) {
        throw 'HTTP host isolation failed with hostile ambient configuration.'
    }
}
finally {
    if (-not $isolationProcess.HasExited) {
        $isolationProcess.Kill($true)
        $isolationProcess.WaitForExit()
    }
    $isolationTimeout.Dispose()
    $isolationProcess.Dispose()
}
