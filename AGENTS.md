# Kern agent guidance

Kern — 2D MMORPG sandbox on Unity 6 (`6000.6.0f1`), URP 2D 17.6, C# 12, UI Toolkit, UniTask, and `darkar25.fodina.*` packages.

## Specialized guides

Specializations are implemented as skills and auto-load based on task triggers. If the task changes domain — explicitly load the corresponding skill before proceeding.

| Domain | Skill | Triggers |
|--------|-------|---------|
| C#, namespaces, DI, assembly layers | `csharp-conventions` | MonoBehaviour, VContainer, asmdef, nullable, SA1513 |
| Coordinates, UI positioning, camera, RenderTexture, fallbacks, profiler | `critical-invariants` | CoordinateUtils, RuntimePanelUtils, LifetimeScope, VSync, EditorLoop |
| Lighting shaders, bloom, tonemapping, HDR, post-processing, calibration | `hdr-color-contract` | saturate, ARGBHalf, HDROutputReconciler, paper white, CompositeFinal |
| Lighting compute, DDA, cascades, bounce | `lighting-guide` | CascadeTrace, DDA.hlsl, TraceLightSegment, IFrameTelemetry |
| Scenes, DI, startup pipeline | sections 2–3 of [`project-context.md`](.agents/project-context.md) | |
| Network, UI, world, rendering, audio, Programmator | section 4 of [`project-context.md`](.agents/project-context.md) | |
| Architectural invariants | section 5 of [`project-context.md`](.agents/project-context.md) | |
| Diagnostics, performance | section 6 of [`project-context.md`](.agents/project-context.md) | |
| Directory boundaries and asmdef | [`repository-map.md`](.agents/repository-map.md) | |

Terrain and lighting changes MUST follow the review gates in
[`TERRAIN_LIGHTING_STANDARD.md`](docs/architecture/TERRAIN_LIGHTING_STANDARD.md).
Treat it as the normative target; attach its required evidence bundle and pass
its merge gates. Do not extend its documented conformance debt or waive a failed
invariant in a PR comment.
The first Terrain-to-Lighting orchestration migration MUST follow gates A–D in
that standard in order; do not replace the direct reference with a forwarding
facade.

Code is the source of truth if reference context is stale. Don't read everything — only what the task requires.

## Authority boundaries

- Unity CLI is available as `unity`. Invoke it when requested by the user. Requests to run tests (such as “запусти тесты”, “запусти все тесты”, “run tests”, “run EditMode tests”, “run PlayMode tests”) authorize running the corresponding Unity tests.
- Do not launch, open, close, or control Unity Editor/Hub in GUI mode, and do not execute builds or destructive operations unless requested by the user.
- Do not perform Git rollback or history rewriting without an explicit request in the current message: `reset`, `restore`, checkout for restoration, `revert`, `clean`, amend, rebase, or force-push. Do not restore files from `HEAD`, stash, or reflog, and do not solicit such permission on your own initiative.
- NEVER ROLL BACK ANYTHING. This rule is broader than Git: it is forbidden to undo your own edit by any means — neither a `git` command, nor manually reverting file text, nor deleting added code and tests. Rollback is permitted ONLY when the user explicitly requests it in the current message.
- Do not edit `.prefab`, `.unity`, or `.asset` files as text; modify them only through explicitly permitted Unity Editor API/Inspector. Preserve GUIDs and `.meta` files.
- Existing working-tree changes belong to the user. Do not overwrite or incorporate them into your changes without necessity.
- During an active task, a short question, question-reply, complaint, or hypothesis is steering for that task: answer briefly and continue the already authorized investigation and fixes. Do not stop work or demand another "work" instruction. Stop only when the user explicitly asks to stop/pause or clearly replaces the objective. A standalone question with no active task calls for an answer; it does not by itself authorize unrelated changes. Preserve existing restrictions on Unity control and other operations.
- NEVER USE `--no-verify`!
- Warning suppression is forbidden: do not add `SuppressMessage`, `#pragma warning disable`, `NoWarn`, disabling blanket warnings, or similar exclusions. Fix the root cause of the warning; an exception is permitted only for an immutable third-party package that is not part of project code.
- `git commit` and `git push` are executed ONLY when the user explicitly requests it in the current message. A user request to "push" means stage all working-tree changes, create one commit with a short Russian message, and push it; do not ask for separate commit authorization. Do not commit or push after completing a task "for convenience" or "to save" — only file edits.
- Always commit everything: all working-tree changes in one commit (`git add -A && git commit`), without splitting or selective staging, unless the user explicitly requests otherwise.
- After every user-requested `git commit`/`git push`, immediately monitor the resulting GitHub Actions run(s) until they finish. Fetch failed-job logs, fix the root cause, push the fix, and continue monitoring; do not report completion while a run is queued, in progress, or failed.
- CI must use standard GitHub-hosted runners (`ubuntu-latest`, `macos-latest`, `windows-latest`); self-hosted/Unity runner labels are forbidden. If a job requires Unity, move it to a standard runner instead of leaving it queued.
- Never make CI green by skipping required checks. Missing Unity assemblies, build artifacts, or other required inputs are a CI failure: produce them in an earlier job or fail with the real error.
- Keep commit messages short and in Russian.

