#!/usr/bin/env bash
# Pre-commit hook to execute the unified Kern CI pipeline locally
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$DIR/run-ci.sh"
