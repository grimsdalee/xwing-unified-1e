# UnifiedToolkit Build Agent

This local .NET 8 agent watches the UnifiedToolkit project and automatically
runs `dotnet build` after source changes settle.

It does not edit Toolkit source files.

## Installation

Open PowerShell in this folder and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass

.\Install-BuildAgent.ps1 `
    -RepositoryRoot "C:\Users\Evan\Documents\GitHub\xwing-unified-1e" `
    -StartNow
```

The installer:

1. Publishes the agent into:
   `tools\UnifiedToolkitBuildAgent\publish`
2. Writes the active `agentsettings.json`.
3. Registers a Windows Task Scheduler task named:
   `UnifiedToolkit Build Agent`
4. Optionally starts it immediately.

Administrator rights should not normally be required because the task runs
under the current user at sign-in.

## Generated reports

Current agent state:

```text
_unifiedtoolkit_reports\build-agent\build-agent-status.json
```

Latest build result:

```text
_unifiedtoolkit_reports\build-agent\build-agent-latest.json
```

Full logs:

```text
_unifiedtoolkit_reports\build-agent\logs
```

## Automatic builds

The agent watches the Toolkit folder for:

```text
.cs
.csproj
.json
.props
.targets
.resx
```

It ignores `bin`, `obj`, and its own report folders.

After changes stop for 2.5 seconds, it runs:

```text
dotnet build UnifiedToolkit.csproj --nologo --verbosity minimal
```

Set `"runCleanBeforeBuild": true` in `agentsettings.json` to run
`dotnet clean` first.

## Run once

```powershell
.\Run-BuildAgent-Once.ps1
```

## Stop and start

```powershell
.\Stop-BuildAgent.ps1
.\Start-BuildAgent.ps1
```

## Queue UnifiedToolkit commands

The queue allows a command to run without keeping a PowerShell window open.

Example:

```powershell
.\Queue-ToolkitCommand.ps1 `
    -Name "Build Epic targeting layouts" `
    -Arguments @(
        "build-epic-ship-targeting-layouts",
        "{RepositoryRoot}"
    )
```

Another example:

```powershell
.\Queue-ToolkitCommand.ps1 `
    -Name "Generate CR90 targeting texture" `
    -Arguments @(
        "generate-epic-ship-targeting-texture",
        "{RepositoryRoot}",
        "--ship",
        "cr90corvette"
    )
```

The placeholders supported in queued and post-build commands are:

```text
{RepositoryRoot}
{ToolkitProjectDirectory}
{ToolkitProjectFile}
```

The queue lives at:

```text
_unifiedtoolkit_agent\commands.json
```

The agent updates each command with completion time, success, exit code,
and log path.

## Commands after every successful build

Add commands to the `commandsAfterSuccessfulBuild` array in
`agentsettings.json`.

Example:

```json
{
  "name": "Build Epic targeting layouts",
  "enabled": true,
  "stopPipelineOnFailure": true,
  "timeoutMinutes": 10,
  "arguments": [
    "build-epic-ship-targeting-layouts",
    "{RepositoryRoot}"
  ]
}
```

Keep this array empty initially. Add only commands that are safe and useful
after every source edit.

## Uninstall

```powershell
.\Uninstall-BuildAgent.ps1
```

To remove the published executable too:

```powershell
.\Uninstall-BuildAgent.ps1 -RemovePublishedFiles
```

Build logs and reports are deliberately retained.
