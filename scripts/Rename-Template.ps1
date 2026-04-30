[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$NewPrefix,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$NewSolutionName,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$OldPrefix = "CleanTemplate",

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$OldSolutionName = "CleanArchitectureTemplate",

    [Parameter()]
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:ExcludedPathPattern = '[\\/](\.git|\.vs|\.idea|bin|obj)([\\/]|$)'
$script:TextExtensions = @(
    ".cs",
    ".csproj",
    ".slnx",
    ".json",
    ".md",
    ".http",
    ".props",
    ".targets",
    ".xml",
    ".yml",
    ".yaml",
    ".editorconfig"
)

function Test-IsExcludedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return $Path -match $script:ExcludedPathPattern
}

function Get-NewNameFromTemplate {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$CurrentPrefix,
        [Parameter(Mandatory = $true)][string]$TargetPrefix,
        [Parameter(Mandatory = $true)][string]$CurrentSolutionName,
        [Parameter(Mandatory = $true)][string]$TargetSolutionName
    )

    $updated = $Value -replace [regex]::Escape($CurrentPrefix), $TargetPrefix
    $updated = $updated -replace [regex]::Escape($CurrentSolutionName), $TargetSolutionName
    return $updated
}

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    return [IO.Path]::GetRelativePath($BasePath, $TargetPath)
}

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path -LiteralPath $repoRoot -PathType Container)) {
    throw "Could not resolve repository root from $PSScriptRoot"
}

if ($OldPrefix -eq $NewPrefix -and $OldSolutionName -eq $NewSolutionName) {
    throw "No changes to apply: new values are the same as current values."
}

Write-Host "Repository: $repoRoot"
Write-Host "Prefix: $OldPrefix -> $NewPrefix"
Write-Host "Solution: $OldSolutionName -> $NewSolutionName"
if ($DryRun) {
    Write-Host "Dry run mode enabled."
}

$allFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File
$candidateTextFiles = $allFiles | Where-Object {
    -not (Test-IsExcludedPath -Path $_.FullName) -and $script:TextExtensions.Contains($_.Extension)
}

$contentUpdates = 0
foreach ($file in $candidateTextFiles) {
    $raw = Get-Content -LiteralPath $file.FullName -Raw
    $updated = Get-NewNameFromTemplate -Value $raw -CurrentPrefix $OldPrefix -TargetPrefix $NewPrefix -CurrentSolutionName $OldSolutionName -TargetSolutionName $NewSolutionName

    if ($updated -ne $raw) {
        $contentUpdates++
        $relative = Get-RepoRelativePath -BasePath $repoRoot -TargetPath $file.FullName
        Write-Host "Updating content: $relative"
        if (-not $DryRun) {
            Set-Content -LiteralPath $file.FullName -Value $updated -Encoding utf8
        }
    }
}

$filesToRename = $allFiles | Where-Object {
    -not (Test-IsExcludedPath -Path $_.FullName) -and (
        $_.Extension -ne ".user"
    ) -and (
        $_.Name.Contains($OldPrefix) -or $_.Name.Contains($OldSolutionName)
    )
} | Sort-Object { $_.FullName.Length } -Descending

$renamedFiles = 0
foreach ($file in $filesToRename) {
    $newName = Get-NewNameFromTemplate -Value $file.Name -CurrentPrefix $OldPrefix -TargetPrefix $NewPrefix -CurrentSolutionName $OldSolutionName -TargetSolutionName $NewSolutionName
    if ($newName -eq $file.Name) {
        continue
    }

    $relative = Get-RepoRelativePath -BasePath $repoRoot -TargetPath $file.FullName
    Write-Host "Renaming file: $relative -> $newName"
    $renamedFiles++
    if (-not $DryRun) {
        Rename-Item -LiteralPath $file.FullName -NewName $newName
    }
}

$allDirectories = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory
$directoriesToRename = $allDirectories | Where-Object {
    -not (Test-IsExcludedPath -Path $_.FullName) -and (
        $_.Name.Contains($OldPrefix) -or $_.Name.Contains($OldSolutionName)
    )
} | Sort-Object { $_.FullName.Length } -Descending

$renamedDirectories = 0
foreach ($directory in $directoriesToRename) {
    $newName = Get-NewNameFromTemplate -Value $directory.Name -CurrentPrefix $OldPrefix -TargetPrefix $NewPrefix -CurrentSolutionName $OldSolutionName -TargetSolutionName $NewSolutionName
    if ($newName -eq $directory.Name) {
        continue
    }

    $relative = Get-RepoRelativePath -BasePath $repoRoot -TargetPath $directory.FullName
    Write-Host "Renaming directory: $relative -> $newName"
    $renamedDirectories++
    if (-not $DryRun) {
        Rename-Item -LiteralPath $directory.FullName -NewName $newName
    }
}

$remainingReferences = @()
if (-not $DryRun) {
    $allFilesAfter = Get-ChildItem -LiteralPath $repoRoot -Recurse -File
    $remainingReferences = $allFilesAfter | Where-Object {
        -not (Test-IsExcludedPath -Path $_.FullName) -and $script:TextExtensions.Contains($_.Extension)
    } | ForEach-Object {
        $matches = @(Select-String -LiteralPath $_.FullName -Pattern $OldPrefix, $OldSolutionName -SimpleMatch)
        if ($matches.Count -gt 0) {
            [PSCustomObject]@{
                Path = $_.FullName
                Count = $matches.Count
            }
        }
    }
}

Write-Host ""
Write-Host "Result"
Write-Host "- Updated files: $contentUpdates"
Write-Host "- Renamed files: $renamedFiles"
Write-Host "- Renamed directories: $renamedDirectories"

if (-not $DryRun -and @($remainingReferences).Count -gt 0) {
    Write-Warning "References to '$OldPrefix' or '$OldSolutionName' still remain in some files."
    foreach ($item in $remainingReferences | Sort-Object Count -Descending) {
        $relative = Get-RepoRelativePath -BasePath $repoRoot -TargetPath $item.Path
        Write-Host "  - $relative ($($item.Count) matches)"
    }
} elseif (-not $DryRun) {
    Write-Host "No references to old names remain in targeted text files."
}

if ($DryRun) {
    Write-Host ""
    Write-Host "Dry run completed: no changes were made."
}
