# Runs Unity Editor batch mode with Hub licensing args, a hard timeout,
# and cleanup of test-spawned processes. Invoke from an escalated shell.
param(
    [string]$UnityExe = 'D:\Application\Unity\6000.3.19f1\Editor\Unity.exe',
    [string]$ProjectPath = 'D:\UnityProject\Factory-with-DOTS',
    [string]$LicensingIpc = '',
    [int]$TimeoutSeconds = 240,
    [string]$LogFile = '',
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = 'Stop'

function Get-LicensingIpcChannel {
    $logFiles = @()
    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        $logDir = Join-Path $env:APPDATA 'UnityHub\logs'
        $logFiles = @(
            (Join-Path $logDir 'info-log.json'),
            (Join-Path $logDir 'info-log.json.1')
        )
    }

    foreach ($logPath in $logFiles) {
        if (-not (Test-Path -LiteralPath $logPath)) {
            continue
        }

        $candidate = $null
        foreach ($line in Get-Content -LiteralPath $logPath) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }
            try {
                $entry = $line | ConvertFrom-Json
            } catch {
                continue
            }
            if ($entry.moduleName -eq 'LaunchProcess' -and
                $entry.msg -match "-licensingIpc',\s*'([^']+)'") {
                $candidate = $Matches[1]
            }
        }
        if ($candidate) {
            return $candidate
        }
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($LicensingIpc)) {
    $discovered = Get-LicensingIpcChannel
    $LicensingIpc = if ($discovered) {
        $discovered
    } else {
        "LicenseClient-$env:USERNAME"
    }
}

if ([string]::IsNullOrWhiteSpace($LogFile)) {
    $LogFile = Join-Path $ProjectPath 'Logs\unity-batch-run.log'
}
$logDir = Split-Path -Parent $LogFile
if (-not [string]::IsNullOrWhiteSpace($logDir) -and -not (Test-Path -LiteralPath $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$unityArgs = @(
    '-batchmode',
    '-acceptSoftwareTermsForThisRunOnly',
    '-useHub',
    '-hubIPC',
    '-cloudEnvironment', 'production',
    '-licensingIpc', $LicensingIpc,
    '-projectPath', $ProjectPath,
    '-logFile', $LogFile
) + @($ExtraArgs)

Write-Host "[unity-launch] licensingIpc=$LicensingIpc"

$process = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
}

$timedOut = -not $process.HasExited
if ($timedOut) {
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
}

$editorRoot = Split-Path -Parent $UnityExe
Get-Process -Name Unity.Licensing.Client -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($editorRoot, [System.StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

[pscustomobject]@{
    ExitCode = $process.ExitCode
    TimedOut = $timedOut
    LicensingIpc = $LicensingIpc
    LogFile = $LogFile
}
