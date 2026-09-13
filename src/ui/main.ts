type Envelope = Readonly<{ value: unknown }>;
type UiContext = Readonly<{
  projection?: { current(): Envelope | null; subscribe(render: (value: Envelope | null) => void): () => void };
}>;

function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

/** DOM observer of the Engine projection; all game state stays in C#. */
export function mountProductUi(root: Element, context: UiContext): Readonly<{ dispose(): void }> {
  const panel = document.createElement('aside');
  panel.setAttribute('aria-label', 'Expedition');
  panel.style.cssText = 'position:relative;margin:12px;padding:12px 16px;color:#eee6d5;background:#171914e8;border:1px solid #74694e;border-radius:5px;font:14px/1.5 system-ui;max-width:460px;pointer-events:none';
  const title = document.createElement('strong');
  title.textContent = 'Rusty Rifles';
  const help = document.createElement('p');
  help.textContent = 'W/S step · A/D sidestep · Q/E turn. Find the green exit tile.';
  const status = document.createElement('output');
  const roster = document.createElement('div');
  roster.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-top:10px';
  panel.append(title, help, status, roster);
  root.append(panel);
  let previousRoster = '';
  const render = (envelope: Envelope | null): void => {
    if (!envelope) { status.textContent = 'Preparing the expedition…'; return; }
    const state = record(envelope.value);
    status.textContent = `${String(state.status)} · ${String(state.facing)} · (${String(state.x)}, ${String(state.y)}) · ${Math.floor(Number(state.seconds))}s · Seed ${String(state.seed)}`;
    const members = record(state.party);
    const signature = JSON.stringify(members);
    if (signature === previousRoster) return;
    previousRoster = signature;
    const slots = ['FrontLeft', 'FrontRight', 'RearLeft', 'RearRight'];
    const orderedMembers = Object.values(members).map(record)
      .sort((a, b) => slots.indexOf(String(a.slot)) - slots.indexOf(String(b.slot)));
    roster.replaceChildren(...orderedMembers.map(member => {
      const card = document.createElement('div');
      card.style.cssText = 'border:1px solid #555344;padding:6px 8px';
      card.textContent = `${String(member.name)} · ${String(member.vitality)}/${String(member.maximumVitality)}`;
      card.setAttribute('aria-label', `${String(member.name)}, ${String(member.slot)}, vitality ${String(member.vitality)} of ${String(member.maximumVitality)}`);
      return card;
    }));
  };
  render(context.projection?.current() ?? null);
  const unsubscribe = context.projection?.subscribe(render) ?? (() => {});
  return { dispose() { unsubscribe(); panel.remove(); } };
}
