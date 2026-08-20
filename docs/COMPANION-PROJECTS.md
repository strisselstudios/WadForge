# TrenchBroom Companion project files

Phase 1 introduces a small persistent project model for TrenchBroom Companion.

## File extension

Companion projects use:

`*.tbproject`

The project file is JSON and is intended to live at the root of the user's mapping project or mod workspace.

## Schema version 1

The first schema stores only stable project-level information:

- Project ID
- Project name
- Game ID
- Optional mod name
- Creation/update timestamps
- Registered `.map` files
- Active map

It deliberately does not contain unfinished BSP2Map, Mesh2Map, TexVar, skybox, compiler, or future module configuration.

Those systems will receive their own project fields only after their standalone behavior and integration requirements are defined.

## Map paths

Map paths inside a `.tbproject` are relative to the directory containing the project file.

Example:

```json
{
  "schemaVersion": 1,
  "projectId": "00000000-0000-0000-0000-000000000000",
  "name": "Castle Campaign",
  "gameId": "dusk",
  "modName": "Castle Campaign",
  "activeMapPath": "maps/level01.map",
  "maps": [
    {
      "path": "maps/level01.map",
      "displayName": "level01"
    }
  ]
}
```

Relative paths make a project movable and prevent the manifest from depending on a specific drive letter or user account.

## Persistence rules

`CompanionProjectStore`:

- validates schema version and required fields;
- rejects absolute or escaping map paths;
- rejects duplicate map entries;
- stores canonical forward-slash relative paths;
- writes through a temporary file and atomically replaces the target project file;
- can resolve stored map paths back to full filesystem paths.

Phase 1.1 changes only Companion Core. It does not change the existing Companion UI or existing WadForge/TrenchBroom behavior.
