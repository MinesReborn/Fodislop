#!/bin/bash
# Pre-commit hook to lint C# code in Unity project using Roslyn analyzers and check compilation errors

set -e

# Use current environment HOME or fallback to user home directory
export HOME="${HOME:-~}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME}"

# Build dependency projects first so Temp/bin/Debug references exist for Assembly-CSharp
if [ -f "Effekseer.csproj" ]; then
    dotnet build Effekseer.csproj -clp:NoSummary >/dev/null 2>&1 || true
fi

if [ -f "UniTask.csproj" ]; then
    dotnet build UniTask.csproj -clp:NoSummary >/dev/null 2>&1 || true
fi

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

PIDS=()
PROJ_LIST=()

for PROJECT_FILE in $PROJECTS; do
    PROJECT_NAME=$(basename "$PROJECT_FILE")
    PROJ_LIST+=("$PROJECT_NAME")
    LOG_FILE="$TMP_DIR/$PROJECT_NAME.log"

    echo "Running full C# Roslyn analyzer check for $PROJECT_NAME..."

    # Run full --no-incremental analysis in parallel using shared Roslyn compiler server & all CPU cores
    (
        dotnet build "$PROJECT_FILE" --no-incremental -maxcpucount -p:UseSharedCompilation=true -nodeReuse:true -clp:NoSummary > "$LOG_FILE" 2>&1
    ) &
    PIDS+=($!)
done

# Wait for all parallel analyzer jobs to complete
for i in "${!PIDS[@]}"; do
    wait "${PIDS[$i]}" || true
    PROJECT_NAME="${PROJ_LIST[$i]}"
    LOG_FILE="$TMP_DIR/$PROJECT_NAME.log"

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
