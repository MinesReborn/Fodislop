#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo "================================================="
echo "===               Kern Unified CI             ==="
echo "================================================="

echo "--- 1. Testing Architecture Linter ---"
dotnet test tools/Kern.ArchitectureLinter.Tests/Kern.ArchitectureLinter.Tests.csproj --configuration Release --verbosity normal

echo "--- 2. Running Architecture Linter (Source Scan) ---"
dotnet run --project tools/Kern.ArchitectureLinter/Kern.ArchitectureLinter.csproj \
    --configuration Release -- \
    --project-root "$ROOT" \
    --source-only \
    --fail-on Error

echo "--- 3. Running Standalone Tests ---"
dotnet test tools/Kern.DisplayTests/Kern.DisplayTests.csproj --configuration Release --verbosity normal
dotnet test tools/Kern.TerrainTests/Kern.TerrainTests.csproj --configuration Release --verbosity normal
dotnet test tools/Kern.WorldLightingExchangeTests/Kern.WorldLightingExchangeTests.csproj --configuration Release --verbosity normal
dotnet test tools/Kern.FrameHarness.Tests/Kern.FrameHarness.Tests.csproj --configuration Release --verbosity normal

echo "--- 4. Building Tools with Warnings As Errors ---"
dotnet build tools/Kern.DesignSystem/Kern.DesignSystem.csproj --configuration Release --warnaserror
dotnet build tools/Kern.TerrainCrystalTests/GenerateCrystalFixtures/GenerateCrystalFixtures.csproj --configuration Release --warnaserror

echo "--- 5. Validating Lighting Transport & Terrain Raster ---"
dotnet run --project tools/Kern.LightingTests/Kern.LightingTests.csproj -- transport
dotnet run --project tools/Kern.TerrainRasterTests/RunRasterTests/RunRasterTests.csproj

echo "================================================="
echo "===       ALL UNIFIED CI CHECKS PASSED        ==="
echo "================================================="
