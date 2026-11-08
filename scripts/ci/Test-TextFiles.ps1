Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
try {
    $paths = (& git -c core.quotepath=false ls-files --cached --others --exclude-standard) | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate repository source.' }
    $extensions = @('.cs', '.csproj', '.props', '.targets', '.slnx', '.json', '.md', '.yml', '.yaml', '.ps1', '.toml', '.runsettings', '.config', '.http')
    $special = @('.editorconfig', '.gitattributes', '.gitignore')
    $failures = [Collections.Generic.List[string]]::new()
    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        if ([IO.Path]::GetExtension($path).ToLowerInvariant() -notin $extensions -and $path -notin $special) { continue }
        $bytes = [IO.File]::ReadAllBytes((Join-Path $root $path))
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $invalidEnding = if ($path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) {
            $text -match '(?<!\r)\n|\r(?!\n)'
        } else { $text.Contains("`r") }
        if ($invalidEnding -or ($text.Length -gt 0 -and -not $text.EndsWith("`n")) -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)) {
            $failures.Add($path)
        }
    }
    if ($failures.Count -gt 0) { throw "Normalize UTF-8, final newline and checkout endings: $($failures -join ', ')" }
    Write-Output 'Source encoding and line endings passed.'
}
finally { Pop-Location }
