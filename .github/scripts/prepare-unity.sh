#!/usr/bin/env bash
set -euo pipefail

curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh |
  UNITY_CLI_CHANNEL=beta bash

if [[ "$(uname -s)" == Darwin ]]; then
  unity_bin="$HOME/.unity/bin"
else
  unity_bin="$HOME/.local/bin"
fi

echo "$unity_bin" >> "$GITHUB_PATH"
export PATH="$unity_bin:$PATH"
unity --version

if [[ -n "${UNITY_LICENSE:-}" ]]; then
  license_file="$RUNNER_TEMP/kern-ci.ulf"
  printf '%s' "$UNITY_LICENSE" > "$license_file"
  unity license activate --file "$license_file"
elif [[ -n "${UNITY_SERIAL:-}" ]]; then
  unity license activate --serial "$UNITY_SERIAL"
else
  echo '::error::Unity validation requires UNITY_LICENSE or UNITY_SERIAL in repository Actions secrets.'
  exit 1
fi
