---
name: factory-performance
description: Run and interpret ECS performance captures and explicitly requested stress tests for the Factory-with-DOTS project at D:/UnityProject/Factory-with-DOTS. Use its existing performance scenes, batch runner, and JSON/CSV reports; do not apply to other Unity projects.
---

# Factory-with-DOTS performance

This is a project-only skill. Resolve the project root from this skill's location; the current
checkout is D:/UnityProject/Factory-with-DOTS. Never route another project's profiling to these scenes.

## Editor preflight

Before modifying the project or running a capture, check Get-Process -Name Unity.
If any Unity Editor is running, stop and tell the user to close it before continuing.
Do not attach, reuse, start another Editor, or close the existing Editor or its licensing processes.
The global unity-launch skill defines version discovery, permissions and bounded process ownership.
Read its SKILL.md from CODEX_HOME/skills/unity-launch (or ~/.codex/skills/unity-launch).
This project's scripts/Run-UnityBatch.ps1 forwards to that one guarded runner; it does not duplicate it.

## Select the measurement

Follow the current policy in Assets/Scenes/Performance/README.md. Default runs use a fixed
4096-scale scenario: Perf_4096_Mk4_FullLoop, scale 64, load 100 percent. Smaller diagnostic runs
are allowed when specifically requested. Stress matrices require an explicit user request;
use Tools/FactoryStress/Run-FactoryStressTest.ps1 with finite bounds and outputs under
PerformanceReports/StressTest. Check that no Editor is running before invoking that legacy tool,
and before each new attempt; never bypass the global stop rule.

Reuse FactoryPerformanceBatchRunner.Run in Factory.Editor; it opens the selected performance
scene, schedules Play Mode, and the runtime capture exits the Editor. Do not pass -quit for this
asynchronous pipeline. Capture steady-state samples only after the READY marker.

```powershell
& '<this-skill-dir>/scripts/Run-UnityBatch.ps1' `
  -TimeoutSeconds 240 `
  -ExtraArgs @(
    '-nographics',
    '-executeMethod', 'FactoryPerformanceBatchRunner.Run',
    '-factoryPerformanceScene', 'Perf_4096_Mk4_FullLoop',
    '-factoryPerformanceScale', '64',
    '-factoryPerformanceLoadPercent', '100',
    '-factoryPerformanceCapture',
    '-factoryPerformanceWarmupSeconds', '5',
    '-factoryPerformanceSampleSeconds', '10',
    '-factoryPerformanceOutput', '<project-root>/PerformanceReports/fixed-4096.json'
  )
```

Use graphics when the requested measurement includes rendering. A headless result measures
simulation/CPU work and is not a rendering benchmark. Use distinct output paths for comparisons.
The project version currently is 6000.3.19f1; the runner reads ProjectVersion.txt rather than freezing it.

## Validate and report

Require ExitCode 0, TimedOut false, then these log stages in order:
Opening batch scene, Entering Play Mode for capture, READY, CAPTURE COMPLETE.
Require the requested JSON and nonempty sampledFrames/metrics; CSV is expected unless
-factoryPerformanceCompact was requested. Confirm scenario, scale/load, and sample duration
match the request, and only compare like-for-like runs.

Report mean and P95 frame time, fixed ticks/second, accepted transfers/second and relevant ECS
markers. State scene, scale, load, sampling duration, headless/graphics mode and artifact paths.
Do not interpret historical timings as current measurements.

Read [references/project-map.md](references/project-map.md) for source paths, report fields and
scene inventory. Read the scene README or actual argument parser for scene-specific scale rules,
continuous build parameters, demolition order and stress thresholds; do not invent defaults.
