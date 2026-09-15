type Values = Record<string, unknown>;

const svgNamespace = 'http://www.w3.org/2000/svg';
const gameplayKeys = new Set(['Space', 'KeyT', 'KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'KeyF', 'KeyR', 'KeyP', 'KeyK', 'KeyL']);
const maxFeedbackLines = 60;
const maxLogRenderChars = 4000;

function record(value: unknown): Values {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Values : {};
}

function entries(value: unknown): Array<[string, Values]> {
  return Object.entries(record(value)).map(([key, entry]) => [key, record(entry)]);
}

function text(value: unknown, fallback: unknown = ''): string {
  const pick = value === null || value === undefined || value === '' ? fallback : value;
  return pick === null || pick === undefined ? '' : String(pick);
}

function number(value: unknown, fallback = 0): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function authoredPositions(state: Values): Array<{ id: string; name: string; rank: number }> {
  return entries(state.positions)
    .map(([id, position]) => ({ id, name: text(position.name, id), rank: number(position.rank) }))
    .sort((left, right) => left.rank - right.rank || left.id.localeCompare(right.id));
}

function facingRotation(facing: string): number {
  return { north: 0, east: 90, south: 180, west: 270 }[facing.toLowerCase()] ?? 0;
}

/**
 * Game-UI bottom bar: current-floor minimap, combined event log, and a
 * formation display with per-member facing. Display plus member selection
 * only; all rules and inventory authority stay in C#.
 */
export function mountBottomBar(root: Element, command: (action: string, fields?: Record<string, unknown>) => void): Readonly<{
  update(raw: unknown): void;
  dispose(): void;
  element: HTMLElement;
}> {
  const bar = document.createElement('footer');
  bar.setAttribute('aria-label', 'Party status bar');
  bar.dataset.rustyUiInteractive = 'true';
  bar.dataset.partyBar = 'true';
  bar.style.cssText = 'box-sizing:border-box;position:fixed;left:0;right:0;bottom:0;z-index:1;display:grid;grid-template-columns:200px minmax(0,1fr) minmax(320px,400px);gap:10px;align-items:stretch;padding:10px 14px;background:#141610f2;border-top:1px solid #74694e;color:#eee6d5;font:13px/1.35 system-ui;pointer-events:auto;max-height:min(240px,36vh)';

  const mapSection = document.createElement('section');
  mapSection.setAttribute('aria-label', 'Minimap');
  const mapLocation = document.createElement('output');
  mapLocation.dataset.barLocation = 'true';
  mapLocation.textContent = 'No map yet';
  mapLocation.style.cssText = 'display:block;margin-bottom:4px;color:#c9c0ae;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis';
  const mapView = document.createElementNS(svgNamespace, 'svg');
  mapView.setAttribute('role', 'img');
  mapView.setAttribute('aria-label', 'Discovered floor map');
  (mapView as unknown as HTMLElement).dataset.barMap = 'true';
  (mapView as unknown as HTMLElement).style.cssText = 'display:block;width:100%;height:140px;background:#10120f;border:1px solid #574f3d;border-radius:3px';
  mapSection.append(mapLocation, mapView as unknown as Node);

  const logSection = document.createElement('section');
  logSection.setAttribute('aria-label', 'Event log');
  const logStatus = document.createElement('output');
  logStatus.dataset.barStatus = 'true';
  logStatus.textContent = 'Preparing…';
  logStatus.style.cssText = 'display:block;margin-bottom:4px;color:#ead27e;white-space:nowrap;overflow:hidden;text-overflow:ellipsis';
  const logView = document.createElement('output');
  logView.dataset.barLog = 'true';
  logView.setAttribute('role', 'log');
  logView.textContent = 'No events yet.';
  logView.style.cssText = 'display:block;height:140px;overflow:auto;white-space:pre-line;color:#e7dcc4;background:#10120f99;border:1px solid #574f3d;border-radius:3px;padding:6px 8px';
  logSection.append(logStatus, logView);

  const formationSection = document.createElement('section');
  formationSection.setAttribute('aria-label', 'Formation');
  const formationFacing = document.createElement('output');
  formationFacing.dataset.barFacing = 'true';
  formationFacing.textContent = 'Party faces North';
  formationFacing.style.cssText = 'display:block;margin-bottom:4px;color:#c9c0ae;font-size:12px';
  const formationGrid = document.createElement('div');
  formationGrid.dataset.barFormation = 'true';
  formationGrid.tabIndex = -1;
  formationGrid.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:5px;max-height:158px;overflow:auto';
  const formationHint = document.createElement('p');
  formationHint.textContent = 'Click a member to select them. Chevron shows facing.';
  formationHint.style.cssText = 'margin:5px 0 0;color:#c9c0ae;font-size:11px';
  formationSection.append(formationFacing, formationGrid, formationHint);

  bar.append(mapSection, logSection, formationSection);
  root.append(bar);

  type Token = Readonly<{
    select: HTMLButtonElement;
    marker: SVGElement;
    chevron: SVGElement;
    name: HTMLElement;
    detail: HTMLElement;
    health: HTMLElement;
  }>;
  const createToken = (key: string): Token => {
    const select = document.createElement('button');
    select.type = 'button';
    select.dataset.barMember = key;
    select.disabled = true;
    select.style.cssText = 'background:#33392f;color:#eee6d5;border:1px solid #574f3d;border-radius:3px;padding:5px 6px;cursor:pointer;font:inherit;text-align:left;min-height:64px';
    const head = document.createElement('span');
    head.style.cssText = 'display:flex;gap:5px;align-items:center';
    const chevron = document.createElementNS(svgNamespace, 'svg');
    chevron.setAttribute('viewBox', '0 0 14 14');
    chevron.setAttribute('aria-hidden', 'true');
    (chevron as unknown as HTMLElement).style.cssText = 'flex:0 0 14px;height:14px;width:14px;visibility:hidden';
    const marker = document.createElementNS(svgNamespace, 'path');
    marker.setAttribute('d', 'M 7 1.5 L 12 12 L 7 9.8 L 2 12 Z');
    marker.setAttribute('fill', '#c9b98a');
    chevron.append(marker);
    const name = document.createElement('strong');
    name.style.cssText = 'display:block;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis';
    head.append(chevron as unknown as Node, name);
    const detail = document.createElement('span');
    detail.style.cssText = 'display:block;font-size:11px;color:#c9c0ae';
    const track = document.createElement('span');
    track.style.cssText = 'display:block;height:6px;border-radius:3px;background:#3a352a;margin-top:4px;overflow:hidden';
    const health = document.createElement('span');
    health.style.cssText = 'display:block;height:100%;width:0%;background:#8ca65c';
    track.append(health);
    select.append(head, detail, track);
    select.addEventListener('click', () => {
      const memberId = select.dataset.memberId;
      if (memberId) command('select', { member: memberId });
    });
    return { select, marker, chevron, name, detail, health };
  };
  // Keyed by member id while occupied, by `empty:<slot>` for baseline gaps.
  // Tokens persist across renders so focus and scroll survive updates.
  const memberTokens = new Map<string, Token>();

  let mapSignature = '';
  let formationSignature = '';
  let logSignature = '';
  // True before the first paint and after a run change, so the log DOM is
  // repainted (back to the placeholder when empty) even if the new render
  // equals the old signature. See F1.
  let logNeedsPaint = true;
  let seenRun = '';
  let lastFeedback = '';
  const feedbackHistory: string[] = [];

  const svgAttributes = (element: SVGElement, values: Record<string, string | number>): void => {
    for (const [name, value] of Object.entries(values)) element.setAttribute(name, String(value));
  };

  const drawMap = (run: Values): void => {
    const maps = record(run.maps);
    const floorKey = text(run.floorKey, '');
    const floor = record(maps[floorKey]);
    const cells = entries(floor.cells).map(([, cell]) => ({
      x: number(cell.x), y: number(cell.y), level: number(cell.level),
    }));
    const markers = entries(floor.markers).map(([id, marker]) => ({ id, x: number(marker.x), y: number(marker.y), label: text(marker.label, id) }));
    const pose = { x: number(run.x), y: number(run.y), facing: text(run.facing, 'North') };
    const signature = JSON.stringify([floorKey, cells, markers, pose]);
    if (signature === mapSignature) return;
    mapSignature = signature;
    while (mapView.firstChild) mapView.firstChild.remove();
    mapLocation.textContent = floorKey
      ? `${text(run.floor, 'Unknown floor')} · facing ${pose.facing} · (${pose.x}, ${pose.y})`
      : 'No map yet';
    if (cells.length === 0) {
      mapView.setAttribute('viewBox', '0 0 10 10');
      const empty = document.createElementNS(svgNamespace, 'text');
      svgAttributes(empty, { x: 5, y: 5, 'text-anchor': 'middle', fill: '#c9c0ae', 'font-size': 1.1 });
      empty.textContent = 'Undiscovered';
      mapView.append(empty);
      return;
    }
    const minX = Math.min(...cells.map(cell => cell.x));
    const maxX = Math.max(...cells.map(cell => cell.x));
    const minY = Math.min(...cells.map(cell => cell.y));
    const maxY = Math.max(...cells.map(cell => cell.y));
    mapView.setAttribute('viewBox', `${minX - 0.4} ${minY - 0.4} ${maxX - minX + 1.8} ${maxY - minY + 1.8}`);
    mapView.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    for (const cell of cells) {
      const tile = document.createElementNS(svgNamespace, 'rect');
      const shade = cell.level > 0 ? '#8ca65c' : cell.level < 0 ? '#5e86a5' : '#d1bc78';
      svgAttributes(tile, { x: cell.x + 0.06, y: cell.y + 0.06, width: 0.88, height: 0.88, rx: 0.08, fill: shade, stroke: '#10120f', 'stroke-width': 0.06 });
      mapView.append(tile);
    }
    for (const marker of markers) {
      const mark = document.createElementNS(svgNamespace, 'path');
      svgAttributes(mark, { d: `M ${marker.x + 0.5} ${marker.y + 0.18} L ${marker.x + 0.82} ${marker.y + 0.5} L ${marker.x + 0.5} ${marker.y + 0.82} L ${marker.x + 0.18} ${marker.y + 0.5} Z`, fill: '#f0cf6a', stroke: '#281d0f', 'stroke-width': 0.06 });
      const label = document.createElementNS(svgNamespace, 'title');
      label.textContent = marker.label;
      mark.append(label);
      mapView.append(mark);
    }
    const arrow = document.createElementNS(svgNamespace, 'path');
    const rotation = facingRotation(pose.facing);
    svgAttributes(arrow, { d: 'M 0.5 0.08 L 0.84 0.82 L 0.5 0.65 L 0.16 0.82 Z', fill: '#f5eee1', stroke: '#251914', 'stroke-width': 0.08, transform: `rotate(${rotation} ${pose.x + 0.5} ${pose.y + 0.5}) translate(${pose.x} ${pose.y})` });
    const partyLabel = document.createElementNS(svgNamespace, 'title');
    partyLabel.textContent = `Party facing ${pose.facing}`;
    arrow.append(partyLabel);
    mapView.append(arrow);
  };

  const renderFormation = (state: Values): void => {
    const party = record(state.party);
    const selectedMember = text(state.selectedMember, '');
    const partyFacing = text(state.facing, 'North');
    const facingLine = `Party faces ${partyFacing}`;
    if (formationFacing.textContent !== facingLine) formationFacing.textContent = facingLine;
    const signature = JSON.stringify([party, selectedMember]);
    if (signature === formationSignature) return;
    formationSignature = signature;
    // Data-driven placement: every authored position renders in rank order
    // (with an empty gap token where unoccupied), then any member on an
    // unauthored position follows in projection order. A larger future party
    // renders every member with no layout change.
    const claimed = new Set<string>();
    const placed: Array<{ key: string; id: string; member: Values } | { key: string; id: null; position: { id: string; name: string } }> = [];
    const layout = authoredPositions(state);
    for (const position of layout) {
      const found = entries(party).find(([id, member]) => !claimed.has(id) && text(member.position, '') === position.id);
      if (found) {
        claimed.add(found[0]);
        // Member keys live in a separate namespace from `empty:<id>` gap
        // keys so a hostile member id can never alias a gap token. See F7.
        placed.push({ key: `member:${found[0]}`, id: found[0], member: found[1] });
      } else {
        placed.push({ key: `empty:${position.id}`, id: null, position });
      }
    }
    for (const [id, member] of entries(party)) {
      if (!claimed.has(id)) {
        claimed.add(id);
        placed.push({ key: `member:${id}`, id, member });
      }
    }
    const hadFocus = formationGrid.contains(document.activeElement);
    for (const [key, token] of memberTokens) {
      if (!placed.some(item => item.key === key)) {
        token.select.remove();
        memberTokens.delete(key);
      }
    }
    for (const item of placed) {
      let token = memberTokens.get(item.key);
      if (!token) {
        token = createToken(item.key);
        memberTokens.set(item.key, token);
      }
      if (item.id === null) {
        if (document.activeElement === token.select) {
          const fallback = [...memberTokens.values()].map(candidate => candidate.select).find(button => button !== token.select && !button.disabled);
          (fallback ?? formationGrid).focus();
        }
        token.select.dataset.barMember = item.key;
        token.select.dataset.memberId = '';
        token.select.disabled = true;
        token.select.style.borderColor = '#574f3d';
        token.select.title = `${item.position.name} · empty`;
        // Clear the occupied-state announcements too, or a screen reader keeps
        // describing the departed member. See F2.
        token.select.removeAttribute('aria-pressed');
        token.select.setAttribute('aria-label', `${item.position.name}, empty`);
        token.name.textContent = `${item.position.name} · empty`;
        token.detail.textContent = 'No member here';
        (token.chevron as unknown as HTMLElement).style.visibility = 'hidden';
        token.health.style.width = '0%';
        token.health.style.background = '#8ca65c';
        formationGrid.append(token.select);
        continue;
      }
      const member = item.member;
      const positionName = text(member.positionName, member.position);
      const facing = text(member.facing, partyFacing);
      const vitality = number(member.vitality);
      const maximum = Math.max(1, number(member.maximumVitality, 1));
      const fraction = Math.min(1, Math.max(0, vitality / maximum));
      const selected = item.id === selectedMember;
      token.select.dataset.barMember = item.id;
      token.select.dataset.memberId = item.id;
      token.select.disabled = false;
      token.select.setAttribute('aria-pressed', String(selected));
      token.select.setAttribute('aria-label', `${text(member.name, item.id)}, ${positionName}; facing ${facing}; vitality ${vitality} of ${maximum}`);
      token.select.title = `${positionName} · facing ${facing} · Power ${text(member.power, '0')} · Defense ${text(member.defense, '0')}`;
      token.select.style.borderColor = selected ? '#e4bd63' : '#827556';
      token.name.textContent = text(member.name, item.id);
      token.detail.textContent = `${positionName} · ${vitality}/${maximum}`;
      token.marker.setAttribute('transform', `rotate(${facingRotation(facing)} 7 7)`);
      token.marker.setAttribute('fill', selected ? '#e4bd63' : '#c9b98a');
      (token.chevron as unknown as HTMLElement).style.visibility = 'visible';
      token.health.style.width = `${Math.round(fraction * 100)}%`;
      token.health.style.background = fraction > 0.5 ? '#8ca65c' : fraction > 0.25 ? '#e4bd63' : '#b0523c';
      formationGrid.append(token.select);
    }
    // Post-loop relocation: removing or disabling the focused token drops
    // focus to the body, so restore it once every token is final.
    if (hadFocus && !formationGrid.contains(document.activeElement)) {
      const fallback = [...memberTokens.values()].map(candidate => candidate.select).find(button => !button.disabled);
      (fallback ?? formationGrid).focus();
    }
  };

  const renderLog = (state: Values): void => {
    const combat = record(state.combat);
    const feedback = text(state.feedback, '');
    // Contract (see F5): feedback is a snapshot level, not an event stream —
    // the projection only carries the latest string, so a repeat is
    // indistinguishable from steady state and intentionally stored once.
    // Genuine repeats of distinct events live in combat.log, kept verbatim.
    if (feedback && feedback !== lastFeedback) {
      lastFeedback = feedback;
      if (feedbackHistory[feedbackHistory.length - 1] !== feedback) {
        feedbackHistory.push(feedback);
        while (feedbackHistory.length > maxFeedbackLines) feedbackHistory.shift();
      }
    }
    const combatLog = text(combat.log, '');
    const statusLine = `${text(state.status, 'Exploring')} · ${text(state.room, 'Passage')}`;
    if (logStatus.textContent !== statusLine) logStatus.textContent = statusLine;
    const combined = [...feedbackHistory, combatLog].filter(line => line.length > 0).join('\n').slice(-maxLogRenderChars);
    if (logNeedsPaint || combined !== logSignature) {
      logSignature = combined;
      logNeedsPaint = false;
      logView.textContent = combined || 'No events yet.';
      logView.scrollTop = logView.scrollHeight;
    }
  };

  const update = (raw: unknown): void => {
    const state = record(raw);
    const run = record(state.run);
    const runId = text(run.id, '');
    if (seenRun !== runId) {
      seenRun = runId;
      lastFeedback = '';
      feedbackHistory.length = 0;
      mapSignature = '';
      formationSignature = '';
      // Forces the log DOM back to the placeholder when the new run starts
      // with no events. See F1.
      logNeedsPaint = true;
    }
    drawMap(run);
    renderFormation(state);
    renderLog(state);
  };

  const stopGameplayKeys = (event: KeyboardEvent): void => {
    if (!gameplayKeys.has(event.code)) return;
    event.preventDefault();
    event.stopPropagation();
  };
  bar.addEventListener('keydown', stopGameplayKeys, true);
  bar.addEventListener('keyup', stopGameplayKeys, true);

  return {
    update,
    element: bar,
    dispose() {
      bar.removeEventListener('keydown', stopGameplayKeys, true);
      bar.removeEventListener('keyup', stopGameplayKeys, true);
      bar.remove();
    },
  };
}
