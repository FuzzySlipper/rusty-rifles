// Compiles the DOM companion (src/ui) against the Engine pair's own UI
// declarations. The product build passes $(RustyEngineProductUiTypes); without
// it (pnpm check:ui) the script asks MSBuild for the same property. Extra
// arguments go to tsc (check:ui passes --noEmit). Node runs the repository's
// own TypeScript, so the command is the same under cmd.exe and sh.
//
// usage: node scripts/build-ui.mjs [<rusty-engine-product-ui.d.ts>] [tsc options]
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join, resolve } from 'node:path';

const repoRoot = resolve(import.meta.dirname, '..');
const args = process.argv.slice(2);
const given = args[0]?.startsWith('-') ? '' : (args.shift() ?? '');
const tscOptions = args;
const types = given || execFileSync('dotnet', ['msbuild', join(repoRoot, 'src', 'Rifles.Game', 'Rifles.Game.csproj'),
  '-nologo', '-getProperty:RustyEngineProductUiTypes'], { encoding: 'utf8' }).trim();
if (!existsSync(types)) {
  console.error('Engine UI declarations are missing; run rusty install and dotnet restore.');
  process.exit(1);
}
const generated = join(repoRoot, 'src', 'ui', 'generated');
mkdirSync(generated, { recursive: true });
const config = join(generated, 'tsconfig.json');
writeFileSync(config, JSON.stringify({ extends: '../tsconfig.json', files: [resolve(types)], include: ['../*.ts'] }, null, 2) + '\n');
const tsc = createRequire(join(repoRoot, 'package.json')).resolve('typescript/bin/tsc');
try {
  execFileSync(process.execPath, [tsc, '--project', config, ...tscOptions], { stdio: 'inherit', cwd: repoRoot });
} catch (error) {
  process.exit(error.status ?? 1);
}
