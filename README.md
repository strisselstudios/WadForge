# WadForge
WadForge is a Windows desktop texture utility for creating and extracting Quake-family WAD texture archives (WAD2/WAD3). This repository also contains TrenchBroom Companion and the source patch used by the WadForge-compatible TrenchBroom build.


Download options and instructions

<img width="1095" height="333" alt="Download Options" src="https://github.com/user-attachments/assets/2ca7f00f-dd14-4846-85c1-4f5d573cd215" />

[WadForge info/instructions](https://github.com/strisselstudios/WadForge/wiki/WadForge-info-and-instructions)

Explains how to download, extract, configure, and use WadForge by itself.

[Full TrenchBroom Companion App + WadForge info/instructions](https://github.com/strisselstudios/WadForge/wiki/Full-TrenchBroom-Companion-App---WadForge-info-and-instructions)

Explains how to download, extract, configure, and use WadForge, TrenchBroom Companion, and the included compatible TrenchBroom build.

## Main features

- Batch PNG, JPG, BMP, and supported image conversion to WAD2 or WAD3.
- Batch WAD2 and WAD3 texture extraction to PNG.
- Drag-and-drop queues and selectable output folders.
- Deterministic internal WAD texture identifiers limited to 16 characters.
- External JSON alias manifests that preserve full display names.
- Original and stored texture dimension metadata.
- Companion validation and launching for WadForge-compatible TrenchBroom builds.

## Long texture names

WAD directory identifiers are limited to 16 characters. WadForge stores the full display names in a sidecar manifest.

Keep each manifest beside the exact WAD that TrenchBroom loads:

`
ExampleLongWadName.wad
ExampleLongWadName.wad.wadforge.json
`
The WadForge-compatible TrenchBroom build displays the long alias in its material browser while preserving the short internal identifier in WAD and MAP data. Official unmodified TrenchBroom builds display only the internal identifier.

## Downloads

Ready-to-run packages are published under the repository's Releases page:

https://github.com/strisselstudios/WadForge/releases

Choose one of these packages:

- WadForge-Only-Windows-x64-v1.0.1.zip - WadForge only, for image/WAD conversion and extraction.
- WadForge-TrenchBroom-Suite-v1.0.1.zip - complete suite with WadForge, TrenchBroom Companion, and the compatible TrenchBroom build.
- WadForge-TrenchBroom-Source-v1.0.1.zip - corresponding source package.

Use the complete suite when long texture names must appear inside TrenchBroom.
## Basic use

1. Extract the complete release ZIP.
2. Run `WadForge\WadForge.exe`.
3. Add images or WAD files to the queue.
4. Select the conversion direction and WAD format.
5. Select an output directory.
6. Keep generated `.wadforge.json` manifests beside their matching WAD files.
7. Use the included compatible TrenchBroom build when long display names are required.

## Source layout

- `src/` - WadForge and Companion .NET source.
- `patches/` - patch for pinned TrenchBroom source.
- `scripts/` - reproducible local build and release scripts.
- `docs/` - build, distribution, alias, and troubleshooting documentation.
- `licenses/` - GPL and third-party licensing material.
- `docs/PUBLISHING.md` - manual GitHub repository and release publication steps.

## Build baseline

The .NET projects target .NET 9 and pin SDK `9.0.316` through `global.json`.

## Pinned TrenchBroom baseline

- Upstream version: `v2026.1`
- Upstream commit: `b8c14a93c6945a389c56ff7bf77e869c16f24895`

## Independent project notice

WadForge and TrenchBroom Companion are independent community projects. The modified TrenchBroom build is not an official TrenchBroom release.

## License

This repository is distributed under the GNU General Public License version 3. TrenchBroom remains subject to its own copyright notices and GPLv3 license. See `LICENSE`, `licenses/`, and `THIRD-PARTY-NOTICES.md`.
