# Phase 11B-1 Dial Runtime Analysis

- Source save: `C:/Users/Evan/Documents/GitHub/xwing-unified-1e/source/unified-2.5/2486128992.json`
- Objects inspected: 1768
- Dial objects found: 2
- Unique dial meshes: 1
- Unique colliders: 1
- Unique faction skins: 1

## Runtime conclusion

The physical dial is identified by its dial mesh/collider. Its `DiffuseURL` is the generic faction skin. Ship identity, manoeuvre selection and action controls are supplied by Lua/UI state rather than by the physical mesh texture.

## Faction skins

- `https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/skin/1/Empire.png`

## Detected action terms

- barrel roll
- boost
- calculate
- cloak
- evade
- focus
- jam
- lock
- reinforce
- target lock

## Detected manoeuvre terms

- bank
- reverse
- straight
- turn

## Dial objects

### 01 — Unassigned Dial (`e8e6c0`)

- JSON path: `ObjectStates[2]/ContainedObjects[83]`
- Mesh: `https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/dialmodel.obj`
- Collider: `https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/dialcollider.obj`
- Faction skin: `https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/skin/1/Empire.png`
- Lua characters: 53258
- XML characters: 65369
- Lua state characters: 0
- Extracted Lua: `01-unassigned-dial-e8e6c0.lua`
- Extracted XML: `01-unassigned-dial-e8e6c0.xml`

### 02 — Unassigned Dial (`e8e6c0`)

- JSON path: `ObjectStates[156]/ContainedObjects[79]`
- Mesh: `https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/dialmodel.obj`
- Collider: `https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/dialcollider.obj`
- Faction skin: `https://raw.githubusercontent.com/JohnnyCheese/TTS_X-Wing2.0/master/assets/dial/skin/1/Empire.png`
- Lua characters: 53990
- XML characters: 65369
- Lua state characters: 0
- Extracted Lua: `02-unassigned-dial-e8e6c0.lua`
- Extracted XML: `02-unassigned-dial-e8e6c0.xml`

