# TrenchBroom Companion project lifecycle

Phase 1.2 adds the first lifecycle API above the Phase 1.1 `.tbproject` persistence layer.

## Responsibilities

`CompanionProjectManager` can:

- create an empty Companion project in a chosen directory;
- create a Companion project around an existing `.map` without moving the map;
- reopen an existing `.tbproject`;
- generate a safe `.tbproject` filename from the user-visible project name.

`CompanionProjectSession` represents an open project and can:

- expose the project file and project directory;
- add `.map` files that already live inside the project directory;
- switch the active registered map;
- resolve the active map to its full filesystem path;
- save project changes.

## Non-destructive existing-map behavior

Creating a Companion project around an existing map writes the `.tbproject` beside the existing `.map`.

The source map is not moved, renamed, or rewritten.

## Deliberate exclusions

Phase 1.2 does not yet:

- create a new TrenchBroom `.map` file;
- copy external maps into a managed project;
- create DUSK folder hierarchies;
- configure TrenchBroom;
- configure WADs;
- configure skyboxes;
- configure compilers;
- expose project management in the GUI;
- integrate BSP2Map, Mesh2Map, TexVar, or unfinished modules.

Those features are layered on only after the project lifecycle foundation is accepted.
