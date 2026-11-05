# Kern agent guidance

Kern — 2D MMORPG sandbox on Unity 6 (`6000.7.0b4`, see `ProjectSettings/ProjectVersion.txt`), URP 2D 17.7, C# 12, UI Toolkit, UniTask, and `darkar25.kern.*` packages from the `Packages/MinesServerNetworking` submodule.

The user has the final word: an explicit instruction in the current message
overrides conflicting project guidance, including Git and rollback restrictions.

## Specialized guides

Specializations are implemented as skills and auto-load based on task triggers. If the task changes domain — explicitly load the corresponding skill before proceeding.

| Domain | Skill | Triggers |
|--------|-------|---------|
| C#, namespaces, DI, assembly layers | `csharp-conventions` | MonoBehaviour, VContainer, asmdef, nullable, SA1513 |
| Coordinates, UI positioning, camera, RenderTexture, fallbacks, profiler | `critical-invariants` | WorldCoordinateConverter, RuntimePanelUtils, LifetimeScope, VSync, EditorLoop |
| Lighting shaders, bloom, tonemapping, HDR, post-processing, calibration | `hdr-color-contract` | saturate, ARGBHalf, HDROutputReconciler, paper white, CompositeFinal |
| Lighting compute, DDA, cascades, bounce | `lighting-guide` | CascadeTrace, DDA.hlsl, TraceLightSegment, IFrameTelemetry |
| Scenes, DI, startup pipeline | sections 2–3 of [`project-context.md`](.agents/project-context.md) | |
| Network, UI, world, rendering, audio, Programmator | section 4 of [`project-context.md`](.agents/project-context.md) | |
| Architectural invariants | section 5 of [`project-context.md`](.agents/project-context.md) | |
| Diagnostics, performance | section 6 of [`project-context.md`](.agents/project-context.md) | |
| Directory boundaries and asmdef | [`repository-map.md`](.agents/repository-map.md) | |

Terrain and lighting changes MUST follow the review gates in
[`TERRAIN_LIGHTING_STANDARD.md`](docs/architecture/TERRAIN_LIGHTING_STANDARD.md).
Treat it as the normative target; record the required evidence within the
documentation scope below and pass its merge gates. Do not extend its documented
conformance debt or waive a failed invariant in a PR comment.
The first Terrain-to-Lighting orchestration migration MUST follow gates A–D in
that standard in order; do not replace the direct reference with a forwarding
facade.

Code is the source of truth if reference context is stale. Don't read everything — only what the task requires.

## Documentation scope

For ordinary fixes, summarize the cause, change, checks and any unverified results
briefly in the final response or the requested PR description.
Do not create standalone reports, evidence bundles, audits, checklists, dated
documents or explanatory pages unless the user explicitly requests them.
Update existing documentation only when the change affects its documented
contract, architecture or usage; make the smallest relevant edit.

This scope overrides requirements to create separate documentation artifacts in
project guides and skills, including `TERRAIN_LIGHTING_STANDARD.md`. Put required
evidence concisely in the response or PR; all applicable implementation and
verification gates remain mandatory. A missing report file is not a failed gate.

## Repository taxonomy

Use module → feature → responsibility folders. Put every project source file in
its owning responsibility folder, including a single file. Do not mix source
files with child categories or collect unowned code in Utils, Helpers, Common,
Misc, Support, or Managers. Split unrelated operations and name their owners.
Keep neutral contracts beside their domain with the correct asmref; folder
hierarchy does not replace assembly boundaries. Move Unity files with their
metadata and migrate resource keys, namespace strings and tool paths together.
See `.agents/repository-map.md` and `tools/architecture/Kern.RepositoryTaxonomy/Inventory/`.

Do not use C# `partial` types. Split responsibilities into separate types and
files with explicit ownership instead of dividing one type across files.

## Authority boundaries

