# Troubleshooting

## Long names do not appear

Verify all of the following:

1. The compatible TrenchBroom build is running.
2. The exact loaded WAD has a matching sidecar beside it.
3. The filenames follow this pattern:

   ```text
   Example.wad
   Example.wad.wadforge.json
   ```

4. The manifest WAD SHA-256 matches the WAD.
5. The map or game configuration is not loading a second copy from another directory.
6. TrenchBroom was restarted or the WAD collection was reloaded after the JSON was added.

## Only short names appear

Short values such as `bamboo_fenc_2EEE` are valid internal WAD identifiers. They appear when the alias manifest is missing, mismatched, or used with official unmodified TrenchBroom.

## The Companion rejects TrenchBroom

Select the WadForge-compatible TrenchBroom installation. The bundled build includes `wadforge-companion-build.json`, which records compatibility and executable hashes.

## WAD hash mismatch

Do not manually edit the WAD after its JSON is generated. Regenerate the WAD and manifest together, or copy the matching pair from the original output location.

## Output permission problems

Select a writable local output directory. Avoid protected Windows directories and partially synchronized cloud-storage paths.

## WAD2 and WAD3 differences

WAD2 uses indexed Quake-style texture data and palette conversion. WAD3 supports embedded palettes for mip textures. Conversion back to PNG reconstructs pixels from the data available in the archive; it cannot restore source information that the WAD format did not retain.

## Missing JSON after copying a WAD

Copy the `.wadforge.json` file manually beside the copied WAD. The JSON is a required part of the long-name feature.
