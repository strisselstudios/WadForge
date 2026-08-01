# Long-name aliases

## Why aliases exist

Classic WAD directory entries provide a 16-byte name field. WadForge therefore separates a material's user-facing display name from its safe internal WAD identifier.

Example:

```text
Display name:
bamboo_fence_gate_closed

Internal WAD identifier:
bamboo_fenc_2EEE
```

## What is stored where

The WAD stores the internal identifier.

The matching sidecar JSON stores:

- Full display name.
- Internal name.
- Original source name.
- Original dimensions.
- Stored dimensions.
- WAD filename.
- WAD SHA-256.

## Required file placement

The pair must remain together:

```text
MinecraftBlocks.wad
MinecraftBlocks.wad.wadforge.json
```

The JSON is resolved from the exact WAD path loaded by TrenchBroom. A JSON beside a different copy of the same WAD is not used.

## TrenchBroom behavior

The compatible TrenchBroom build:

- Displays long aliases in the material browser.
- Filters and sorts using display names.
- Provides display-name and internal-name copy commands.
- Continues using the internal identifier for texture lookup and MAP serialization.

Official unmodified TrenchBroom shows only the internal identifier.

## Portability

When copying, moving, sharing, or archiving a WAD, copy its `.wadforge.json` file with it.
