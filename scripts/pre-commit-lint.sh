#!/bin/bash
# Pre-commit hook to lint C# code in Unity project using Roslyn analyzers and check compilation errors

set -e

# Use current environment HOME or fallback to user home directory
export HOME="${HOME:-~}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME}"

echo "=== C# Pre-Commit & CI/CD Analyzer Check ==="
echo "Environment: CI=${CI:-false}, OS=$(uname -s), HOME=$HOME"

# Build all sub-projects first so DLL references in Temp/bin/Debug exist before Assembly-CSharp build
DEPENDENCIES=(
    "Effekseer.csproj"
    "EffekseerEditor.csproj"
    "Effekseer.URP.csproj"
    "UniTask.csproj"
    "UniTask.Linq.csproj"
    "UniTask.Editor.csproj"
    "UniTask.DOTween.csproj"
    "UniTask.Addressables.csproj"
    "UniTask.TextMeshPro.csproj"
    "McpUnity.Editor.csproj"
)

echo "--- Step 1: Building sub-project dependencies ---"
for DEPENDENCY in "${DEPENDENCIES[@]}"; do
    if [ -f "$DEPENDENCY" ]; then
        echo "Building $DEPENDENCY..."
        dotnet build "$DEPENDENCY" -clp:NoSummary >/dev/null 2>&1 || true
    fi
done

# Find all generated Assembly-CSharp project files
PROJECTS=$(find . -maxdepth 1 -name "Assembly-CSharp*.csproj")

if [ -z "$PROJECTS" ]; then
    echo "Notice: No Assembly-CSharp*.csproj files found in repository root."
    echo "Skipping C# Roslyn analyzer checks."
    exit 0
fi

echo "--- Step 2: Analyzing Assembly-CSharp projects ---"
HAS_WARNINGS=0
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

for PROJECT_FILE in $PROJECTS; do
    PROJECT_NAME=$(basename "$PROJECT_FILE")
    LOG_FILE="$TMP_DIR/$PROJECT_NAME.log"

    echo "Running full C# Roslyn analyzer check for $PROJECT_NAME..."

    # Build sequentially and capture all build output
    dotnet build "$PROJECT_FILE" -maxcpucount -p:UseSharedCompilation=true -nodeReuse:true -clp:NoSummary > "$LOG_FILE" 2>&1 || true

    if [ -f "$LOG_FILE" ]; then
        BUILD_LOG=$(cat "$LOG_FILE")

        # Only catch errors in user codebase (Assets/Scripts or Assets/Editor)
        PROJECT_ERRORS=$(echo "$BUILD_LOG" | grep -E ": error " | grep -E "(^|/|\\\\)Assets/(Scripts|Editor)/" || true)

        # Only catch warnings in user codebase (Assets/Scripts or Assets/Editor)
        # Exclude vendored VContainer runtime from linting
        PROJECT_WARNINGS=$(echo "$BUILD_LOG" | grep -E ": warning " | grep -E "(^|/|\\\\)Assets/(Scripts|Editor)/" | grep -v "Assets/Scripts/VContainer/" || true)

        if [ -n "$PROJECT_ERRORS" ]; then
            echo -e "\n\033[0;31mError: Compilation failed for $PROJECT_NAME in user codebase:\033[0m"
            echo "$PROJECT_ERRORS"
            HAS_WARNINGS=1

            echo -e "\n--- Detailed log for $PROJECT_NAME ---"
            echo "$BUILD_LOG"
            echo "---------------------------------------"
        fi

        if [ -n "$PROJECT_WARNINGS" ]; then
            echo -e "\n\033[0;31mError: Linters detected warnings in $PROJECT_NAME codebase:\033[0m"
            echo "$PROJECT_WARNINGS"
            HAS_WARNINGS=1

            if [ "$CI" = "true" ]; then
                echo -e "\n--- Detailed log for $PROJECT_NAME (CI Mode) ---"
                echo "$BUILD_LOG"
                echo "---------------------------------------------------"
            fi
        fi
    fi
done

if [ "$HAS_WARNINGS" -eq 1 ]; then
    echo -e "\n\033[0;31mPlease fix all compilation errors and analyzer warnings before committing.\033[0m"
    exit 1
fi

echo "All C# Roslyn analyzer checks passed successfully!"
exit 0