- Never launch, invoke, or control Unity Editor, Unity Hub, or Unity CLI unless the user explicitly requests that specific operation in the current task. Code changes, shader changes, visual fixes, test requirements, skills, and verification standards do not by themselves authorize a Unity operation. Use source inspection, existing logs, and relevant local checks that do not invoke Unity; continue all independently available work without asking to run Unity.
- Never mention Unity project locks, lockfiles, or an open Editor as a blocker in commentary or final responses. Do not inspect Editor processes or lockfiles just to check whether Unity could run, ask the user to close the Editor, or stop implementation on that basis. State the checks actually performed without claiming unrun runtime or GPU verification passed.
- Clones and copies of the project are forbidden: never copy, clone, or duplicate the project or its `Assets`/`Library`/`Temp` to another path (scratchpad, `/tmp`, APFS clone), and never run tests or builds against such a copy. Stay inside the working tree.
- Android verification builds must use Development mode by default so device logs and debugging are available. For `Kern.Editor.BuildScript.BuildAndroid`, pass `-kernDev` through Unity's command-line arguments (for example, `unity build --target Android --execute-method Kern.Editor.BuildScript.BuildAndroid --args "-kernDev"`). Build Release only when the user explicitly requests a Release/store build.
- For Android launch or world-entry investigations, trace the complete startup path in project code and inspect existing logs first; do not start ADB/device sessions or build an APK unless the user explicitly requests that action in the current task. A request to investigate or fix the failure alone does not authorize repeated builds. When a build is explicitly requested, use Development mode as specified above.
- During code-only Android launch investigations, preserve existing timeout values. Never increase a timeout just to defer a failure or make startup appear successful; first identify its owner, start point, covered phase, and failure behavior, then fix the underlying cause. Do not run a build as an indirect way to validate a code-only investigation.
- Before opening an Android APK in Finder or handing it to the user, verify it was produced by the latest successful requested build (check build result, modification time, and expected Development/debug configuration). Never present an older APK as the new build; if the requested build failed or produced no APK, say so plainly and do not open the stale artifact.
- Keep the in-game diagnostic/status line enabled in Android Development builds. Do not remove or hide runtime diagnostics to make a build look cleaner; verify the line is present in the APK configuration before sharing it.
- Do not launch, open, close, or control Unity Editor/Hub in GUI mode, and do not execute builds or destructive operations unless requested by the user.
- ABSOLUTE BAN, NO EXCEPTIONS — not even when the user asks to "roll back", "undo" or "revert": never run any command that overwrites working-tree or index content from Git. This includes `git restore` (any flags), `git checkout -- <path>`, `git checkout <commit> -- <path>`, `git switch` with discard, `git reset` (any mode), `git stash` (push, pop, apply, drop), `git clean`, `git revert`, `git apply -R`, `git read-tree`, `git checkout-index`, `git rm --cached`, amend, rebase, force-push, and any script, alias or tool that does the same. Do not restore files from `HEAD`, the index, stash or reflog in any form. A single path can contain the user's uncommitted work that Git cannot bring back.
- NEVER ROLL BACK ANYTHING. This rule is broader than Git: it is forbidden to undo your own edit by any means — neither a `git` command, nor manually reverting file text, nor deleting added code and tests. Rollback is permitted ONLY when the user explicitly requests it in the current message, and even then ONLY by editing file text by hand (Edit/Write), removing exactly your own changes — never through Git or any command that copies content from history.
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

**HARD REQUIREMENT — FINISH CLEAR TASKS.** When the user gives a clear,
actionable request, DO THE WORK. Do not bail out with a status update, partial
diagnosis, hypothesis, apology, or list of things still to investigate. Keep
reading the relevant code, make the requested change, and verify it as far as
the permitted tools allow. The task is not complete until the requested result
exists.

**NO FAKE BLOCKERS.** Your uncertainty, incomplete search, or failure to spot a
file is not a blocker. Search the repository and relevant neighboring project
paths thoroughly, trace the actual implementation, and keep going. Do not ask
the user to repeat, clarify, or authorize work they already clearly requested.
Ask only when a concrete, consequential choice cannot be resolved from the
conversation or code. Continue every independent part of the task while that
choice is pending.

**FOLLOW THE REQUEST, NOT AN INVENTED SUBSTITUTE.** Implement the artifact the
user asked for. Do not replace animations with animation debug views, or
animations with test cells; do not invent fixtures, entities, UI, or content.
Use the conversation and repository context to choose the implementation that
matches the stated outcome. If the user corrects you, treat that correction as
steering for the active task: acknowledge briefly and immediately continue the
corrected task. Stop only when the user explicitly says to stop or replaces
the objective.

