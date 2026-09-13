type Envelope = Readonly<{ value: unknown }>;
type UiContext = Readonly<{
  projection?: { current(): Envelope | null; subscribe(render: (value: Envelope | null) => void): () => void };
  intents?: { claim(intent: string, value: { kind: 'product-payload'; contract: string; data: Record<string, unknown> }): void };
}>;
function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
}
/** Commands are Engine input claims. Only projections change displayed game state. */
export function mountProductUi(root: Element, context: UiContext): Readonly<{ dispose(): void }> {
  const panel = document.createElement('aside');
  panel.setAttribute('aria-label', 'Expedition');
  panel.style.cssText = 'position:relative;margin:12px;padding:12px 16px;color:#eee6d5;background:#171914e8;border:1px solid #74694e;border-radius:5px;font:14px/1.5 system-ui;max-width:460px;pointer-events:auto';
  const title = document.createElement('strong'); title.textContent = 'Rusty Rifles';
  const help = document.createElement('p'); help.textContent = 'W/S step · A/D sidestep · Q/E turn · F use · R cycle · P pause · K save · L load';
  const status = document.createElement('output');
  const feedback = document.createElement('p'); feedback.setAttribute('role', 'status');
  const roster = document.createElement('div'); roster.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-top:10px';
  let state: Record<string, unknown> = {};
  const command = (action: string, extra: Record<string, unknown> = {}): void => {
    context.intents?.claim('rifles.command', {kind: 'product-payload', contract: 'rifles.command.v1',
      data: {revision: String(state.commandRevision), action, ...extra}});
  };
  const button = (label: string, action: () => void): HTMLButtonElement => {
    const element = document.createElement('button'); element.type = 'button'; element.textContent = label;
    element.style.cssText = 'background:#33392f;color:#eee6d5;border:1px solid #827556;padding:6px;margin:3px;cursor:pointer';
    element.addEventListener('click', action); return element;
  };
  const pause = button('Pause', () => command('pause'));
  const actions = document.createElement('nav'); actions.setAttribute('aria-label', 'Expedition controls');
  actions.append(pause, button('Save', () => command('save')), button('Load', () => command('load')), button('Restart', () => command('restart')));
  const focus = document.createElement('p');
  const use = button('Use feature', () => command('use', {target: Number(state.focusId), targetRevision: Number(state.focusRevision)}));
  actions.append(use);
  const art = document.createElement('details');
  const artTitle = document.createElement('summary'); artTitle.textContent = 'Art comparison';
  const artStatus = document.createElement('p');
  art.append(artTitle, artStatus, button('Switch treatment', () => command('art-style')),
    button('Move light', () => command('art-light')), button('Toggle room lights', () => command('art-fill')));
  panel.append(title, help, status, roster, actions, art, focus, feedback); root.append(panel);
  let previousRoster = '';
  const render = (envelope: Envelope | null): void => {
    if (!envelope) { status.textContent = 'Preparing the expedition…'; return; }
    state = record(envelope.value);
    artStatus.textContent = `${String(state.artStyle)} · Light ${String(state.lightPosition)} · ${state.roomLights === 1 ? "Room lights on" : "Room fill off"}`;
    status.textContent = `${String(state.status)} · ${String(state.facing)} · (${String(state.x)}, ${String(state.y)}) · ${Math.floor(Number(state.seconds))}s · Seed ${String(state.seed)}`;
    focus.textContent = String(state.focusLabel); use.disabled = !state.focusId || state.paused === 1;
    feedback.textContent = String(state.feedback); pause.textContent = state.paused === 1 ? 'Resume' : 'Pause';
    const members = record(state.party), signature = JSON.stringify([members, state.selectedMember]);
    if (signature === previousRoster) return;
    previousRoster = signature;
    const slots = ['FrontLeft', 'FrontRight', 'RearLeft', 'RearRight'];
    roster.replaceChildren(...Object.entries(members).map(([id, value]) => ({id, ...record(value)}))
      .sort((a, b) => slots.indexOf(String(record(a).slot)) - slots.indexOf(String(record(b).slot)))
      .map(member => {
        const m = record(member);
        const card = button(`${String(m.name)} · ${String(m.vitality)}/${String(m.maximumVitality)}`, () => command('select', {member: member.id}));
        card.setAttribute('aria-pressed', String(member.id === state.selectedMember));
        card.setAttribute('aria-label', `${String(m.name)}, ${String(m.slot)}`);
        if (member.id === state.selectedMember) card.style.borderColor = '#e4bd63';
        return card;
      }));
  };
  render(context.projection?.current() ?? null);
  const unsubscribe = context.projection?.subscribe(render) ?? (() => {});
  return { dispose() { unsubscribe(); panel.remove(); } };
}
