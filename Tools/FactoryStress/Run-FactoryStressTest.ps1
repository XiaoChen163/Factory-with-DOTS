param(
    [string]$UnityExe = 'D:\Application\Unity\6000.3.19f1\Editor\Unity.exe',
    [string]$ProjectPath = '',
    [string]$Scene = 'Perf_Straight_Scalable',
    [int]$StartScale = 2,
    [int]$MaxScale = [int]::MaxValue,
    [int]$Multiplier = 2,
    [int]$LoadPercent = 50,
    [float]$WarmupSeconds = 5,
    [float]$SampleSeconds = 10,
    [float]$TimeDelaySeconds = 0.25,
    [float]$DragSeconds = 0.25,
    [ValidateRange(0, 1)]
    [int]$DemolitionOrder = 0,
    [float]$TpsThreshold = 50,
    [float]$FpsThreshold = 0,
    [ValidateSet('Any', 'All')]
    [string]$ThresholdMode = 'Any',
    [string]$OutputDir = '',
    [int]$TimeoutSeconds = 300,
    [switch]$Graphics,
    [switch]$ReimportSubScene
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = (Resolve-Path '.').Path
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $ProjectPath 'Docs\PerformanceReports\StressTest'
}
if ($StartScale -lt 1 -or $MaxScale -lt $StartScale) {
    throw 'StartScale must be >= 1 and MaxScale must be >= StartScale.'
}
if ($Multiplier -lt 2) {
    throw 'Multiplier must be >= 2.'
}
if (-not (Test-Path -LiteralPath $UnityExe)) {
    throw "Unity Editor not found: $UnityExe"
}

function Get-LicensingIpcChannel {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        $logDir = Join-Path $env:APPDATA 'UnityHub\logs'
        $candidates = @(
            (Join-Path $logDir 'info-log.json'),
            (Join-Path $logDir 'info-log.json.1')
        )
    }

    foreach ($path in $candidates) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }
        foreach ($line in Get-Content -LiteralPath $path) {
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
                return $Matches[1]
            }
        }
    }

    return $null
}

function Test-AttemptPassed {
    param(
        [pscustomobject]$Report
    )

    $tps = [double]$Report.fixedTicksPerSecond
    $fps = [double]$Report.framesPerSecond
    $tpsFailed = $TpsThreshold -gt 0 -and $tps -lt $TpsThreshold
    $fpsFailed = $FpsThreshold -gt 0 -and $fps -lt $FpsThreshold

    if ($ThresholdMode -eq 'All' -and
        $TpsThreshold -gt 0 -and
        $FpsThreshold -gt 0) {
        return -not ($tpsFailed -and $fpsFailed)
    }

    return -not ($tpsFailed -or $fpsFailed)
}

