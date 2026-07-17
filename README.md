# X-Wing Unified First Edition

## Project Goal

**X-Wing Unified First Edition** is an open-source project that
generates a complete **X-Wing Miniatures First Edition** Tabletop
Simulator workshop module using the Unified 2.0/2.5 project as its
technical foundation.

Rather than manually editing a TTS save, the project compiles a new
First Edition module from semantic game data, legacy First Edition
assets and Unified's spawning framework.

## Design Principles

-   First Edition gameplay and content
-   Unified spawning framework and object lifecycle
-   Legacy First Edition artwork and assets
-   Semantic Repository as the single source of truth
-   Fully generated workshop save

## Current Progress

  Area                  Status
  --------------------- --------
  Semantic Ships        50
  Semantic Pilots       191
  Semantic Upgrades     160
  Validation Errors     0
  Validation Warnings   0

## Architecture

``` text
Semantic Repository
        ↓
Hybrid Ship Definitions
        ↓
Spawner Definitions
        ↓
Object Builder
        ↓
Complete Save Builder
```

## Current Milestones

-   ✅ Repository analysis
-   ✅ Semantic conversion
-   ✅ Hybrid asset discovery
-   ✅ First Edition base conversion
-   ✅ Spawner reverse engineering
-   ⬜ Object Builder
-   ⬜ Save Builder
-   ⬜ Gameplay rules conversion
-   ⬜ Workshop release

## Repository Layout

``` text
docs/
tools/UnifiedToolkit/
source/
ConversionData/
```

## License

See the repository license for details.
