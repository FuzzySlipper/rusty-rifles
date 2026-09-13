#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
pnpm run check:ui
dotnet build RustyRifles.slnx -c Release
dotnet run --project tests/Procgen -c Release --no-build
dotnet run --project tests/Game -c Release --no-build
dotnet run --project src/Rifles.Procgen.Tool -c Release --no-build -- --self-check
dotnet msbuild src/Rifles.Game -t:StageRustyEngineCoreClrProduct -p:Configuration=Release
