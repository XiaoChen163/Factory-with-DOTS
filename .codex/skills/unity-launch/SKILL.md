---
name: unity-launch
description: Launch and control the Factory-with-DOTS Unity project (Unity 6000.3.19f1) from a shell/Codex session: batch-mode Editor runs, Play Mode, ECS performance captures, and -executeMethod workflows. Use this skill whenever the user asks to open, launch, run, or profile Unity, enter Play Mode, run a performance or batch scene, or invoke Unity CLI commands for this project, even if they don't say "skill" or "Unity".
---

# Unity Launch & Performance Run (Factory-with-DOTS)

## Why this skill exists

A direct `Unity.exe` batch launch from a fresh agent session fails in three predictable ways. Each one wastes a lot of time if rediscovered by trial and error:

1. **Infinite wait.** Unity's Licensing Client retries for 60+ seconds when it cannot initialize and does not give up on its own. A bare synchronous call can hang a session forever.
2. **Sandbox denial.** The licensing client and Unity Hub must write to `AppData` and license caches. The default Codex sandbox blocks those writes (`EPERM`, refused channel), so licensing never completes.
3. **Missing or hardcoded licensing parameters.** The Editor needs Hub IPC arguments and a licensing channel. The channel is not portable across machines: Hub can use `LicenseClient-<username>` or a device-specific token. Never hardcode it; discover it at runtime.

The project already ships a complete batch performance pipeline. Reuse it instead of writing a new profiler or launch harness.

## Machine facts

Defaults on this machine, all overridable via script parameters:

- Project (`-ProjectPath`): `D:\UnityProject\Factory-with-DOTS`
- Editor (`-UnityExe`): `D:\Application\Unity\6000.3.19f1\Editor\Unity.exe`
- Editor version in `ProjectSettings/ProjectVersion.txt`: `6000.3.19f1`
- Performance entry point: `FactoryPerformanceBatchRunner.Run` in the `Factory.Editor` assembly
- Performance scenes: `Assets/Scenes/Performance/Perf_*.unity`
- Default performance scenario: `Perf_4096_Mk4_FullLoop` with `-factoryPerformanceScale 64` (4096 belts)
- Licensing channel: discovered automatically; do not hardcode a username

## Cross-machine usage

This skill is not tied to one machine:

- Pass `-UnityExe` when the Editor lives elsewhere. Common locations are `C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe` and `D:\Application\Unity\<version>\Editor\Unity.exe`. Check `ProjectSettings\ProjectVersion.txt` and match the Editor version.
- Pass `-ProjectPath` when the project checkout is elsewhere.
- Resolve the skill's own `scripts\Run-UnityBatch.ps1` relative to the folder containing this `SKILL.md` (from the skill's `file:` locator); absolute paths in examples are only for this machine.
- Unity Hub must be installed, running, and signed in on the target machine so `-useHub -hubIPC` and the licensing client can work.

## Licensing channel discovery

The `-licensingIpc` value is machine-specific. Resolve it in this order:

1. Explicit `-LicensingIpc` parameter if the caller knows it.
2. Latest `LaunchProcess` entry in `%APPDATA%\UnityHub\logs\info-log.json` (also checks `info-log.json.1`), where Hub records the exact `-licensingIpc` it passes to Unity.
3. Fallback to `LicenseClient-<Windows username>` (`LicenseClient-$env:USERNAME`).

`scripts/Run-UnityBatch.ps1` implements this and reports the resolved channel in its output. If none of these work, ask the user to open the project once through Unity Hub, then re-read the Hub log to get the real channel.

## Launch rules

These rules map one-to-one to failures seen in real runs. Follow them for every Unity batch invocation.

