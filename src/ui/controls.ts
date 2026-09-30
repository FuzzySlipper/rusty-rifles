export const controls = [
  ['KeyW', 'W/S step', 'forward'], ['KeyS', '', 'backward'], ['KeyA', 'A/D sidestep', 'strafe-left'], ['KeyD', '', 'strafe-right'],
  ['KeyQ', 'Q/E turn', 'turn-left'], ['KeyE', '', 'turn-right'], ['Space', 'Space fire', 'fire'], ['KeyV', 'V melee', 'melee'],
  ['KeyB', 'B fix bayonets', 'fix-bayonets'], ['KeyN', 'N unfix bayonets', 'unfix-bayonets'], ['KeyC', 'C charge', 'charge'],
  ['KeyR', 'R reload', 'reload'], ['KeyF', 'F use', 'use'], ['KeyT', 'T cycle interactable', 'cycle'],
  ['KeyP', 'P pause', 'pause'], ['KeyK', 'K save', 'save'], ['KeyL', 'L load', 'load'],
] as const;
export const gameplayKeys = new Set<string>(controls.map(([key]) => key));
export const controlHelp = controls.map(([, help]) => help).filter(Boolean).join(' · ');

export function controlShortcut(action: string): string {
  const code = controls.find(([, , binding]) => binding === action)?.[0];
  return code?.startsWith('Key') ? code.slice(3) : code ?? '';
}
