param(
    [ValidateSet('All', 'SqlServer', 'PostgreSql')]
    [string]$Provider = 'All'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$work = [IO.Path]::GetFullPath((Join-Path $root 'work'))
$keys = @('RECOVERY_TEST_PROVIDER', 'RECOVERY_TEST_CONNECTION', 'RECOVERY_TEST_PHASE', 'MSSQL_SA_PASSWORD', 'POSTGRES_PASSWORD')
$original = @{}
foreach ($key in $keys) { $original[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }

function Check-Native([string]$description) {
    if ($LASTEXITCODE -ne 0) { throw "$description failed (exit $LASTEXITCODE)." }
}
function Sql([string]$container, [string]$query) {
    docker exec $container /bin/bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "$1"' -- $query
    Check-Native 'SQL Server command'
}
function Phase([string]$phase) {
    $env:RECOVERY_TEST_PHASE = $phase
    Write-Host "Recovery phase: $phase"
    dotnet test (Join-Path $root 'test/CleanTemplate.Infrastructure.Tests/CleanTemplate.Infrastructure.Tests.csproj') -c Release --nologo --filter 'FullyQualifiedName~BackupRestoreAcceptanceTests'
    Check-Native 'Recovery acceptance'
}

docker info --format '{{.ServerVersion}}'
Check-Native 'Docker availability'
try {
    if (-not (Test-Path -LiteralPath $work)) { New-Item -ItemType Directory -Path $work | Out-Null }
    if ((Get-Item -LiteralPath $work -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'work must not be a link.' }
    $providers = if ($Provider -eq 'All') { @('SqlServer', 'PostgreSql') } else { @($Provider) }
    foreach ($current in $providers) {
        $id = [Guid]::NewGuid().ToString('N')
        $name = "recovery-test-$id"
        $directory = [IO.Path]::GetFullPath((Join-Path $work "recovery-$id"))
        if (-not $directory.StartsWith($work + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe work path.' }
        $password = "aA1!$([Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)))"
        $created = $false
        $madeDirectory = $false
        try {
            New-Item -ItemType Directory -Path $directory | Out-Null
            $madeDirectory = $true
            if ($current -eq 'SqlServer') {
                $env:MSSQL_SA_PASSWORD = $password
                docker run --detach --name $name --label clean-template-test=recovery --publish '127.0.0.1::1433' --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD mcr.microsoft.com/mssql/server:2022-latest | Out-Null
                $portNumber = 1433
            } else {
                $env:POSTGRES_PASSWORD = $password
                docker run --detach --name $name --label clean-template-test=recovery --publish '127.0.0.1::5432' --env POSTGRES_PASSWORD postgres:17 | Out-Null
                $portNumber = 5432
            }
            Check-Native 'Container creation'
            $created = $true
            $ready = $false
            for ($attempt = 0; $attempt -lt 90; $attempt++) {
                if ($current -eq 'SqlServer') {
                    docker exec $name /bin/bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -Q "SELECT 1" -b -o /dev/null' 2>$null | Out-Null
                } else { docker exec $name pg_isready --username postgres 2>$null | Out-Null }
                if ($LASTEXITCODE -eq 0) { $ready = $true; break }
                Start-Sleep -Seconds 2
            }
            if (-not $ready) { throw "$current did not become ready." }
            $binding = docker port $name "$portNumber/tcp"
            Check-Native 'Port discovery'
            $port = ($binding.Trim() -split ':')[-1]
            $env:RECOVERY_TEST_PROVIDER = $current
            $env:RECOVERY_TEST_CONNECTION = if ($current -eq 'SqlServer') {
                "Server=127.0.0.1,$port;User ID=sa;Password=$password;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15"
            } else { "Host=127.0.0.1;Port=$port;Username=postgres;Password=$password;Timeout=15" }
            Write-Host "Testing native backup and restore: $current"
            if ($current -eq 'SqlServer') {
                Sql $name "CREATE DATABASE recovery_source ON (NAME = recovery_data, FILENAME = '/var/opt/mssql/data/recovery_source.mdf') LOG ON (NAME = recovery_log, FILENAME = '/var/opt/mssql/data/recovery_source.ldf')"
            } else {
                docker exec $name createdb -U postgres recovery_source
                Check-Native 'Create source'
            }
            Phase 'seed'
            if ($current -eq 'SqlServer') {
                Sql $name "BACKUP DATABASE recovery_source TO DISK = '/var/opt/mssql/data/recovery.bak' WITH COPY_ONLY, CHECKSUM, INIT"
                Sql $name "RESTORE VERIFYONLY FROM DISK = '/var/opt/mssql/data/recovery.bak' WITH CHECKSUM"
                $containerFile = '/var/opt/mssql/data/recovery.bak'
            } else {
                docker exec $name pg_dump -U postgres -Fc -f /tmp/recovery.dump recovery_source
                Check-Native 'pg_dump'
                $containerFile = '/tmp/recovery.dump'
            }
            $artifact = Join-Path $directory 'native-backup'
            docker cp "${name}:${containerFile}" $artifact
            Check-Native 'Export backup'
            Phase 'mutate'
            docker cp $artifact "${name}:${containerFile}.restore"
            Check-Native 'Import backup'
            if ($current -eq 'SqlServer') {
                Sql $name "RESTORE DATABASE recovery_target FROM DISK = '/var/opt/mssql/data/recovery.bak.restore' WITH CHECKSUM, MOVE 'recovery_data' TO '/var/opt/mssql/data/recovery_target.mdf', MOVE 'recovery_log' TO '/var/opt/mssql/data/recovery_target.ldf'"
                Sql $name "DBCC CHECKDB ('recovery_target') WITH NO_INFOMSGS"
            } else {
                docker exec $name createdb -U postgres recovery_target
                Check-Native 'Create restore target'
                docker exec $name pg_restore -U postgres --exit-on-error --no-owner --no-privileges -d recovery_target /tmp/recovery.dump.restore
                Check-Native 'pg_restore'
            }
            Phase 'verify'
            Write-Host "$current native recovery passed."
        }
        finally {
            if ($created) {
                docker rm --force $name | Out-Null
                if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove test container $name." }
            }
            if ($madeDirectory -and (Test-Path -LiteralPath $directory)) {
                $item = Get-Item -LiteralPath $directory -Force
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or -not $item.FullName.Equals($directory, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path.' }
                Remove-Item -LiteralPath $directory -Recurse -Force
            }
        }
    }
}
finally {
    foreach ($entry in $original.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
}
