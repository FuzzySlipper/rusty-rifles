type Values = Record<string, unknown>;
const record = (value: unknown): Values => value !== null && typeof value === 'object' ? value as Values : {};
const text = (value: unknown): string => value === undefined || value === null ? '' : String(value);

/** Paused draft editor. C# owns the draft, validation, timing and final commit. */
export function mountFormationPlanner(root: Element, command: (action: string, fields?: Values) => void): {
  update(state: Values): void; dispose(): void;
} {
  const overlay = document.createElement('section');
  overlay.dataset.formationPlanner = 'true'; overlay.dataset.rustyUiInteractive = 'true';
  overlay.setAttribute('role', 'dialog'); overlay.setAttribute('aria-modal', 'true');
  overlay.setAttribute('aria-label', 'Change formation');
  overlay.style.cssText = 'position:fixed;inset:6vh max(5vw,20px);z-index:80;overflow:auto;background:#171c18fa;border:2px solid #b69b66;border-radius:8px;padding:24px;color:#eee6d5;font:16px/1.4 system-ui;box-shadow:0 0 0 100vmax #000a;pointer-events:auto';
  const heading = document.createElement('h2'); heading.textContent = 'Change formation';
  const instructions = document.createElement('p');
  instructions.textContent = 'Time is paused. Select a soldier, then a position to move or exchange places. The commander stays in the center.';
  const grid = document.createElement('div'); grid.dataset.formationDraft = 'true';
  grid.style.cssText = 'display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;max-width:900px;margin:20px auto';
  const pending = document.createElement('output'); pending.style.cssText = 'display:block;margin:12px 0';
  const controls = document.createElement('div'); controls.style.cssText = 'display:flex;gap:12px';
  const button = (label: string, action: string): HTMLButtonElement => {
    const result = document.createElement('button'); result.type = 'button'; result.textContent = label;
    result.style.cssText = 'font:inherit;padding:10px 24px;background:#3e4936;color:#fff4d0;border:1px solid #b69b66;border-radius:4px';
    result.addEventListener('click', () => command(action)); return result;
  };
  const execute = button('Execute', 'formation-execute'); execute.dataset.formationExecute = 'true';
  const cancel = button('Cancel', 'formation-cancel'); cancel.dataset.formationCancel = 'true';
  controls.append(execute, cancel); overlay.append(heading, instructions, grid, pending, controls);
  overlay.hidden = true; root.append(overlay);
  let selected = ''; let previous = ''; let current: Values = {};

  const render = (): void => {
    const planner = record(current.formation);
    const open = Number(planner.open) === 1;
    const wasHidden = overlay.hidden; overlay.hidden = !open;
    if (!open) { selected = ''; previous = ''; return; }
    const party = record(current.party), draft = record(planner.draft);
    const signature = JSON.stringify([current.party, current.positions, draft, selected]);
    if (signature !== previous) {
      previous = signature; grid.replaceChildren();
      const positions = Object.entries(record(current.positions)).sort(([, left], [, right]) => {
        const a = record(left), b = record(right);
        return Number(b.offsetForward) - Number(a.offsetForward) || Number(b.offsetLeft) - Number(a.offsetLeft);
      });
      for (const [positionId, raw] of positions) {
        const position = record(raw);
        const memberId = Object.keys(draft).find(id => draft[id] === positionId) ?? '';
        const member = record(party[memberId]);
        const center = Number(position.offsetForward) === 0 && Number(position.offsetLeft) === 0;
        const living = Number(member.vitality) > 0;
        const cell = document.createElement('button'); cell.type = 'button';
        cell.dataset.formationPosition = positionId;
        cell.style.cssText = `min-height:110px;white-space:pre-line;font:inherit;border:2px solid ${memberId && memberId === selected ? '#ffe197' : '#776e56'};border-radius:6px;background:${center ? '#4b3a20' : '#283128'};color:#f0e7ce;padding:12px`;
        cell.textContent = center
          ? `${text(member.name) || 'Commander'}\n${text(member.vitality)}/${text(member.maximumVitality)} health\nFixed center`
          : `${text(position.name)}\n${memberId ? text(member.name) : 'Empty'}${memberId ? `\n${text(member.vitality)}/${text(member.maximumVitality)} health` : ''}`;
        cell.disabled = center || (!!memberId && !living);
        cell.addEventListener('click', () => {
          if (selected) { command('formation-place', { member: selected, position: positionId }); selected = ''; }
          else if (memberId) selected = memberId;
          render();
        });
        grid.append(cell);
      }
    }
    pending.textContent = `${text(planner.changed)} soldiers will reposition over ${text(planner.duration)}s. The party cannot move or turn during execution; affected soldiers cannot act.`;
    if (wasHidden) cancel.focus();
  };
  const guard = (event: KeyboardEvent): void => {
    if (event.code === 'Escape' && event.type === 'keydown') { event.preventDefault(); command('formation-cancel'); }
    event.stopPropagation();
  };
  overlay.addEventListener('keydown', guard); overlay.addEventListener('keyup', guard);
  return { update(state) { current = state; render(); }, dispose() { overlay.remove(); } };
}
