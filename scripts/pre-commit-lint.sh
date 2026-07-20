#!/bin/bash
# Pre-commit hook to lint C# code in Unity project using Roslyn analyzers

set -e

# Find all generated Assembly-CSharp project files
PROJECTS=$(find . -maxdepth 1 -name "Assembly-CSharp*.csproj")

if [ -z "$PROJECTS" ]; then
    echo "Warning: Assembly-CSharp*.csproj files not found."
    echo "Please open the project in Unity Editor first to generate C# project files."
    echo "Skipping C# Roslyn analyzer checks for this commit."
    exit 0
fi

# Get staged C# files (ignoring whitespace-only modifications)
STAGED_FILES=$(git diff --cached -w --name-only --diff-filter=ACM | grep '\.cs$' || true)

if [ -z "$STAGED_FILES" ]; then
    echo "No staged C# files to analyze."
    exit 0
fi

HAS_WARNINGS=0

for PROJECT_FILE in $PROJECTS; do
    PROJECT_NAME=$(basename "$PROJECT_FILE")
    echo "Running C# Roslyn analyzers for $PROJECT_NAME via dotnet build..."

    # Run build and capture output (disable incremental compilation to check all files)
    BUILD_LOG=$(dotnet build "$PROJECT_FILE" --no-incremental 2>&1 || true)

    # Search for warnings in staged files
    STAGED_WARNINGS=""
    for FILE in $STAGED_FILES; do
        FILE_NAME=$(basename "$FILE")
        # Match warning for this specific file name
        FILE_WARNINGS=$(echo "$BUILD_LOG" | grep -Ei "warning (SA|CA|RCS|UNT)[0-9]+" | grep "$FILE_NAME" || true)
        if [ -n "$FILE_WARNINGS" ]; then
            STAGED_WARNINGS="${STAGED_WARNINGS}${FILE_WARNINGS}"$'\n'
        fi
    done

    if [ -n "$STAGED_WARNINGS" ]; then
        echo -e "\n\033[0;31mError: Linters detected warnings in staged files for $PROJECT_NAME:\033[0m"
        echo "$STAGED_WARNINGS"
        HAS_WARNINGS=1
    fi
done

if [ "$HAS_WARNINGS" -eq 1 ]; then
    echo -e "\n\033[0;31mPlease fix all analyzer warnings (SA, CA, RCS, UNT) before committing.\033[0m"
    exit 1
fi

echo "All C# Roslyn analyzer checks passed successfully!"
exit 0