## Task execution

When the user sends project errors, compiler output, stack traces, or runtime logs, treat them as an instruction to fix the reported problem immediately. Locate the root cause, edit the affected files, and run the strongest permitted verification. Do not stop at explaining the error or merely suggesting a fix; only report without editing when the user explicitly asks for diagnosis only.

For non-trivial work: define the outcome, make the changes, and continue until verified completion unless a new user decision is required. Without separate approval, you may run relevant local checks that do not control Unity, have no production access, and use disposable fixtures. Fix failures caused by your change and re-run the relevant checks.

For terrain/lighting work, identify the owning domain, stage, invalidation path,
and required proof before editing; apply the mandatory checklist in
`docs/architecture/TERRAIN_LIGHTING_STANDARD.md` to the final diff. Missing
required evidence or a failed applicable gate blocks completion.

When the user reports a current performance regression, treat it as present in the current working tree. A previously fixed bug or measurements from before that fix do not resolve the report; continue investigating the current cause and do not shift verification of the reported regression onto the user.

Trust the user's reported symptoms and measurements as facts: if the user reports a 60 ms freeze when digging a block, investigate that freeze without asking them to prove it, reproduce it again, or provide a profile. Distinguish the accepted symptom from the cause that still needs investigation. When instructed to read code or use existing logs only, trace the complete production code path autonomously; do not substitute requests to control Unity, collect new measurements, or obtain confirmation for that work. Missing profiling evidence does not invalidate the user's report or justify stopping code analysis. State uncertainty about the cause honestly without casting doubt on the reported problem.

Keep the investigation anchored to the subsystem and symptom the user identified. For a lighting FPS regression, trace the current frame's lighting invalidation, repeated rebuilds, dispatches, shaders, and GPU cost until the cause of the reported slowdown is established. A screenshot of GC, an incidental diagnostic cost, or an unrelated inefficiency is supporting evidence only; do not switch tasks or edit that path unless its causal contribution to the reported slowdown is demonstrated. Do not present a minor or unmeasured improvement as a fix for the main regression.

For claims about visual or GPU results, the test must go through the production path: real shader and pass, real mesh attributes/`SV_POSITION`, production material keywords, real data textures, and the same camera/projection path. An isolated probe shader, a manual helper function call, a static source check, or a CPU model are supplementary tests only — they do not prove game behavior. If the production path cannot be run, explicitly mark the verification as incomplete.

The visual test oracle must be independent of production functions. A helper matching itself is not valid as a regression proof.

Never explain a visual or runtime problem by saying that debug mode is enabled. Debug state may be inspected as one hypothesis, but it is not evidence of the root cause and must never end the investigation or replace a production-path fix. A screenshot with flat colors must be traced through the real shader, mesh data, material keywords, textures, and camera path; if that production verification cannot be performed, report the result as unverified instead of attributing it to `TerrainDebugView` or any other debug feature.

Never explain freezes, frame spikes, or other performance problems by the Unity Editor (editor overhead, inspector repaint, editor GC, play mode in editor) and never propose checking in a player build as a way to dismiss them. The root cause must be found in project code, shaders, or assets and fixed there.

Never explain lag, low FPS, freezes, or frame spikes by other processes or system load (other apps or games, emulators, browsers, IDE extensions, WindowServer, memory pressure, swap) and never propose closing other programs to make the problem go away. The root cause must be found in project code, shaders, or assets and fixed there.

Do not invent things the user did not ask for. Motion, rotation, animation, pulsing, flickering — NOT added on the agent's initiative. A static image means a static result. A correction to one word in the description applies to the entire entity.

Do not return an intermediate blocker or symptom description as a result. Independently locate the root cause and continue to an actual result. Stop only when available options are exhausted and the next step genuinely requires new permission or a user decision — then report a specific proven blocker without excuses or repetition.
