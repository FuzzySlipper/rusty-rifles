#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ $# != 2 ]]; then
  echo "Usage: $0 /absolute/path/to/runtime-pack /absolute/path/to/Rusty.Engine.nupkg" >&2
  exit 2
fi
python3 - "$1" "$2" <<'PY'
from pathlib import Path
import json, shutil, sys, xml.etree.ElementTree as ET, zipfile
runtime, package = (Path(value).resolve() for value in sys.argv[1:])
version = ET.parse('src/Rifles.Game/Rifles.Game.csproj').findtext('.//RustyEngineSdkPackageVersion')
revision = version.split('dev.', 1)[1]
manifest = json.loads((runtime / 'runtime-manifest.json').read_text())
if not manifest['sourceRevision'].startswith(revision):
    raise SystemExit('Runtime revision does not match the game SDK version.')
with zipfile.ZipFile(package) as archive:
    spec = ET.fromstring(archive.read('Rusty.Engine.nuspec'))
    metadata = {e.tag.split('}')[-1]: e.text for e in spec.iter()}
    if metadata.get('id') != 'Rusty.Engine' or metadata.get('version') != version:
        raise SystemExit('SDK package identity does not match the game.')
destination = Path('.runtime') / ('pair-' + revision) / 'runtime-pack'
feed = Path('.runtime/sdk-feed')
if destination.exists():
    raise SystemExit(f'{destination} already exists; move it aside before installing a replacement.')
destination.parent.mkdir(parents=True, exist_ok=True)
feed.mkdir(parents=True, exist_ok=True)
shutil.copytree(runtime, destination)
shutil.copy2(package, feed / f'Rusty.Engine.{version}.nupkg')
print(f'Installed Engine {version}')
PY
