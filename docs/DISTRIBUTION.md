# Distribution

## Release assets

Each release should provide:

```text
WadForge-TrenchBroom-Suite-vX.Y.Z.zip
WadForge-TrenchBroom-Source-vX.Y.Z.zip
SHA256SUMS.txt
RELEASE-NOTES-vX.Y.Z.md
```

## Full suite

The full suite is the normal user download. It contains:

- WadForge.
- TrenchBroom Companion.
- WadForge-compatible TrenchBroom.
- Required TrenchBroom resources and support files.
- Compatibility marker.
- TrenchBroom patch.
- Licenses and third-party notices.
- SHA-256 checksums.

## WadForge-only use

WadForge itself does not require TrenchBroom when the user only needs WAD creation or extraction.

## Long-name integration

Long material names require the WadForge-compatible TrenchBroom build. An official unmodified TrenchBroom installation can load the WAD but will display its short internal identifiers.

The alias JSON must be beside the exact WAD path that the map or game configuration loads:

```text
Example.wad
Example.wad.wadforge.json
```

Copying a WAD to another directory without its JSON removes the display aliases at that new location.

## Corresponding source

The source release must include:

- The exact WadForge repository source for the release tag.
- The complete patched TrenchBroom source used to build the bundled executable.
- TrenchBroom license and modification notice.
- The applied patch.
- Reproducible build instructions.

## Release files are not normal Git source

Ready-to-run ZIP files, executables, logs, and build directories belong under GitHub Releases. They should not be committed into the repository's ordinary source history.
