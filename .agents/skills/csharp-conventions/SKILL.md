---
name: csharp-conventions
description: >-
  Kern project C# conventions, namespace rules, DI/VContainer patterns, and assembly layer boundaries.
  Use when writing, editing, or reviewing any C# file — MonoBehaviours, ScriptableObjects, services,
  contracts, factories, or editor scripts. Triggers on: namespace, nullable, asmdef, VContainer,
  RegisterComponent, IObjectResolver, composition root, Kern.Contracts, primary constructor,
  record struct, SA1513, _camelCase, PascalCase, asmref, MonoScript.GetClass.
---

# C# and structure

## Language and nullable

- `#nullable enable` is enabled; annotate reference types explicitly as nullable or non-null.
- Use C# 12 features (`primary constructors`, `readonly record struct`, collection expressions) where appropriate.

## Namespaces

- Regular types use **file-scoped namespaces**.
- Types deriving from `MonoBehaviour`, `ScriptableObject`, `ScriptableRendererFeature`, or `VolumeComponent` use **block namespaces** — otherwise `MonoScript.GetClass()` may return `null`.

## Style

- Allman braces, mandatory `{}`, SA1513/SA1508.
- Trailing comma in multi-line initializers.
- Non-public instance fields — `_camelCase`; non-public static fields — `s_camelCase`; constants (including local constants), public fields, types, namespaces, methods, properties, events, enum members, delegates, and local functions — `PascalCase`.
- Interfaces — `I` + `PascalCase`; generic type parameters — `T` + `PascalCase`; locals and parameters — `camelCase`.
- Use `Is`/`Has`/`Can` for boolean meaning, `Count` for quantities, `Index` for positions, and `Id` for identity. Use meaningful plural collection names and `ById` for identity lookup.
- Async operations use the `Async` suffix; event handlers use `Handle` + event name. Use `Id`, `Uv`, and `Ms`; keep `CPU`, `LUT`, `FPS`, `M3G`, `GPU`, `LRU`, and `RLE` uppercase in compound identifiers; retain names required by external APIs and contracts.
- Use `Credits` rather than `Creds` for project-owned currency identifiers. Keep immutable external packet/API names at the integration boundary.
- Include units when otherwise ambiguous (`durationSeconds`, `gpuTimeMs`, `radiusPixels`, `distanceMeters`).
- Prefer private `[SerializeField]` fields with public properties. Preserve Unity callback names and shader parameter contracts exactly. When renaming serialized fields, preserve values with `[FormerlySerializedAs]`; migrate existing symbols by subsystem rather than mass replacement.
- `.editorconfig` enforces naming at warning severity. Shader and asset names follow their domain contracts; C# naming rules do not rename shader bindings or assets.
- Unity script file name must match the class name.

## DI / VContainer

- Do not create managers via `AddComponent` in `Configure`.
- Register scene components via `RegisterComponent`; create prefabs/entities via `ISceneObjectFactory`.
- `IObjectResolver` is permitted only in composition roots and factories.
- `RegisterInstance` does not inject manually created objects.
- Do not resolve the container in `Awake`, `OnEnable`, or `Start`.

## Assembly layers

- `Kern.Contracts` is the bottom layer: it does not reference any `Kern.*` assembly.
- Module contracts live next to the module in a `Contracts/` folder with `Kern.Contracts.asmref`; without it the type lands in the module assembly and breaks everyone below.
- Do not place types that depend on implementation details in `Contracts/`.
- Guarded by `ContractsAssemblyBoundaryTests`.

## Documents

- Files in `docs/` must be self-contained HTML with inline `<style>`, no Markdown, no external dependencies.
