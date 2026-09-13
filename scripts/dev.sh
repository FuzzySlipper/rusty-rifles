#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
runtime=".runtime/pair-2e4255bd3ad5/runtime-pack"
if [[ ! -x "$runtime/bin/rusty" ]]; then
  echo "Install the paired Engine runtime and SDK with scripts/install-engine.sh first." >&2
  exit 1
fi
exec "$runtime/bin/rusty" dev --runtime "$runtime" --project src/Rifles.Game/Rifles.Game.csproj --live-debug "$@"
