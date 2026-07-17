# Architecture

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

## Semantic Repository

The repository is the authoritative source for ships, pilots, upgrades
and edition metadata.

## Hybrid Ship Definitions

Combine: - First Edition assets - Unified spawning metadata - Canonical
mappings

## Spawner Definitions

Extracted from Unified's Lua implementation and translated into First
Edition compatible construction recipes.

## Object Builder

Produces fully playable Tabletop Simulator objects.

## Complete Save Builder

Assembles the generated objects into a complete Workshop save.