1. **Escalate.** Invoke Unity through `shell_command` with `sandbox_permissions: "require_escalated"`. The Licensing Client must write outside the workspace; without escalation the Editor hangs in `[Licensing::Module] Timed-out after 60.00s, waiting for Licensing to initialize` loops. If you see channel refusals or `EPERM`, the usual cause is a missing escalation, not a wrong command.
2. **Cap the runtime.** Always enforce a hard timeout (default 240s) with `Start-Process` + a deadline loop + `Stop-Process`. Never call `Unity.exe` with a bare `&` and no timeout. The bundled `scripts/Run-UnityBatch.ps1` implements this.
3. **Use the Hub/licensing args, with a discovered channel.** Include `-batchmode -acceptSoftwareTermsForThisRunOnly -useHub -hubIPC -cloudEnvironment production -licensingIpc <discovered>`. Never paste a username-specific channel into the skill or commands.
4. **Do not pass `-quit` for the capture pipeline.** `FactoryPerformanceMetricsCapture` calls `EditorApplication.Exit(0)` itself when capture finishes. With `-quit`, the Editor exits before Play Mode and capture complete.
5. **Log to the workspace.** Always pass `-logFile <workspace path>` and check completion markers after the run; an exit code alone is not enough.

## Stress test policy

Default performance runs use fixed 4096-scale scenes only. Do not start a stress test
unless the user explicitly asks for one. When a stress test is requested, run
`Tools/FactoryStress/Run-FactoryStressTest.ps1` and store results under
`PerformanceReports/StressTest/`.

## Quick start

Replace `<skill-dir>` with the folder containing this `SKILL.md` (on this machine: `C:\Users\Xiao_Chen\.codex\skills\unity-launch`). Run the bundled script from an escalated `shell_command`; the licensing channel is resolved automatically:

```powershell
& '<skill-dir>\scripts\Run-UnityBatch.ps1' `
  -TimeoutSeconds 240 `
  -ExtraArgs @(
    '-executeMethod','FactoryPerformanceBatchRunner.Run',
    '-factoryPerformanceScene','Perf_4096_Mk4_FullLoop',
    '-factoryPerformanceScale','64',
    '-factoryPerformanceLoadPercent','100',
    '-factoryPerformanceCapture',
    '-factoryPerformanceWarmupSeconds','1',
    '-factoryPerformanceSampleSeconds','2',
    '-factoryPerformanceOutput','D:\UnityProject\Factory-with-DOTS\PerformanceReports\Default4096\performance-report.json'
  )
```

Give the shell command a tool timeout of at least `300000` ms so the wrapper does not abort before the script's own deadline. The script returns `ExitCode`, `TimedOut`, `LicensingIpc`, and `LogFile`.

If a machine needs an explicit channel, pass `-LicensingIpc 'LicenseClient-<value>'` to the script.

## Raw equivalent

When more control is needed than the script provides, use this equivalent pattern. Note the dynamic channel resolution:

```powershell
# Resolve the channel instead of hardcoding it.
$ipc = "LicenseClient-$env:USERNAME"
# Prefer the channel Hub actually used:
#   Select-String -Path "$env:APPDATA\UnityHub\logs\info-log.json" -Pattern "-licensingIpc',\s*'([^']+)'"

$exe = 'D:\Application\Unity\6000.3.19f1\Editor\Unity.exe'
$log = 'D:\UnityProject\Factory-with-DOTS\Logs\unity-batch.log'
$args = @(
  '-batchmode','-acceptSoftwareTermsForThisRunOnly',
  '-useHub','-hubIPC','-cloudEnvironment','production',
  '-licensingIpc',$ipc,
  '-projectPath','D:\UnityProject\Factory-with-DOTS',
  '-logFile',$log,
  '-executeMethod','FactoryPerformanceBatchRunner.Run',
  '-factoryPerformanceScene','Perf_4096_Mk4_FullLoop',
  '-factoryPerformanceScale','64',
  '-factoryPerformanceLoadPercent','100',
  '-factoryPerformanceCapture',
  '-factoryPerformanceWarmupSeconds','1',
  '-factoryPerformanceSampleSeconds','2',
  '-factoryPerformanceOutput','D:\UnityProject\Factory-with-DOTS\PerformanceReports\Default4096\performance-report.json'
)
$p = Start-Process -FilePath $exe -ArgumentList $args -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds(240)
while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5 }
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit() }
$p.ExitCode
```

## Verification

A successful performance run must show all of the following:

