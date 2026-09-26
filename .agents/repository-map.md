# Kern Repository Map

This file describes the current tree. Assembly boundaries are defined by
`.asmdef` and `.asmref` files, not by a guessed directory hierarchy. Update this
map when a production root or assembly boundary changes.

## Repository roots

| Path | Purpose | Placement rule |
| --- | --- | --- |
| `Assets/` | Unity-authored game code, assets, scenes, and runtime resources | Keep Unity GUIDs by moving each asset together with its `.meta` file. Never edit serialized Unity assets as text. |
| `Packages/` | Embedded Unity packages, including Effekseer | Keep vendor package code and assets inside their package; do not mix game code into it. |
| `ProjectSettings/` | Unity project configuration | Change only for a requested project-setting change; avoid mass-formatting. |
| `KernAudio/` | FMOD Studio project, metadata, source audio, and built banks | Follow the `fmod-sync` skill when synchronizing banks. |
| `tools/` | Standalone .NET tools, harnesses, and test projects | Keep generated `bin/` and `obj/` out of source. |
| `scripts/` | Small project automation and asset-generation entry points | Use for thin scripts; put substantial tools and their code in `tools/`. |
| `visual/` | Independent UI lab and visual generators | This is not production UI unless a production integration is explicit. |
| `docs/` | Architecture, planning, operations, design, and research documents | Put new project documents in the matching subdirectory, not at repository root. |
| `.agents/` | Agent guidance, skills, and repository context | Never put runtime code here. |

Root-level files are reserved for repository-wide entry points and configuration:
`README.md`, `LICENSE`, `AGENTS.md`, solution/build configuration, and tool
configuration. Project plans, handoffs, inventories, and design notes belong in
`docs/`.

## Unity source and assembly boundaries

All project C# source is under `Assets/Scripts/`. The current project assembly
definitions are:

| Assembly | Definition location | Responsibility |
| --- | --- | --- |
| `Kern.Contracts` | `Core/Interfaces/Contracts` | Lowest-level shared contracts and data types |
| `Kern.Core` | `Core` | Shared runtime services, configuration, lifecycle, and rendering services |
| `Kern.Bootstrap` | `Core/Bootstrap` | Scene scopes, startup, and scene transitions |
| `Kern.AssetPipeline` | `AssetPipeline` | Runtime asset loading, caching, and animation decoding |
| `Kern.Infrastructure` | `Audio` | Audio and Effekseer infrastructure adapters |
| `Kern.Application` | `Game` | Game and player application behavior |
| `Kern.Networking` | `Networking` | Transport, packet processing, and network-facing models |
| `Kern.Presentation` | `Rendering` | Rendering and post-processing presentation code |
| `Kern.UI` | `UI` | UI Toolkit presentation and controllers |
| `Kern.World` | `World` | World, terrain, lighting, streaming, and storage integration |
| `Kern.Persistence` | `World/Persistence` | World persistence implementation |
| `Kern.Editor` | `Editor` | Editor-only tools and validation |
| `Kern.Tests.Editor` | `Tests` | Unity editor tests |
| `Kern.Tests.PlayMode` | `Tests/PlayMode` | Unity play mode tests |
| `Kern.Tests.Shared` | `Tests/Shared` | Shared test support |

`Assets/Scripts/VContainer` contains vendored VContainer assemblies. Other
third-party code belongs under `Assets/Plugins/` or `Packages/`, according to
how that dependency is integrated. Folder names do not grant assembly ownership;
check the nearest `.asmdef` or `.asmref` before moving C# files.

## Asset placement rules

- `Assets/Resources/` is reserved for assets that production code intentionally
  loads by runtime path with `Resources.Load`. Keep path-based groups distinct
  (`UI/`, `Shaders/`, `Localization/`, and similar); add or change a resource
  path constant/registry when renaming such an asset.
- `Assets/Shaders/` holds authored shaders/includes referenced by materials or
  shader includes. `Assets/Resources/Shaders/` holds shaders and compute shaders
  that production code loads dynamically from `Resources`. Do not move between
  these roots without checking material references and every runtime load path.
- `Assets/Resources/UI/` contains runtime-loaded UXML and its referenced USS.
  `Assets/Resources/Styles/` contains shared USS resources; shared theme assets
  stay under `Assets/UI Toolkit/`. Keep UXML structure with its corresponding UI
  feature.
- `Assets/Textures/` contains authored, directly referenced/imported art. Group
  by stable domain (cells, items, UI, VFX, etc.). Numeric or legacy names that
  are identifiers must be mapped/documented rather than renamed for aesthetics.
- `Assets/StreamingAssets/` is for files that must remain directly addressable
  in the built player. It is not a second general-purpose Resources directory.
- Always move Unity assets with their `.meta` files. Do not hand-edit scenes,
  prefabs, materials, or other serialized Unity assets as text.

## Tests and standalone tools

- Put Unity tests only below `Assets/Scripts/Tests/{Editor,PlayMode,Shared}`;
  do not create feature-local `Tests` directories.
- Platform-independent harnesses and .NET tests live under `tools/` in a
  named project directory. They supplement, but do not replace, Unity tests
  when production Unity behavior is the subject of verification.
- `scripts/` is for small entry points; reusable or multi-file implementation
  belongs under `tools/`.

## Documentation navigation

- architecture and asset contracts: `docs/architecture/`;
- plans and readiness criteria: `docs/planning/`;
- handoffs and operational audits: `docs/operations/`;
- design inventories and visual reviews: `docs/design/`;
- lighting research sources: `docs/lighting-research/`;
- visual catalog: `docs/index.html`;
- project-wide agent rules: `AGENTS.md` and `.agents/project-context.md`.

## Local/generated files

Do not include local or generated trees such as `Library/`, `Temp/`, `Logs/`,
`Build/`, `LightingDumps/`, `ProfilerCaptures/`, `UserSettings/`, `bin/`, and
`obj/` in source inventories or repository maps.
