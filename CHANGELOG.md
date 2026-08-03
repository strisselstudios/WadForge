# Changelog

## 1.0.1 - 2026-08-02

- Removed development-section and implementation-status notes from the WadForge interface.
- Replaced the remaining mode-selection development message with a neutral ready status.
- Rebuilt the standalone WadForge and complete-suite packages from the corrected source.
- No TrenchBroom Companion or TrenchBroom behavior changed in this hotfix.

## 1.0.0

### WadForge

- Added batch image-to-WAD2 conversion.
- Added batch image-to-WAD3 conversion.
- Added WAD2 and WAD3 texture extraction to PNG.
- Added drag-and-drop queue support.
- Added deterministic internal WAD texture identifiers.
- Added external JSON alias manifests for long display names.
- Added preservation of original and stored texture dimensions.
- Added branded application icon and final user-facing title.

### TrenchBroom Companion

- Added WAD and alias-manifest validation.
- Added compatible TrenchBroom build detection.
- Added launch support for selected TrenchBroom installations.
- Added drag-and-drop support.
- Added branded application icon and final user-facing title.

### WadForge-compatible TrenchBroom

- Added WadForge alias-manifest loading.
- Added long display names in the material browser.
- Added display-name filtering and sorting.
- Added separate display-name and internal-name copy commands.
- Preserved safe internal identifiers in WAD and MAP data.
