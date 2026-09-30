#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
ui_types="${1:-}"
if [[ $# -gt 0 ]]; then shift; fi
if [[ -z "$ui_types" ]]; then
  ui_types="$(dotnet msbuild src/Rifles.Game/Rifles.Game.csproj -nologo -getProperty:RustyEngineProductUiTypes)"
fi
if [[ ! -f "$ui_types" ]]; then
  echo "Engine UI declarations are missing; run rusty install and dotnet restore." >&2
  exit 1
fi
mkdir -p src/ui/generated
node --input-type=module - "$ui_types" <<'JS'
import { writeFileSync } from 'node:fs';
writeFileSync('src/ui/generated/tsconfig.json', JSON.stringify({
  extends: '../tsconfig.json', files: [process.argv[2]], include: ['../*.ts']
}, null, 2) + '\n');
JS
pnpm exec tsc --project src/ui/generated/tsconfig.json "$@"
