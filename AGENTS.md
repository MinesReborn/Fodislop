# Kern agent guidance

Kern is a 2D MMORPG sandbox built with Unity 6 (`6000.7.0b4`), URP 2D
`17.7.0`, C# 12, UI Toolkit, UniTask, and the `darkar25.kern.*` packages in
`Packages/MinesServerNetworking`. Confirm versions in `ProjectSettings/` and
`Packages/manifest.json` when they matter. Source code is authoritative when
this guidance or a referenced document is stale.

The user's explicit instructions in the current task take precedence over this
file. Read only the relevant sections and specialized guides; do not load the
whole repository without a reason.

## Project context and specialized guides

- [`project-context.md`](.agents/project-context.md): current scene startup,
  DI/lifecycle contracts, subsystem invariants, diagnostics and verification.
- [`repository-map.md`](.agents/repository-map.md): assembly boundaries,
  source/assets placement, test/tool roots and taxonomy.
- [`TERRAIN_LIGHTING_STANDARD.md`](docs/architecture/TERRAIN_LIGHTING_STANDARD.md):
  mandatory terrain/lighting review gates. For the first Terrain-to-Lighting
  orchestration migration, complete gates A–D in order and keep the direct
  reference; do not insert a forwarding facade.
- Load the matching project skill before C# or specialized subsystem work:
  `csharp-conventions`, `critical-invariants`, `hdr-color-contract`,
  `lighting-guide`, `ui` / `ui-uitk` / `ui-ugui`, and others listed in the
  available skills catalog. Skills add workflow detail; they do not override
  explicit user instructions or this file.

| Area | Skill(s) | Use when |
| --- | --- | --- |
| C#, namespaces, DI, asmdefs | `csharp-conventions` | Editing or reviewing C# source |
| Coordinates, cameras, UI positioning, RenderTexture, profiler invariants | `critical-invariants` | Touching coordinate conversion, screen anchoring, cameras, rendering lifetime or profiling |
| Terrain/lighting compute and DDA | `lighting-guide` | Editing lighting fields, cascades, bounce, or lighting orchestration |
| HDR, color, post-processing, calibration | `hdr-color-contract`, `urp-postprocessing` | Editing color transforms, display output, bloom, or Volume settings |
| UI | `ui`, then `ui-uitk` / `ui-ugui` / `ui-imgui` as appropriate | Inspecting or changing runtime/editor UI |
| FMOD banks and audio cost | `fmod-sync`, `optimize-audio` | Synchronizing banks or changing audio import/playback configuration |
| Localization | `localization`, `optimize-text-mesh-pro` | Adding locales, fonts, or localized TMP content |
| Whole-codebase restructuring | `untangle-codebase` | The architecture itself is the requested problem |
| Unity command-line/editor operations | `unity-cli` | Only after that specific operation is explicitly requested |
| Unity live services or purchases | `build-live-game`, `implement-in-app-purchases` | Implementing those backend or store features |

## Development and repository rules

- Server UI and its browser mockups must use the existing protocol as the source
  of truth. Do not hardcode behavior by window names, text, CSS roles, or example
  identifiers, and do not add undeclared component properties or packet fields.
  Before proposing a protocol extension, demonstrate the specific unmet need and
  check existing components, composition, Style, attached properties and supported
  rich text. Present alternatives and tradeoffs; obtain explicit user agreement
  before changing the protocol or adopting a new UI contract. Do not label a
  renderer/conversion defect as a missing protocol capability. Agree consequential
  design and architecture decisions with the user before implementing them.
- Use module → feature → responsibility folders. Keep source files in owning
  responsibility folders, assemblies at their `.asmdef`/`.asmref` boundaries,
  and neutral contracts with their domain. Do not introduce generic `Utils`,
  `Helpers`, `Common`, `Misc`, `Support`, or `Managers` bags. See the repository
  map and `tools/architecture/Kern.RepositoryTaxonomy/Inventory/`.
- Do not use C# `partial` types in project code. Separate responsibilities into
  owned types and files. (Vendored third-party code is not project code.)
- Follow C# conventions: nullable references are explicit; use file-scoped
  namespaces for ordinary types and block namespaces for Unity types requiring
  `MonoScript.GetClass()`; Allman braces, mandatory braces, project naming and
  analyzer rules apply. See `csharp-conventions` for the complete contract.
- Move Unity assets with their `.meta` files. Preserve GUIDs and migrate resource
  keys, namespace strings, serialized type references and tool paths together.
  Never edit `.prefab`, `.unity`, or `.asset` files as text. Do not modify them
  outside an explicitly authorized Unity API/Inspector operation.
