# WadForge modifications to TrenchBroom

- Upstream project: TrenchBroom
- Pinned version: v2026.1
- Pinned commit: b8c14a93c6945a389c56ff7bf77e869c16f24895

The bundled compatible build is modified from upstream.

Modifications include:

- A separate material display-name property.
- Loading WadForge aliases from `<wad>.wadforge.json`.
- Display-name labels in the material browser.
- Display-name filtering and sorting.
- Copy Display Name and Copy Internal WAD Name commands.
- Preservation of the original safe internal identifier for lookup and MAP serialization.
- Filename fallback when TrenchBroom's logical material name includes path components.

The source patch is:

```text
patches/TrenchBroom-v2026.1-WadForge.patch
```

This is an independent modification and is not an official TrenchBroom release.
