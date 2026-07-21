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

for PROJECT_FILE in $PROJECTS; do
    PROJECT_NAME=$(basename "$PROJECT_FILE")

    echo "Running C# Roslyn analyzers for $PROJECT_NAME via dotnet build..."

    # Run build and capture output
    BUILD_LOG=$(dotnet build "$PROJECT_FILE" --no-incremental 2>&1 || true)

    # Search for compilation errors
    PROJECT_ERRORS=$(echo "$BUILD_LOG" | grep -Ei "error CS[0-9]+" || true)

    # Search for all warnings in the entire codebase (Assets/Scripts or Assets/Editor)
    PROJECT_WARNINGS=$(echo "$BUILD_LOG" | grep -Ei "warning (SA|CA|RCS|UNT)[0-9]+" | grep -E "/Assets/(Scripts|Editor)/" || true)

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
done

if [ "$HAS_WARNINGS" -eq 1 ]; then
    echo -e "\n\033[0;31mPlease fix all compilation errors and analyzer warnings before committing.\033[0m"
    exit 1
fi

echo "All C# Roslyn analyzer checks passed successfully!"
exit 0
