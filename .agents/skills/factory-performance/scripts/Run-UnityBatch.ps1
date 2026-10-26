# Project-only forwarding wrapper; all launches use the global guarded runner.
[CmdletBinding()]
param(
    [string]$UnityExe = '',
    [string]$ProjectPath = '',
    [string]$LicensingIpc = '',
    [ValidateRange(1, 86400)] [int]$TimeoutSeconds = 240,
    [string]$LogFile = '',
    [string[]]$ExtraArgs = @()
)
$ErrorActionPreference = 'Stop'
$editors = @(Get-Process -Name Unity -ErrorAction SilentlyContinue)
if ($editors.Count -gt 0) {
    throw 'UNITY_EDITOR_ALREADY_RUNNING: Close all running Unity Editors before continuing. No Editor was started or stopped.'
}
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
if ($ProjectPath -and -not [string]::Equals((Resolve-Path -LiteralPath $ProjectPath).Path.TrimEnd('\'),
    $projectRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'factory-performance may only run its own project checkout.'
}
$skillsHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$runner = Join-Path $skillsHome 'skills/unity-launch/scripts/Run-UnityBatch.ps1'
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) { throw 'Global unity-launch runner is missing; do not launch Unity directly.' }
& $runner -ProjectPath $projectRoot -UnityExe $UnityExe -LicensingIpc $LicensingIpc `
    -TimeoutSeconds $TimeoutSeconds -LogFile $LogFile -ExtraArgs $ExtraArgs