When the user sends project errors, compiler output, stack traces, or runtime logs, treat them as an instruction to fix the reported problem immediately. Locate the root cause, edit the affected files, and run the strongest permitted verification. Do not stop at explaining the error or merely suggesting a fix; only report without editing when the user explicitly asks for diagnosis only.

Never suppress, downgrade, or deduplicate error diagnostics as a substitute for
fixing the failure. Preserve the error and its useful context while tracing the
root cause. Do not swallow exceptions or replace a failing handler with a
successful no-op. Change logging behavior only when explicitly requested and
when the logging behavior itself is the stated defect.

For non-trivial work: define the outcome, make the changes, and continue until verified completion unless a new user decision is required. Without separate approval, you may run relevant local checks that do not control Unity, have no production access, and use disposable fixtures. Fix failures caused by your change and re-run the relevant checks.

For terrain/lighting work, identify the owning domain, stage, invalidation path,
and required proof before editing; apply the mandatory checklist in
`docs/architecture/TERRAIN_LIGHTING_STANDARD.md` to the final diff. Missing
required evidence or a failed applicable gate blocks completion.

When the user reports a current performance regression, treat it as present in the current working tree. A previously fixed bug or measurements from before that fix do not resolve the report; continue investigating the current cause and do not shift verification of the reported regression onto the user.

When investigating a launch or world-entry failure, do not increase or otherwise alter timeouts to mask the failure. Trace where each timer starts, which phases it covers, and what its timeout path does; change timeout policy only when the cause and intended phase contract are established by code or runtime evidence. Prefer surfacing the first startup exception at the failing composition or lifecycle boundary.

Trust the user's reported symptoms and measurements as facts: if the user reports a 60 ms freeze when digging a block, investigate that freeze without asking them to prove it, reproduce it again, or provide a profile. Distinguish the accepted symptom from the cause that still needs investigation. When instructed to read code or use existing logs only, trace the complete production code path autonomously; do not substitute requests to control Unity, collect new measurements, or obtain confirmation for that work. Missing profiling evidence does not invalidate the user's report or justify stopping code analysis. State uncertainty about the cause honestly without casting doubt on the reported problem.

Keep the investigation anchored to the subsystem and symptom the user identified. For a lighting FPS regression, trace the current frame's lighting invalidation, repeated rebuilds, dispatches, shaders, and GPU cost until the cause of the reported slowdown is established. A screenshot of GC, an incidental diagnostic cost, or an unrelated inefficiency is supporting evidence only; do not switch tasks or edit that path unless its causal contribution to the reported slowdown is demonstrated. Do not present a minor or unmeasured improvement as a fix for the main regression.

For claims about visual or GPU results, the test must go through the production path: real shader and pass, real mesh attributes/`SV_POSITION`, production material keywords, real data textures, and the same camera/projection path. An isolated probe shader, a manual helper function call, a static source check, or a CPU model are supplementary tests only — they do not prove game behavior. If the production path cannot be run, explicitly mark the verification as incomplete.

The visual test oracle must be independent of production functions. A helper matching itself is not valid as a regression proof.

Never explain a visual or runtime problem by saying that debug mode is enabled. Debug state may be inspected as one hypothesis, but it is not evidence of the root cause and must never end the investigation or replace a production-path fix. A screenshot with flat colors must be traced through the real shader, mesh data, material keywords, textures, and camera path; if that production verification cannot be performed, report the result as unverified instead of attributing it to `TerrainDebugView` or any other debug feature.

Never explain freezes, frame spikes, or other performance problems by the Unity Editor (editor overhead, inspector repaint, editor GC, play mode in editor) and never propose checking in a player build as a way to dismiss them. The root cause must be found in project code, shaders, or assets and fixed there.

Never explain lag, low FPS, freezes, or frame spikes by other processes or system load (other apps or games, emulators, browsers, IDE extensions, WindowServer, memory pressure, swap) and never propose closing other programs to make the problem go away. The root cause must be found in project code, shaders, or assets and fixed there.

Do not invent things the user did not ask for. Motion, rotation, animation, pulsing, flickering — NOT added on the agent's initiative. A static image means a static result. A correction to one word in the description applies to the entire entity.

Do not return an intermediate blocker or symptom description as a result. Independently locate the root cause and continue to an actual result. Stop only when available options are exhausted and the next step genuinely requires new permission or a user decision — then report a specific proven blocker without excuses or repetition.