- Do not create standalone reports, evidence bundles, audits, checklists, dated
  documents, or explanatory pages for ordinary fixes. Summarize cause, change,
  checks and unverified results in the final response or requested PR. Update
  existing documentation only when its contract, architecture or usage changes;
  make the smallest relevant edit. This scope overrides requests in project
  guides/skills for separate evidence documents; required implementation gates
  still apply and evidence may be summarized in the response.

## Authority and data safety

- Work only inside `/Users/murasama/Projects/games/Fodislop` for this project.
  Do not search, read, or modify sibling repositories or other project directories
  unless the user explicitly names that external project/path and authorizes the
  specific operation in the current task. A server-side feature, protocol task,
  or reference to a server does not authorize finding or editing another checkout.
  Use the sources and packages in this workspace; if external source changes are
  required, explain the exact dependency and ask before accessing that project.
- Never launch, invoke, or control Unity Editor, Unity Hub, or Unity CLI unless
  the user explicitly requests that specific operation in the current task.
  Code, shader, visual, test, skill, or verification requests do not grant that
  authority. Continue source inspection, existing-log review and local checks
  that do not control Unity. Do not inspect Editor processes or lockfiles, ask
  the user to close the Editor, or cite project locks as blockers.
- Do not copy, clone or duplicate this project or its `Assets`, `Library`, or
  `Temp` trees. Do not run builds or tests against such copies.
- Existing working-tree changes belong to the user. Inspect status before
  editing and avoid unrelated changes. Never overwrite working-tree or index
  content from Git history. Do not use `git restore`, discard checkout/switch,
  reset, stash, clean, revert, reverse-apply, read-tree, checkout-index, cached
  removal, amend, rebase, force-push, or any equivalent mechanism. Never roll
  back any change, including your own, unless the user explicitly asks in the
  current message; then remove only your own edits by hand, never from Git.
- Commit and push only when explicitly requested in the current message. A
  request to push authorizes staging all working-tree changes and making one
  commit with a short Russian message, then pushing. Never use `--no-verify`.
  After a requested commit/push, monitor resulting GitHub Actions runs to
  completion, inspect failures and fix their causes. CI must use standard
  GitHub-hosted runners; do not skip required checks to make CI green.
- Do not add warning suppressions (`SuppressMessage`, `#pragma warning disable`,
  `NoWarn`, blanket exclusions). Fix the cause; the only exception is immutable
  third-party code outside the project.

## Unity, Android and runtime verification

- Android verification builds use Development mode by default. For
  `Kern.Editor.BuildScript.BuildAndroid`, pass `-kernDev` through Unity's
  command-line arguments. Build Release only on an explicit request.
- For Android launch/world-entry issues, trace the complete startup path and
  inspect existing logs first. Do not start ADB/device sessions or build APKs
  unless explicitly requested. Code-only investigations preserve timeout values
  and must find the underlying cause rather than defer failure by extending a
  timeout.
- Keep the in-game diagnostic/status line enabled in Development builds. Before
  sharing an APK, verify it came from the latest successful requested build,
  check its modification time and Development/debug configuration, and confirm
  the diagnostic line is present. Never present a stale APK as a new build.
- Do not infer production visual/GPU behavior from a helper, isolated probe,
  source inspection or CPU model. Verification claims require the real
  production shader/pass, mesh attributes and `SV_POSITION`, material keywords,
  data textures and camera/projection path. If that path cannot be run within
  the user's authorization, mark runtime/GPU verification incomplete.

## Task execution and evidence

- Finish clear, actionable requests. Trace to the cause, implement the requested
  artifact, and verify as far as permitted. Uncertainty or an incomplete search
  is not a blocker. Continue independent work while resolving any truly
  consequential choice.
- Treat reported errors, logs and current performance symptoms as real and
  investigate/fix them. Do not suppress or downgrade diagnostics, swallow
  exceptions, or substitute a no-op for a failing handler. Keep investigation
  anchored to the reported subsystem; do not attribute defects to debug mode,
  Unity Editor overhead, or other processes. Do not mask performance problems
  with caps, delays, frame skipping or reduced required update frequency.
- For terrain/lighting changes, identify owner, stage, invalidation path and
  required proof before editing. Follow all applicable gates in
  `TERRAIN_LIGHTING_STANDARD.md`; do not extend documented conformance debt or
  waive failed invariants. The root cause and verification limits must be clear.
- Do not add unrequested motion, rotation, animation, pulsing or flickering.
- Do not run tests or builds unless the user asks to test/verify or the task
  explicitly requires it. When verification is requested, use relevant local
  checks that do not invoke Unity unless Unity itself was explicitly requested.
- Keep user updates concise and meaningful during longer work. Final responses
  should state what changed, why, checks performed and material unverified
  results. Link local files with absolute Markdown paths.

## Review log

- **2026-11-08:** Reconciled these shared rules with the current project context,
  repository layout, scene flow, assembly boundaries and verification limits.
