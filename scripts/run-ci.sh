#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo "================================================="
echo "===               Kern Unified CI             ==="
echo "================================================="

echo "--- 1. Testing Architecture Linter ---"
dotnet test tools/architecture/Kern.ArchitectureLinter.Tests/Kern.ArchitectureLinter.Tests.csproj --configuration Release --verbosity normal

echo "--- 2. Running Architecture Linter (Source Scan) ---"
dotnet run --project tools/architecture/Kern.ArchitectureLinter/Kern.ArchitectureLinter.csproj \
    --configuration Release -- \
    --project-root "$ROOT" \
    --source-only \
    --fail-on Error

echo "--- 3. Running Standalone Tests ---"
dotnet test tools/tests/Kern.DisplayTests/Kern.DisplayTests.csproj --configuration Release --verbosity normal
dotnet test tools/tests/Kern.TerrainTests/Kern.TerrainTests.csproj --configuration Release --verbosity normal
dotnet test tools/tests/Kern.WorldLightingExchangeTests/Kern.WorldLightingExchangeTests.csproj --configuration Release --verbosity normal
dotnet test tools/tests/Kern.FrameHarness.Tests/Kern.FrameHarness.Tests.csproj --configuration Release --verbosity normal

echo "--- 4. Building Tools with Warnings As Errors ---"
dotnet build tools/generators/Kern.DesignSystem/Kern.DesignSystem.csproj --configuration Release --warnaserror
dotnet build tools/tests/Kern.TerrainCrystalTests/GenerateCrystalFixtures/GenerateCrystalFixtures.csproj --configuration Release --warnaserror

echo "--- 5. Validating Lighting Transport, Crystals & Terrain Raster ---"
dotnet run --project tools/tests/Kern.LightingTests/Kern.LightingTests.csproj -- transport
dotnet run --project tools/tests/Kern.TerrainCrystalTests/RunCrystalTests/RunCrystalTests.csproj
dotnet run --project tools/tests/Kern.TerrainRasterTests/RunRasterTests/RunRasterTests.csproj

echo "================================================="
echo "===       ALL UNIFIED CI CHECKS PASSED        ==="
echo "================================================="
