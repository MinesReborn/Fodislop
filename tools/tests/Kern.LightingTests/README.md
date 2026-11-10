# Kern lighting tests

The lighting test tools are a standalone C# runner. They do not start Unity or
load Editor assemblies.

```bash
dotnet run --project tools/tests/Kern.LightingTests/Kern.LightingTests.csproj -- all
dotnet run --project tools/tests/Kern.LightingTests/Kern.LightingTests.csproj -- transport
dotnet run --project tools/tests/Kern.LightingTests/Kern.LightingTests.csproj -- rects
dotnet run --project tools/tests/Kern.LightingTests/Kern.LightingTests.csproj -- equivalence reference.compute candidate.compute
dotnet run --project tools/tests/Kern.LightingTests/Kern.LightingTests.csproj -- compile
dotnet run --project tools/tests/Kern.LightingTests/Kern.LightingTests.csproj -- streaming
dotnet run --project tools/tests/Kern.LightingTests/Kern.LightingTests.csproj -- freeze-report LightingDumps/<dump>
```

The native HLSL fixture remains C++ because the production shader functions are
compiled with `clang++` and exercised through the same float32 shim. C# owns
include expansion, function extraction, compilation, execution and comparison.

`rects` checks local-ray coverage and the full far-to-near dependency chain,
including clamped edge probes and non-divisible field dimensions. `transport`
uses the actual `StaticLightingDirty` conversion/planner to dispatch the HLSL
cascades after placing/removing an emissive solid. Its packed atlas must exactly
match a full solve with bilinear correction on and off. A local-only rectangle
mutation must fail; a mask test that dispatches every probe unconditionally
would not detect the original square-halo defect. These are numerical checks,
not proof of Unity/Metal bindings or the final camera image.
