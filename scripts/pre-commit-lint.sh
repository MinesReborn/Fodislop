#!/bin/bash
# Pre-commit hook to lint C# code in Unity project using Roslyn analyzers and check compilation errors

set -e

export HOME="${HOME:-/Users/murasama}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/Users/murasama}"

# Find all generated Assembly-CSharp project files
PROJECTS=$(find . -maxdepth 1 -name "Assembly-CSharp*.csproj")

if [ -z "$PROJECTS" ]; then
    echo "Warning: Assembly-CSharp*.csproj files not found."
    echo "Please open the project in Unity Editor first to generate C# project files."
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

        # All compilation errors
        PROJECT_ERRORS=$(echo "$BUILD_LOG" | grep -E ": error " | grep -E "/Assets/(Scripts|Editor)/" || true)

        # All warnings from any analyzer or compiler in Assets/Scripts or Assets/Editor
        PROJECT_WARNINGS=$(echo "$BUILD_LOG" | grep -E ": warning " | grep -E "/Assets/(Scripts|Editor)/" || true)

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
