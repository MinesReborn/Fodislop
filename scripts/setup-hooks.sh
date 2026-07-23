#!/usr/bin/env bash
# Setup version-controlled git hooks (.githooks)

set -e

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

echo "Setting git core.hooksPath to .githooks..."
git config core.hooksPath .githooks

chmod +x .githooks/* 2>/dev/null || true
chmod +x scripts/pre-commit-lint.sh 2>/dev/null || true

echo "Git hooks setup completed successfully!"
echo "Tracked hooks in .githooks/ will now run automatically on git commit."
