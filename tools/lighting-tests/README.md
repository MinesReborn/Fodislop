# Kern lighting tests

The lighting test tools are a standalone C# runner. They do not start Unity or
load Editor assemblies.

```bash
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- all
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- transport
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- equivalence reference.compute candidate.compute
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- compile
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- streaming
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- freeze-report LightingDumps/<dump>
```

The native HLSL fixture remains C++ because the production shader functions are
compiled with `clang++` and exercised through the same float32 shim. C# owns
include expansion, function extraction, compilation, execution and comparison.
