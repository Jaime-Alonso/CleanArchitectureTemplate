$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work = Join-Path $root "work/openapi-client/$([guid]::NewGuid().ToString('N'))"
$toolName = if ($IsWindows) { 'nswag.exe' } else { 'nswag' }
$tool = Join-Path $root "work/tools/$toolName"
if (-not (Test-Path $tool)) { throw 'Install NSwag.ConsoleCore 14.7.1 in work/tools first; see README.' }
$installed = & dotnet tool list --tool-path (Join-Path $root 'work/tools')
if ($LASTEXITCODE -ne 0 -or -not ($installed -match '^nswag\.consolecore\s+14\.7\.1\s')) {
    throw 'Expected NSwag.ConsoleCore 14.7.1 in work/tools.'
}
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'OpenApiClientSmoke/*') $work -Force

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}

Invoke-Checked 'dotnet' @('build', (Join-Path $root 'src/CleanTemplate.Host/CleanTemplate.Host.csproj'), '--nologo')
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$url = "http://127.0.0.1:$port"
$password = 'Aa1!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$key = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = Join-Path $root 'src/CleanTemplate.Host'
$start.ArgumentList.Add((Join-Path $root 'src/CleanTemplate.Host/bin/Debug/net10.0/CleanTemplate.Host.dll'))
$start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
$start.Environment['DOTNET_ENVIRONMENT'] = 'Development'
$start.Environment['ASPNETCORE_URLS'] = $url
$start.Environment['DatabaseProvider'] = 'SqliteInMemory'
$start.Environment['ConnectionStrings__DefaultConnection'] = "Data Source=smoke-$([guid]::NewGuid().ToString('N'));Mode=Memory;Cache=Shared"
$start.Environment['ProductReads__Mode'] = 'Primary'
foreach ($name in @('ConnectionStrings__ReadConnection', 'ConnectionStrings__DapperConnection', 'ConnectionStrings__ProductReadReplica')) {
    $start.Environment[$name] = ''
}
$start.Environment['Caching__Provider'] = 'Memory'
$start.Environment['Auth__Schemes__Oidc__Enabled'] = 'false'
$start.Environment['Auth__Provisioning__Enabled'] = 'false'
$start.Environment['Auth__Schemes__LocalJwt__SigningKey'] = $key
$start.Environment['Auth__Schemes__LocalJwt__Enabled'] = 'true'
$start.Environment['IdentitySeed__AdminEmail'] = 'admin@local.template'
$start.Environment['IdentitySeed__AdminPassword'] = $password
$start.Environment['OpenTelemetry__Traces__Enabled'] = 'false'
$start.Environment['OpenTelemetry__Metrics__Enabled'] = 'false'
$process = $null
try {
    $process = [Diagnostics.Process]::Start($start)
    $document = Join-Path $work 'openapi.json'
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        if ($process.HasExited) { throw "Host exited with code $($process.ExitCode)" }
        try {
            Invoke-WebRequest -Uri "$url/openapi/v1.json" -OutFile $document -TimeoutSec 2
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Host OpenAPI document did not become ready in time.' }
    Invoke-Checked $tool @('openapi2csclient', "/input:$document", "/output:$(Join-Path $work 'Client.cs')", '/classname:ApiClient', '/namespace:Smoke', '/UseBaseUrl:false')
    Invoke-Checked 'dotnet' @('build', (Join-Path $work 'OpenApiClientSmoke.csproj'), '--nologo')
    $smoke = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $smoke.UseShellExecute = $false
    $smoke.CreateNoWindow = $true
    $smoke.RedirectStandardOutput = $true
    $smoke.RedirectStandardError = $true
    $smoke.ArgumentList.Add((Join-Path $work 'bin/Debug/net10.0/OpenApiClientSmoke.dll'))
    $smoke.Environment['SMOKE_URL'] = $url
    $smoke.Environment['SMOKE_PASSWORD'] = $password
    $clientProcess = [Diagnostics.Process]::Start($smoke)
    try {
        $stdout = $clientProcess.StandardOutput.ReadToEndAsync()
        $stderr = $clientProcess.StandardError.ReadToEndAsync()
        if (-not $clientProcess.WaitForExit(60000)) { throw 'Client smoke timed out.' }
        if ($clientProcess.ExitCode -ne 0) { throw "Client smoke failed with exit code $($clientProcess.ExitCode); output suppressed to protect secrets." }
        Write-Host $stdout.GetAwaiter().GetResult().Trim()
    } finally {
        if (-not $clientProcess.HasExited) { $clientProcess.Kill(); $clientProcess.WaitForExit() }
        $clientProcess.Dispose()
    }
    Write-Host 'OpenAPI client smoke passed.'
} finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