function Invoke-PerformanceAttempt {
    param(
        [int]$Scale,
        [bool]$Detailed,
        [string]$OutputPath
    )

    $attemptLog = Join-Path (Split-Path -Parent $OutputPath) 'unity.log'
    $unityArgs = @(
        '-batchmode',
        '-acceptSoftwareTermsForThisRunOnly',
        '-useHub',
        '-hubIPC',
        '-cloudEnvironment', 'production',
        '-licensingIpc', $licensingIpc,
        '-projectPath', $ProjectPath,
        '-logFile', $attemptLog,
        '-executeMethod', $(if ($ReimportSubScene) {
            'FactoryPerformanceBatchRunner.ReimportSubSceneAndRun'
        } else {
            'FactoryPerformanceBatchRunner.Run'
        }),
        '-factoryPerformanceScene', $Scene,
        '-factoryPerformanceScale', ([string]$Scale),
        '-factoryPerformanceLoadPercent', ([string]$LoadPercent),
        '-factoryPerformanceCapture',
        '-factoryPerformanceWarmupSeconds', ([string]$WarmupSeconds),
        '-factoryPerformanceSampleSeconds', ([string]$SampleSeconds),
        '-factoryPerformanceTimeDelay', ([string]$TimeDelaySeconds),
        '-factoryPerformanceDragSeconds', ([string]$DragSeconds),
        '-factoryPerformanceDemolitionOrder', ([string]$DemolitionOrder),
        '-factoryPerformanceOutput', $OutputPath
    )
    if (-not $Detailed) {
        $unityArgs += @('-factoryPerformanceCompact')
    }
    if (-not $Graphics) {
        $unityArgs += @('-nographics')
    }

    $process = Start-Process -FilePath $UnityExe `
        -ArgumentList $unityArgs `
        -PassThru `
        -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
    }

    $timedOut = -not $process.HasExited
    if ($timedOut) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }

    $status = 'error'
    $report = $null
    if (-not $timedOut -and $process.ExitCode -eq 0 -and
        (Test-Path -LiteralPath $OutputPath)) {
        $report = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
        if ($null -ne $report.fixedTicksPerSecond) {
            $status = if (Test-AttemptPassed -Report $report) {
                'passed'
            } else {
                'failed'
            }
        }
    }

    return [pscustomobject]@{
        Status = $status
        Report = $report
        TimedOut = $timedOut
    }
}

$licensingIpc = Get-LicensingIpcChannel
if ([string]::IsNullOrWhiteSpace($licensingIpc)) {
    $licensingIpc = "LicenseClient-$env:USERNAME"
}

$runId = 'stress-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$runRoot = Join-Path $OutputDir (Join-Path $Scene $runId)
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$attempts = @()
$scale = $StartScale
$lastPassingScale = $null
$firstFailingScale = $null
$thresholdReportPath = $null
$limitFound = $false
$noLimit = $false
$aborted = $false

while ($scale -le $MaxScale) {
    $attemptPath = Join-Path $runRoot ("scale-{0}" -f $scale)
    New-Item -ItemType Directory -Path $attemptPath -Force | Out-Null
    $writeDetailed = $scale -eq 4096
    $reportPath = if ($writeDetailed) {
        Join-Path $attemptPath 'report-detailed.json'
    } else {
        Join-Path $attemptPath 'report.json'
    }

    $result = Invoke-PerformanceAttempt `
        -Scale $scale `
        -Detailed $writeDetailed `
        -OutputPath $reportPath
    $status = $result.Status
    $report = $result.Report

    if ($status -eq 'passed' -and -not $writeDetailed -and
        $null -ne $report -and
        ($report.beltEntities -eq 4096 -or
         $report.itemEntities -eq 4096 -or
         $report.expectedBelts -eq 4096 -or
         $report.peakBeltEntities -eq 4096)) {
        $writeDetailed = $true
        $reportPath = Join-Path $attemptPath 'report-detailed.json'
        $result = Invoke-PerformanceAttempt `
            -Scale $scale `
            -Detailed $true `
            -OutputPath $reportPath
        $status = $result.Status
        $report = $result.Report
    }

    if ($status -eq 'failed' -and -not $writeDetailed) {
        $writeDetailed = $true
        $reportPath = Join-Path $attemptPath 'report-detailed.json'
        $result = Invoke-PerformanceAttempt `
            -Scale $scale `
            -Detailed $true `
            -OutputPath $reportPath
        $status = $result.Status
        $report = $result.Report
    }

    $attempt = [pscustomobject]@{
        scale = $scale
        scaleUnit = if ($null -ne $report.scaleUnit) { $report.scaleUnit } else { '' }
        status = $status
        fixedTicksPerSecond = if ($null -ne $report) {
            [double]$report.fixedTicksPerSecond
        } else {
            0
        }
        framesPerSecond = if ($null -ne $report) {
            [double]$report.framesPerSecond
        } else {
            0
        }
        frameTimeMeanMilliseconds = if ($null -ne $report) {
            [double]$report.frameTimeMeanMilliseconds
        } else {
            0
        }
        frameTimeP95Milliseconds = if ($null -ne $report) {
            [double]$report.frameTimeP95Milliseconds
        } else {
            0
        }
        beltEntities = if ($null -ne $report) {
            [int]$report.beltEntities
        } else {
            0
        }
        itemEntities = if ($null -ne $report) {
            [int]$report.itemEntities
        } else {
            0
        }
        reportPath = $reportPath
    }
    $attempts += $attempt

    if ($status -eq 'passed') {
        $lastPassingScale = $scale
    } elseif ($status -eq 'failed') {
        $limitFound = $true
        $firstFailingScale = $scale
        $thresholdReportPath = $reportPath
        break
    } else {
        $aborted = $true
        break
    }

    if ($scale -ge $MaxScale) {
        $noLimit = $true
        break
    }
    if ($scale -gt [int]::MaxValue / $Multiplier) {
        $noLimit = $true
        break
    }
    $scale *= $Multiplier
}

if (-not $limitFound -and -not $noLimit -and -not $aborted) {
    $noLimit = $true
}

$summary = [pscustomobject]@{
    runId = $runId
    scene = $Scene
    loadPercent = $LoadPercent
    warmupSeconds = $WarmupSeconds
    sampleSeconds = $SampleSeconds
    timeDelaySeconds = $TimeDelaySeconds
    dragSeconds = $DragSeconds
    demolitionOrder = $DemolitionOrder
    startScale = $StartScale
    maxScale = $MaxScale
    multiplier = $Multiplier
    tpsThreshold = $TpsThreshold
    fpsThreshold = $FpsThreshold
    thresholdMode = $ThresholdMode
    attempts = $attempts
    result = [pscustomobject]@{
        foundLimit = $limitFound
        noLimit = $noLimit
        aborted = $aborted
        lastPassingScale = $lastPassingScale
        firstFailingScale = $firstFailingScale
        thresholdReportPath = $thresholdReportPath
    }
}

$summaryPath = Join-Path $runRoot 'stress-summary.json'
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
$attempts | Export-Csv -LiteralPath (Join-Path $runRoot 'attempts.csv') -NoTypeInformation

if ($null -ne $thresholdReportPath) {
    Write-Host "[factory-stress] LIMIT FOUND: first failing scale=$firstFailingScale, last passing scale=$lastPassingScale"
    Write-Host "[factory-stress] Threshold report: $thresholdReportPath"
} else {
    Write-Host "[factory-stress] No limit found up to scale=$MaxScale"
}
Write-Host "[factory-stress] Summary: $summaryPath"

[pscustomobject]@{
    RunRoot = $runRoot
    SummaryPath = $summaryPath
    LimitFound = $limitFound
    LastPassingScale = $lastPassingScale
    FirstFailingScale = $firstFailingScale
}
