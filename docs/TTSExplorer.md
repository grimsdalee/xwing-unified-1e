# TTS Explorer

A small Windows desktop tool for inspecting Tabletop Simulator save/workshop JSON files.

## Requirements

- .NET 8 SDK

Install from Microsoft if needed:
https://dotnet.microsoft.com/en-us/download/dotnet/8.0

## Build

From the repository root:

```cmd
cd tools\TtsExplorer
dotnet build
```

## Run

```cmd
dotnet run
```

Then open one of:

```text
source\unified-2.5\2486128992.json
source\legacy-1e\3302209318.json
```

## Features in v0.1

- Open TTS JSON files
- Summarise top-level structure
- Browse all objects, including contained objects
- Search by GUID, nickname, URL, or Lua text
- View object Lua
- View object XML UI
- View raw object JSON
- Export scripts and UI to normal files

## Why this exists

Unified 2.5 has a very large Global Lua script and hundreds of object scripts. Editing the JSON directly is impractical. This tool gives us a maintainable way to inspect the engine before converting it to 1E.
