#!/usr/bin/env bash
set -uo pipefail
cd "$(dirname "$0")/.."
failures=0
check_lane() {
  local name="$1"
  shift
  if "$@"; then
    echo "$name: PASS"
  else
    echo "$name: FAIL" >&2
    failures=$((failures + 1))
    return 1
  fi
}
build_ok=false
if check_lane build dotnet build RustyRifles.slnx -c Release; then build_ok=true; fi
check_lane ui pnpm run check:ui
if $build_ok; then
  check_lane procgen dotnet run --project tests/Procgen -c Release --no-build
  check_lane game dotnet run --project tests/Game -c Release --no-build
else
  echo "procgen/game: SKIPPED (build failed; stale executables are not evidence)"
fi
check_lane staging dotnet msbuild src/Rifles.Game -t:StageRustyEngineCoreClrProduct -p:Configuration=Release
echo "Check lanes: 5, failures: $failures"
[[ "$failures" -eq 0 ]]
