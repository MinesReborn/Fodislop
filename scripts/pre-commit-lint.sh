#!/bin/bash
# Pre-commit hook to lint C# code in Unity project using Roslyn analyzers and check compilation errors

set -e

# Use current environment HOME or fallback to user home directory
export HOME="${HOME:-~}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME}"

# Build all sub-projects first so DLL references in Temp/bin/Debug exist before Assembly-CSharp build
for DEPENDENCY in Effekseer.csproj EffekseerEditor.csproj Effekseer.URP.csproj UniTask.csproj UniTask.Linq.csproj UniTask.Editor.csproj UniTask.DOTween.csproj UniTask.Addressables.csproj UniTask.TextMeshPro.csproj McpUnity.Editor.csproj; do
    if [ -f "$DEPENDENCY" ]; then
        dotnet build "$DEPENDENCY" -clp:NoSummary >/dev/null 2>&1 || true
    fi
done

# Find all generated Assembly-CSharp project files
PROJECTS=$(find . -maxdepth 1 -name "Assembly-CSharp*.csproj")

if [ -z "$PROJECTS" ]; then
    echo -e "\033[0;31mError: No Assembly-CSharp*.csproj files found in repository root.\033[0m"
    echo "Please open the project in Unity Editor to generate C# project files."
    if [ "$CI" = "true" ] || [ "$STRICT_LINT" = "1" ]; then
        exit 1
    fi
    echo "Skipping C# Roslyn analyzer checks for this commit."
    exit 0
fi

HAS_WARNINGS=0
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

# Build projects sequentially to prevent MSBuild race conditions during CS0006 DLL file linking
for PROJECT_FILE in $PROJECTS; do
    PROJECT_NAME=$(basename "$PROJECT_FILE")
    LOG_FILE="$TMP_DIR/$PROJECT_NAME.log"

    echo "Running full C# Roslyn analyzer check for $PROJECT_NAME..."

    # Build sequentially with shared compilation
    dotnet build "$PROJECT_FILE" -maxcpucount -p:UseSharedCompilation=true -nodeReuse:true -clp:NoSummary > "$LOG_FILE" 2>&1 || true

    if [ -f "$LOG_FILE" ]; then
        BUILD_LOG=$(cat "$LOG_FILE")

        # All compilation errors in user codebase (Assets/Scripts or Assets/Editor)
        PROJECT_ERRORS=$(echo "$BUILD_LOG" | grep -E ": error " | grep -E "(^|/|\\\\)Assets/(Scripts|Editor)/" || echo "$BUILD_LOG" | grep -E ": error CS" || true)

        # All warnings in user codebase (Assets/Scripts or Assets/Editor)
        PROJECT_WARNINGS=$(echo "$BUILD_LOG" | grep -E ": warning " | grep -E "(^|/|\\\\)Assets/(Scripts|Editor)/" || true)

        if [ -n "$PROJECT_ERRORS" ]; then
            echo -e "\n\033[0;31mError: Compilation failed for $PROJECT_NAME:\033[0m"
            echo "$PROJECT_ERRORS"
            HAS_WARNINGS=1
        fi

        if [ -n "$PROJECT_WARNINGS" ]; then
            echo -e "\n\033[0;31mError: Linters detected warnings in $PROJECT_NAME codebase:\033[0m"
            echo "$PROJECT_WARNINGS"
            HAS_WARNINGS=1
        fi
    fi
done

if [ "$HAS_WARNINGS" -eq 1 ]; then
    echo -e "\n\033[0;31mPlease fix all compilation errors and analyzer warnings before committing.\033[0m"
    exit 1
fi

echo "All C# Roslyn analyzer checks passed successfully!"
exit 0