- Script result: `ExitCode 0`, `TimedOut False`, `LicensingIpc` resolved to a non-empty value
- Log lines, in order: `[ECS Performance] Opening batch scene: ...`, `[ECS Performance] Entering Play Mode for capture.`, `[ECS Performance] READY: ...`, `[ECS Performance] CAPTURE COMPLETE: ...`
- JSON report exists at `-factoryPerformanceOutput`, CSV beside it
- Report fields populated: `sampledFrames`, `frameTimeMeanMilliseconds`, `frameTimeP95Milliseconds`, `metrics`

## Performance pipeline details

| Argument | Purpose | Example |
| --- | --- | --- |
| `-factoryPerformanceScene` | Scene name under `Assets/Scenes/Performance` | `Perf_4096_Mk4_FullLoop` |
| `-factoryPerformanceScale` | Primary scale for all non-F16 parameterized scenes; `ScalableStraight` also accepts the legacy NodeCount argument | `64` |
| `-factoryPerformanceNodeCount` | Legacy alias for `-factoryPerformanceScale` on `ScalableStraight`; no hard upper limit | `4096` |
| `-factoryPerformanceLoadPercent` | Initial item occupancy percent | `100` |
| `-factoryPerformanceCapture` | Enables the Profiler capture component | flag |
| `-factoryPerformanceWarmupSeconds` | Warmup before sampling | `1` |
| `-factoryPerformanceSampleSeconds` | Sampling duration | `2` |
| `-factoryPerformanceTimeDelay` | Build stress: seconds between placements/removals | `0.25` |
| `-factoryPerformanceDragSeconds` | Build stress: drag duration from start to end | `0.25` |
| `-factoryPerformanceDemolitionOrder` | Build stress: `0` forward, `1` reverse | `0` |
| `-factoryPerformanceOutput` | JSON report path (CSV is derived) | workspace path |
| `-factoryPerformanceCompact` | Write compact JSON summary without per-frame CSV | flag |

Supported scenes: `Perf_4096_Mk4_HalfLoaded`, `Perf_F16_Mk4_1024Items`, `Perf_4096_Mk4_Blocking`, `Perf_Straight_Scalable`, `Perf_512_MixedJunction`, `Perf_4096_Mk4_FullLoop`, `Perf_ProducerConsumer`, `Perf_ContinuousBeltBuild`.

Reports contain frame time statistics, managed/GC/system memory, ECS system markers (`BeltTransferSystem`, `BeltProgressSystem`, `ItemProcessSystem`, ...), fixed tick rate, and transfer counters. See `references/project-map.md` for the full script/scene map and report schema.

## Stress test

Run only when the user explicitly requests a stress test. Results are written to
`PerformanceReports/StressTest/`:

```powershell
& 'D:\UnityProject\Factory-with-DOTS\Tools\FactoryStress\Run-FactoryStressTest.ps1' `
  -Scene Perf_4096_Mk4_FullLoop `
  -StartScale 2 `
  -MaxScale 128 `
  -TpsThreshold 50
```

## Failure diagnosis

| Symptom | Cause | Fix |
| --- | --- | --- |
| `Timed-out after 60.00s, waiting for Licensing to initialize` or `Connection to channel ... refused` | not escalated, licensing client unavailable, or wrong channel | rerun with escalation; check Hub is running; let the script discover the channel or pass `-LicensingIpc` explicitly |
| Hub CLI `EPERM` on `user-settings.json.tmp-*` | sandbox blocking AppData writes | use escalated shell |
| `TimedOut=True`, exit `-1` | script hit the hard timeout | read log tail, fix blocker, rerun |
| `error CS...` in log | C# compile failure | fix code before rerunning |
| `Performance scene not found` | wrong scene name | list `Assets/Scenes/Performance` |
| Editor not found or wrong version | default `-UnityExe` does not exist on this machine | locate the Editor matching `ProjectVersion.txt` and pass `-UnityExe` |
| another `Unity.exe` holds the Library | stale process or user's open Editor | stop only test-spawned processes; ask the user before killing their Editor |

## Cleanup

After each run, stop leftover test-spawned processes: `Unity.exe` and the editor-bundled `Unity.Licensing.Client.exe` (path under the active Editor install). Do not stop the Hub-owned `Unity.Licensing.Client.exe` under the Unity Hub install directory; that is the user's normal session. `Run-UnityBatch.ps1` already performs this cleanup.
