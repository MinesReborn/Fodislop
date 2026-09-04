#!/bin/bash
# Pre-commit hook to lint C# code in Unity project using Roslyn analyzers and check compilation errors

set -e

# Keep dotnet's first-run state outside the repository when the caller has not
# supplied a location. Never overwrite HOME: it is unrelated to this check.
LINT_DOTNET_HOME="${DOTNET_CLI_HOME:-${TMPDIR:-/tmp}/fodislop-dotnet-cli}"
export DOTNET_CLI_HOME="$LINT_DOTNET_HOME"

echo "=== C# Local Analyzer Check ==="
echo "Environment: CI=${CI:-false}, OS=$(uname -s), DOTNET_CLI_HOME=$DOTNET_CLI_HOME"

echo "--- Step 0: Auditing project architecture and settings invariants ---"
node "$(dirname "$0")/check-architecture.js"

# Настройки описываются атрибутами и читаются рефлексией: ни компилятор, ни
# линтер не могут сказать, что диапазон над полем осмыслен, что значение по
# умолчанию в него попадает и что ветка разбора для этого типа существует.
# Проба исполняет эту логику вне Unity на настоящих файлах проекта. Мотиватор:
# `case int number when field.Range != null` компилировался безупречно и ронял
# запуск игры на штатном разрешении экрана.
echo "--- Step 0.1: Executing settings schema probe ---"
if command -v dotnet >/dev/null 2>&1; then
    DOTNET_NOLOGO=1 dotnet run --project "$(dirname "$0")/../tools/Fodinae.SettingsProbe" \
        --verbosity quiet -- "$(dirname "$0")/.."
else
    echo "Notice: dotnet not found; settings probe skipped."
fi

if [ "$CI" != "true" ]; then
    echo "Notice: local hooks run fast static checks only."
    echo "Unity compile, EditMode, PlayMode, and IL2CPP validation are mandatory CI jobs."
    exit 0
fi

ensure_restore_assets() {
    local project_file="$1"
    local project_name
    local assets_file

    project_name="$(basename "$project_file" .csproj)"
    assets_file="Temp/obj/$project_name/project.assets.json"
    if [ -f "$assets_file" ]; then
        return 0
    fi

    echo "Missing NuGet assets: $assets_file, restoring..."
    if ! dotnet restore "$project_file" --ignore-failed-sources --disable-parallel >/dev/null 2>&1; then
        echo "Auto-restore failed for $project_file"
        echo "Run manually: dotnet restore $project_file --ignore-failed-sources --disable-parallel"
        exit 1
    fi

    if [ ! -f "$assets_file" ]; then
        echo "NuGet assets still missing after restore: $assets_file"
        exit 1
    fi
}

# Build all sub-projects first so DLL references in Temp/bin/Debug exist before Assembly-CSharp build
DEPENDENCIES=(
    "Effekseer.csproj"
    "EffekseerEditor.csproj"
    "Effekseer.URP.csproj"
    "UniTask.csproj"
    "UniTask.Linq.csproj"
    "UniTask.DOTween.csproj"
    "UniTask.Addressables.csproj"
    "UniTask.TextMeshPro.csproj"
    "McpUnity.Editor.csproj"
)

echo "--- Step 1: Building sub-project dependencies ---"
for DEPENDENCY in "${DEPENDENCIES[@]}"; do
    if [ ! -f "$DEPENDENCY" ]; then
        continue
    fi
    if ! dotnet restore "$DEPENDENCY" --ignore-failed-sources --disable-parallel >/dev/null 2>&1; then
        echo "Skipping $DEPENDENCY: restore failed (likely missing targeting pack on this platform)"
        continue
    fi
    echo "Building $DEPENDENCY..."
    if ! dotnet build "$DEPENDENCY" --no-restore -maxcpucount:1 -p:UseSharedCompilation=false -nodeReuse:false -clp:NoSummary >/dev/null 2>&1; then
        echo "Skipping $DEPENDENCY: build failed (likely missing targeting pack on this platform)"
        continue
    fi
done

# Build the runtime project before editor projects. The editor assembly references
# Assembly-CSharp.dll, so filesystem-dependent find order can otherwise validate
# editor code against a stale runtime assembly and report false missing members.
PROJECTS=()
for PROJECT_FILE in \
    "./Fodinae.Runtime.csproj" \
    "./Fodinae.Editor.csproj" \
    "./Fodinae.Tests.Editor.csproj"; do
    if [ -f "$PROJECT_FILE" ]; then
        PROJECTS+=("$PROJECT_FILE")
    fi
done

if [ "${#PROJECTS[@]}" -eq 0 ]; then
    echo "Notice: No Assembly-CSharp*.csproj files found in repository root."
    echo "Skipping C# Roslyn analyzer checks."
    exit 0
fi

echo "--- Step 2: Analyzing Assembly-CSharp projects ---"
HAS_WARNINGS=0
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

for PROJECT_FILE in "${PROJECTS[@]}"; do
    PROJECT_NAME=$(basename "$PROJECT_FILE")
    LOG_FILE="$TMP_DIR/$PROJECT_NAME.log"

    ensure_restore_assets "$PROJECT_FILE"

    echo "Running full C# Roslyn analyzer check for $PROJECT_NAME..."

    # Build sequentially and capture all build output
    if ! dotnet build "$PROJECT_FILE" --no-restore --no-dependencies -maxcpucount:1 -p:UseSharedCompilation=false -nodeReuse:false -clp:NoSummary > "$LOG_FILE" 2>&1; then
        HAS_WARNINGS=1
        echo "Build failed for $PROJECT_NAME; full output follows:"
        cat "$LOG_FILE"
        continue
    fi

    if [ -f "$LOG_FILE" ]; then
        BUILD_LOG=$(cat "$LOG_FILE")

        # Only catch errors in user codebase (Assets/Scripts or Assets/Editor)
        PROJECT_ERRORS=$(echo "$BUILD_LOG" | grep -E ": error " | grep -E "(^|/|\\\\)Assets/(Scripts|Editor)/" || true)

        # Only catch warnings in user codebase (Assets/Scripts or Assets/Editor)
        # Exclude vendored VContainer from linting
        # In CI mode check all codebase warnings, locally check staged files
        ALL_WARNINGS=$(echo "$BUILD_LOG" | grep -E ": warning " | grep -E "(^|/|\\\\)Assets/(Scripts|Editor)/" | grep -v "Assets/Scripts/VContainer/" || true)
        PROJECT_WARNINGS=""

        if [ "$CI" = "true" ]; then
            PROJECT_WARNINGS="$ALL_WARNINGS"
        elif [ -n "$ALL_WARNINGS" ]; then
            STAGED_CS_FILES=$(git diff --cached --name-only --diff-filter=ACM -- '*.cs' | sed 's|/|\\|g' || true)
            if [ -n "$STAGED_CS_FILES" ]; then
                while IFS= read -r warning_line; do
                    WARN_FILE=$(echo "$warning_line" | grep -oE "Assets/[^(:]+" | head -1)
                    if [ -n "$WARN_FILE" ]; then
                        ESCAPED=$(echo "$WARN_FILE" | sed 's|/|\\|g')
                        if echo "$STAGED_CS_FILES" | grep -qF "$ESCAPED"; then
                            PROJECT_WARNINGS="${PROJECT_WARNINGS}${warning_line}"$'\n'
                        fi
                    fi
                done <<< "$ALL_WARNINGS"
                PROJECT_WARNINGS=$(echo "$PROJECT_WARNINGS" | sed '/^$/d')
            fi
        fi

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
