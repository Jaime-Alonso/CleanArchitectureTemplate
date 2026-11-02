param(
    [ValidateSet('All', 'SqlServer', 'PostgreSql')]
    [string]$Provider = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$originalEnvironment = @{}
foreach ($key in @('VERSIONED_MIGRATIONS_TEST_PROVIDER', 'VERSIONED_MIGRATIONS_TEST_CONNECTION', 'MSSQL_SA_PASSWORD', 'POSTGRES_PASSWORD')) {
    $originalEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}

docker info --format '{{.ServerVersion}}'
if ($LASTEXITCODE -ne 0) { throw 'Docker must be running.' }

try {
    $providers = if ($Provider -eq 'All') { @('SqlServer', 'PostgreSql') } else { @($Provider) }
    foreach ($current in $providers) {
        $name = "versioned-migrations-$($current.ToLowerInvariant())-$([Guid]::NewGuid().ToString('N'))"
        $password = "aA1!$([Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)))"
        $created = $false
        try {
            if ($current -eq 'SqlServer') {
                $env:MSSQL_SA_PASSWORD = $password
                docker run --detach --name $name --label clean-template-test=versioned-migrations --publish '127.0.0.1::1433' --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD mcr.microsoft.com/mssql/server:2022-latest
                $portNumber = 1433
            }
            else {
                $env:POSTGRES_PASSWORD = $password
                docker run --detach --name $name --label clean-template-test=versioned-migrations --publish '127.0.0.1::5432' --env POSTGRES_PASSWORD postgres:17
                $portNumber = 5432
            }
            if ($LASTEXITCODE -ne 0) { throw "Could not create $current container." }
            $created = $true

            $ready = $false
            for ($attempt = 0; $attempt -lt 90; $attempt++) {
                if ($current -eq 'SqlServer') {
                    # The secret stays in the container environment, not in command arguments or test output.
                    docker exec $name /bin/bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -Q "SELECT 1" -b -o /dev/null' 2>$null | Out-Null
                }
                else {
                    docker exec $name pg_isready --username postgres 2>$null | Out-Null
                }
                if ($LASTEXITCODE -eq 0) { $ready = $true; break }
                Start-Sleep -Seconds 2
            }
            if (-not $ready) { throw "$current did not become ready within 180 seconds." }

            $binding = docker port $name "$portNumber/tcp"
            if ($LASTEXITCODE -ne 0) { throw 'Could not resolve test container port.' }
            $port = ($binding.Trim() -split ':')[-1]
            $env:VERSIONED_MIGRATIONS_TEST_PROVIDER = $current
            $env:VERSIONED_MIGRATIONS_TEST_CONNECTION = if ($current -eq 'SqlServer') {
                "Server=127.0.0.1,$port;User ID=sa;Password=$password;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15"
            }
            else {
                "Host=127.0.0.1;Port=$port;Username=postgres;Password=$password;Timeout=15"
            }

            Write-Host "Running versioned migrations acceptance on $current (isolated migrated databases)."
            dotnet test (Join-Path $root 'test/CleanTemplate.Infrastructure.Tests/CleanTemplate.Infrastructure.Tests.csproj') -c Release --nologo --filter 'FullyQualifiedName~VersionedMigrationsAcceptanceTests'
            if ($LASTEXITCODE -ne 0) { throw "$current versioned migrations acceptance failed." }
        }
        finally {
            if ($created) {
                docker rm --force $name | Out-Null
                if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove test container $name." }
            }
        }
    }
}
finally {
    foreach ($entry in $originalEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}
