---
name: unity-launch
description: Open or run the Factory-with-DOTS Unity project on Windows, including batch compilation, Play Mode workflows, and executeMethod tasks. Discover the required Editor version and stop if any Unity Editor is already running. Use factory-performance for performance captures.
---

# Unity Editor launch

This is a project skill for Factory-with-DOTS. Resolve the project root relative to this
SKILL.md (`../../..`); the current checkout is D:/UnityProject/Factory-with-DOTS.
Use it for this checkout. Performance scenarios are defined by the sibling
[factory-performance skill](../factory-performance/SKILL.md).

## Existing Editor is a stop condition

Before starting a Unity task, check `Get-Process -Name Unity -ErrorAction SilentlyContinue`.
If any Unity Editor exists, stop this Unity task and tell the user to close the Editor before
continuing. This applies to the same project and other projects. Do not launch another Editor,
attach/reuse the existing instance, close it, or stop its Licensing Client. Do not modify the
project, trigger imports, or start builds while waiting. Explicitly requested skill maintenance
itself does not require launching Unity and may continue.

Report process IDs only when useful. Never print full process command lines or raw Hub logs:
they may contain access tokens. No command-line inspection is needed for this global stop rule.
The bundled [Run-UnityBatch.ps1](scripts/Run-UnityBatch.ps1) enforces the stop condition before
writing logs and again before launch. A blocked result is not permission to bypass the script.

## Resolve the project and Editor

- Resolve this checkout's root as above, verify Assets, Packages, and
  ProjectSettings/ProjectVersion.txt, and pass that root explicitly to the runner.
- Read m_EditorVersion and use that exact Editor. The script discovers installations under
  D:/Application/Unity and standard Unity Hub paths, or accepts -UnityExe.
- Follow the active session's permissions. With full access and no approvals, do not request
  escalation or pass forbidden permission flags. In restricted sessions, follow the actual
  tool's approval flow only when a real access failure requires it.
- Locate helpers relative to this SKILL.md. Use the available shell tool, currently
  exec_command; poll returned sessions with write_stdin rather than relaunching.

## Choose the launch method

For batch compilation, Play Mode, and executeMethod tasks, use the guarded batch wrapper below
by default. For interactive opening, launch the resolved Editor directly. Do not require Unity
CLI discovery or a CLI attempt before either workflow.

Use the official Unity CLI (`unity`) when the user requests it or a specific task benefits from
its capabilities. Apply the existing-Editor stop check and exact project-version resolution
before any CLI launch.
Resolve the CLI with `Get-Command unity`; if the current shell has a stale PATH, also check
`$env:LOCALAPPDATA\Unity\bin\unity.exe`. Check `--version` and the relevant command's `--help`
before choosing arguments; do not confuse the CLI binary with the Editor's `Unity.exe`.

For opening a project, use the explicit project root and resolved Editor path:

```powershell
& '<resolved-unity-cli>' open '<absolute-project-path>' `
  --editor-path '<resolved-editor-Unity.exe>' --non-interactive
```

Use CLI commands for compilation, Play Mode, or executeMethod workflows when the installed
CLI supports the required operation and preserves the task's timeout, logs, window visibility,
and owned-process cleanup. Inspect its help rather than assuming command names or flags.
Only connect to an Editor started by the current task; CLI usage does not permit attaching
to an Editor that was already open. For Pipeline commands, always pass the explicit
`--project-path`; do not install or change project packages merely to select a launch method.

If a chosen CLI workflow is unavailable or cannot meet the task's requirements, use the guarded
batch wrapper or direct Editor launch below when it still satisfies the user's request. Do not retry
after a CLI launch failure until you have checked whether it already started an Editor.

## Batch execution

The wrapper uses a hard timeout (default 240 seconds), hidden process startup, properly quoted
Windows arguments, and project-local logs. Licensing IPC comes from an explicit parameter,
the newest usable Hub launch record, or the current Windows username. Never output Hub credentials.

```powershell
& '<skill-dir>/scripts/Run-UnityBatch.ps1' `
  -ProjectPath '<this-project-root>' `
  -TimeoutSeconds 240 `
  -ExtraArgs @('-nographics', '-quit')
```

Choose exit behavior for the actual task:

- Compile/import-only: -quit is appropriate; inspect compilation errors and successful exit.
- Synchronous executeMethod: use -quit only when the method returns after all work completes.
- Asynchronous Play Mode/capture: omit -quit if the pipeline calls EditorApplication.Exit itself;
  inspect its completion marker and expected artifacts. Reuse the project's existing runner.

For interactive opening, use Start-Process with the resolved Editor executable and explicit
-projectPath after performing the same process check and version resolution.
Use -WindowStyle Hidden unless the user explicitly requests a visible Editor; preserve this
visibility requirement when choosing a CLI launch as well.
Do not use the timeout-based batch wrapper for a persistent interactive session.

## Verification and cleanup

Check ExitCode, TimedOut, task-specific log markers/errors and expected outputs. A zero exit code
alone is insufficient when a method may return early. Factory ECS markers do not apply to other projects.

If Unity compiles but dotnet reports missing source types, regenerate ignored csproj/solution files
through the installed IDE integration, then rerun the original build. With the Rider package, the
CLI entry is Packages.Rider.Editor.RiderScriptEditor.SyncSolution; verify generated Compile entries.
Close any running Editor first. Do not alter gameplay code to repair stale project files.

On timeout/interruption, terminate only the batch Editor owned by this invocation. Never kill
Licensing Clients by name or installation path; leave shared licensing processes untouched.
Only clean up auxiliary processes when ownership by this invocation is proven.

For launcher maintenance, run scripts/Test-UnityBatch.ps1. It mocks all process launches and
checks existing-Editor/race blocking, Windows argument round-trips, timeout and exception cleanup.
It must not be replaced with a real Editor launch while the user has an Editor open.

If the required Editor is missing, licensing fails, or an Editor opens before launch, stop and report
that condition. Never retry by killing user processes or blindly changing permissions.
