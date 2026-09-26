# Architecture linter findings: Terrain/Lighting refactor

## Final run

Full captured output: `terrain-lighting-architecture-linter-2026-09-24.txt`.
Initial 228-finding output, before root-cause fixes: `terrain-lighting-architecture-linter-initial-2026-09-24.txt`.

Command: `dotnet run --project tools/Kern.ArchitectureLinter/Kern.ArchitectureLinter.csproj -- --project-root .`
Final result: 54 rules evaluated, exit code 0, 0 errors, 15 warnings.

The initial 209 errors were audited against runtime dataflow and source paths:

- **202 `KERN-LOCALIZATION` dead-key errors:** all locale keys had UI references in nested `.uxml` files after the UI tree was organized under `Menus`, `Gameplay`, and `Overlays`. `LocalizationRule` searched only top-level `Assets/Resources/UI/*.uxml`, while `SourceScanner.EnumerateUxmlFiles` already provided recursive traversal. The rule now uses that recursive enumerator. No localization keys or locale files were deleted or changed. A linter fixture verifies a key referenced from nested UXML is live.
- **7 `KERN-PATTERN` errors:** the two allow-path expressions named the old `Assets/Scripts/Core/Bootstrap/*LifetimeScope.cs` locations. The existing composition roots are now under `Assets/Scripts/Core/Bootstrap/Scopes/`. The two allow paths now identify scope files in that exact directory; runtime Bootstrap code is unchanged, and `TryResolve` outside that composition-root path still fails. A positive and negative linter fixture cover the boundary.

The final run reports these warnings:

| Rule | Count | Exact paths / findings | Disposition |
| --- | ---: | --- | --- |
| `KERN-OVERSIZED-FILE` — production files exceed 500 lines | 10 | `Assets/Scripts/Game/Audio/ServerAudioEvent.cs` (509); `Assets/Scripts/Networking/Connection/ConnectionManager.cs` (587); `Assets/Scripts/Rendering/Tools/Imgui/Windows/RenderBypassWindow.cs` (540); `Assets/Scripts/UI/Map/WorldMap/WorldMapRenderer.cs` (758); `Assets/Scripts/World/Lighting/Core/LightingEngine.cs` (571); `Assets/Scripts/World/Terrain/Core/TerrainRenderer.cs` (623); `Assets/Scripts/World/Terrain/Gpu/TerrainCellBuilder.cs` (675); `Assets/Scripts/World/Terrain/Gpu/TerrainWindow.cs` (689); `Assets/Scripts/World/Textures/TextureAtlas.cs` (570); `Assets/Scripts/World/Textures/WorldTextureManager.cs` (535). | Six paths are outside Terrain/Lighting. Four domain warnings remain listed as maintainability work; no suppression was added. Distinct channel texture/staging ownership moved from `TerrainCellDataTextures` to `TerrainCellDataChannel<T>`; the file is now 368 lines. Dynamic polar ray-budget allocation moved from `DynamicLightingSolver` to `DynamicPolarWorkBudget`; the solver is now 465 lines. The Terrain frame exchange publisher was extracted from `TerrainRenderer`, which is now 623 lines. |
| `KERN-DEAD-MEMBERS` — Dead member detection | 4 | `RuntimeTextureFactory.CreateRGBA32WithMipmaps` — `Assets/Scripts/AssetPipeline/Loading/RuntimeTextureFactory.cs:62`; `PlayerMovementMath.DirectionToAngle` — `Assets/Scripts/Game/Player/Logic/PlayerMovementMath.cs:35`; `WorldMapMipCache.HasStoredChunk` — `Assets/Scripts/UI/Map/Core/WorldMapMipCache.cs:126`; `WorldLayerFile.TryLoadInto` — `Assets/Scripts/World/Persistence/WorldLayerFile.cs:127`. | All outside Terrain/Lighting. Terrain `UpdateRegion` and `TerrainQuadBuilder.IsBuildingBlock` were removed after repository-wide C# consumer scans found declarations only. The two unused public revision properties `TerrainContentRevision` and `PublishedTerrainContentRevision` were also removed after a no-consumer scan. |
| `KERN-SILENT-UI-NOOP` — Silent UI no-op detection | 1 | `Assets/Scripts/UI/Map/Core/MapInteractionController.cs:83`: null-root guard returns silently. | Unrelated UI warning; not changed. |

`KERN-TERRAIN-LIGHTING-BOUNDARY`, `KERN-NAMING`, `KERN-FORBIDDEN-API`, localization, and Bootstrap pattern checks all report zero violations. Targeted Terrain↔Lighting and Audio→Terrain source scans are empty. `git diff --check` passes.

Gate D is accepted: the registered full linter exits 0, and its only findings are the 15 warnings listed above. Gate C source integration is present, but runtime acceptance remains pending production-path Unity compile/PlayMode evidence; no Unity operation was run.
