type Values = Record<string, unknown>;
const record = (value: unknown): Values => value !== null && typeof value === 'object' ? value as Values : {};
const text = (value: unknown): string => value === null || value === undefined ? '' : String(value);

/** One visible entry per ability ID; provider eligibility comes from C#. */
export function mountAbilityMenu(root: Element, triggerHost: Element, command: (action: string, fields?: Values) => void): {
  update(state: Values): void; dispose(): void;
} {
  const toggle = document.createElement('button'); toggle.type = 'button'; toggle.textContent = 'Abilities';
  toggle.dataset.abilitiesToggle = 'true'; toggle.setAttribute('aria-expanded', 'false');
  const panel = document.createElement('section'); panel.hidden = true; panel.dataset.abilityMenu = 'true';
  panel.dataset.rustyUiInteractive = 'true'; panel.setAttribute('aria-label', 'Party abilities');
  panel.style.cssText = 'position:fixed;bottom:190px;left:240px;z-index:5;max-height:50vh;width:min(440px,65vw);overflow:auto;padding:14px;background:#1c241cf5;border:1px solid #ae9666;border-radius:6px;color:#eee6d5;font:14px/1.4 system-ui;pointer-events:auto';
  const title = document.createElement('strong'); title.textContent = 'Party abilities';
  const close = document.createElement('button'); close.type = 'button'; close.textContent = 'Close'; close.style.cssText = 'float:right';
  const targetLabel = document.createElement('label'); targetLabel.textContent = 'Ally target '; targetLabel.style.cssText = 'display:block;margin:10px 0';
  const ally = document.createElement('select'); ally.dataset.abilityAlly = 'true'; targetLabel.append(ally);
  const note = document.createElement('p'); note.textContent = 'Hostile abilities attack forward. Each ready provider acts and recovers independently.';
  const list = document.createElement('div'); list.style.cssText = 'display:grid;gap:8px';
  panel.append(title, close, note, targetLabel, list); root.append(panel); triggerHost.append(toggle);
  const setOpen = (open: boolean): void => { panel.hidden = !open; toggle.setAttribute('aria-expanded', String(open)); if (!open) toggle.blur(); };
  toggle.addEventListener('click', () => { setOpen(panel.hidden); toggle.blur(); }); close.addEventListener('click', () => setOpen(false));
  ally.addEventListener('change', () => command('select', { member: ally.value }));
  const buttons = new Map<string, HTMLButtonElement>(); let rosterSignature = '';
  panel.addEventListener('keydown', event => { if (event.code === 'Escape') { event.preventDefault(); setOpen(false); } event.stopPropagation(); });
  panel.addEventListener('keyup', event => event.stopPropagation());
  return {
    update(state) {
      const combat = record(state.combat), party = record(state.party);
      const signature = JSON.stringify(Object.entries(party).map(([id, raw]) => [id, record(raw).name]));
      if (signature !== rosterSignature) {
        rosterSignature = signature; ally.replaceChildren();
        for (const [id, raw] of Object.entries(party)) { const option = document.createElement('option'); option.value = id; option.textContent = text(record(raw).name); ally.append(option); }
      }
      if (document.activeElement !== ally) ally.value = text(state.selectedMember);
      const present = new Set<string>();
      for (const [id, raw] of Object.entries(record(combat.abilities))) {
        present.add(id); const ability = record(raw);
        let button = buttons.get(id);
        if (!button) {
          button = document.createElement('button'); button.type = 'button'; button.dataset.partyAbility = id;
          button.style.cssText = 'white-space:pre-line;text-align:left;padding:9px;color:#fff0c8;background:#35412f;border:1px solid #95805e;border-radius:3px';
          button.addEventListener('click', () => command('ability', { spell: id, member: ally.value }));
          buttons.set(id, button); list.append(button);
        }
        button.textContent = `${text(ability.name)} · ${text(ability.eligible)}/${text(ability.total)} ready${Number(ability.shared) === 1 ? '\nOne shared party effect' : ''}`;
        button.disabled = Number(ability.eligible) === 0 || Number(state.paused) === 1 || Number(combat.defeated) === 1;
        button.title = Object.entries(record(ability.providers)).map(([memberId, rawProvider]) => {
          const provider = record(rawProvider); const action = record(record(combat.members)[memberId]);
          const cooldown = Number(action.remaining) > 0 ? ` · ${text(action.phase)} ${Number(action.remaining).toFixed(1)}s` : '';
          return `${text(record(party[memberId]).name)}: ${text(provider.reason)}${cooldown}`;
        }).join('\n');
      }
      for (const [id, button] of buttons) if (!present.has(id)) { button.remove(); buttons.delete(id); }
      toggle.disabled = present.size === 0;
    },
    dispose() { panel.remove(); toggle.remove(); },
  };
}
