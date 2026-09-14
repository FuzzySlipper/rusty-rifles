type Values = Record<string, unknown>;

const svgNamespace = 'http://www.w3.org/2000/svg';
const gameplayKeys = new Set(['Space', 'KeyT', 'KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'KeyF', 'KeyR', 'KeyP', 'KeyK', 'KeyL']);
const formationSlots = ['FrontLeft', 'FrontRight', 'RearLeft', 'RearRight'] as const;
type FormationSlot = (typeof formationSlots)[number];
const maxFeedbackLines = 60;
const maxLogRenderChars = 4000;

function record(value: unknown): Values {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Values : {};
}

function entries(value: unknown): Array<[string, Values]> {
  return Object.entries(record(value)).map(([key, entry]) => [key, record(entry)]);
}

function text(value: unknown, fallback = ''): string {
  return value === null || value === undefined || value === '' ? fallback : String(value);
}

function number(value: unknown, fallback = 0): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function slotLabel(slot: string): string {
  switch (slot) {
    case 'FrontLeft': return 'Front left';
    case 'FrontRight': return 'Front right';
    case 'RearLeft': return 'Rear left';
    case 'RearRight': return 'Rear right';
    default: return slot;
  }
}

/**
 * First game-UI pass: a fixed bottom bar with a current-floor minimap, a
 * combined event log, and a 2x2 formation display. Display plus member
 * selection only; all rules and inventory authority stay in C#.
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
  bar.style.cssText = 'box-sizing:border-box;position:fixed;left:0;right:0;bottom:0;z-index:1;display:grid;grid-template-columns:210px minmax(0,1fr) 250px;gap:10px;align-items:stretch;padding:10px 14px;background:#141610f2;border-top:1px solid #74694e;color:#eee6d5;font:13px/1.35 system-ui;pointer-events:auto;max-height:min(240px,36vh)';

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
  const formationGrid = document.createElement('div');
  formationGrid.dataset.barFormation = 'true';
  formationGrid.tabIndex = -1;
  formationGrid.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:5px';
  const formationHint = document.createElement('p');
  formationHint.textContent = 'Click a member to select them.';
  formationHint.style.cssText = 'margin:5px 0 0;color:#c9c0ae;font-size:11px';
  formationSection.append(formationGrid, formationHint);

  bar.append(mapSection, logSection, formationSection);
  root.append(bar);

  const memberButtons = new Map<FormationSlot, { select: HTMLButtonElement; health: HTMLElement }>();
  for (const slot of formationSlots) {
    const select = document.createElement('button');
    select.type = 'button';
    select.dataset.barMember = slot;
    select.disabled = true;
    select.setAttribute('aria-label', `${slotLabel(slot)}, empty`);
    select.title = `${slotLabel(slot)} · empty`;
    select.style.cssText = 'background:#33392f;color:#eee6d5;border:1px solid #574f3d;border-radius:3px;padding:5px 6px;cursor:pointer;font:inherit;text-align:left;min-height:64px';
    const name = document.createElement('strong');
    name.textContent = `${slotLabel(slot)} · empty`;
    name.style.cssText = 'display:block;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis';
    const detail = document.createElement('span');
    detail.textContent = 'No member here';
    detail.style.cssText = 'display:block;font-size:11px;color:#c9c0ae';
    const track = document.createElement('span');
    track.style.cssText = 'display:block;height:6px;border-radius:3px;background:#3a352a;margin-top:4px;overflow:hidden';
    const health = document.createElement('span');
    health.style.cssText = 'display:block;height:100%;width:0%;background:#8ca65c';
    track.append(health);
    select.append(name, detail, track);
    select.addEventListener('click', () => {
      const memberId = select.dataset.memberId;
      if (memberId) command('select', { member: memberId });
    });
    formationGrid.append(select);
    memberButtons.set(slot, { select, health });
  }

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
    const rotation = { north: 0, east: 90, south: 180, west: 270 }[pose.facing.toLowerCase()] ?? 0;
    svgAttributes(arrow, { d: 'M 0.5 0.08 L 0.84 0.82 L 0.5 0.65 L 0.16 0.82 Z', fill: '#f5eee1', stroke: '#251914', 'stroke-width': 0.08, transform: `rotate(${rotation} ${pose.x + 0.5} ${pose.y + 0.5}) translate(${pose.x} ${pose.y})` });
    const partyLabel = document.createElementNS(svgNamespace, 'title');
    partyLabel.textContent = `Party facing ${pose.facing}`;
    arrow.append(partyLabel);
    mapView.append(arrow);
  };

  const renderFormation = (state: Values): void => {
    const party = record(state.party);
    const selectedMember = text(state.selectedMember, '');
    const signature = JSON.stringify([party, selectedMember]);
    if (signature === formationSignature) return;
    formationSignature = signature;
    const bySlot = new Map<string, { id: string; member: Values }>();
    // Overflow contract (see F3): the bar renders exactly the four baseline
    // positions. First member wins a contested slot; members on unknown slots
    // stay selectable through the legacy roster until a later pass designs a
    // larger formation display. C# currently always emits the four baseline
    // slots, so this only guards future definitions, never today's party.
    for (const [id, member] of entries(party)) {
      const key = text(member.slot, '');
      if (!bySlot.has(key)) bySlot.set(key, { id, member });
    }
    for (const slot of formationSlots) {
      const row = memberButtons.get(slot);
      if (!row) continue;
      const found = bySlot.get(slot);
      const name = row.select.querySelector('strong');
      const detail = row.select.querySelector('span');
      if (!found) {
        // Search BEFORE disabling would return this very button, so exclude
        // it: it is about to become unfocusable. See re-review of F6.
        if (document.activeElement === row.select) {
          const fallback = formationSlots.map(candidate => memberButtons.get(candidate)?.select).find(button => button && button !== row.select && !button.disabled);
          (fallback ?? formationGrid).focus();
        }
        row.select.dataset.memberId = '';
        row.select.disabled = true;
        row.select.style.borderColor = '#574f3d';
        row.select.title = `${slotLabel(slot)} · empty`;
        // Clear the occupied-state announcements too, or a screen reader keeps
        // describing the departed member. See F2.
        row.select.removeAttribute('aria-pressed');
        row.select.setAttribute('aria-label', `${slotLabel(slot)}, empty`);
        if (name) name.textContent = `${slotLabel(slot)} · empty`;
        if (detail) detail.textContent = 'No member here';
        row.health.style.width = '0%';
        row.health.style.background = '#8ca65c';
        continue;
      }
      row.select.disabled = false;
      row.select.dataset.memberId = found.id;
      const vitality = number(found.member.vitality);
      const maximum = Math.max(1, number(found.member.maximumVitality, 1));
      const fraction = Math.min(1, Math.max(0, vitality / maximum));
      row.select.setAttribute('aria-pressed', String(found.id === selectedMember));
      row.select.setAttribute('aria-label', `${text(found.member.name, found.id)}, ${slotLabel(slot)}; vitality ${vitality} of ${maximum}`);
      row.select.title = `${slotLabel(slot)} · Power ${text(found.member.power, '0')} · Defense ${text(found.member.defense, '0')}`;
      row.select.style.borderColor = found.id === selectedMember ? '#e4bd63' : '#827556';
      if (name) name.textContent = text(found.member.name, found.id);
      if (detail) detail.textContent = `${slotLabel(slot)} · ${vitality}/${maximum}`;
      row.health.style.width = `${Math.round(fraction * 100)}%`;
      row.health.style.background = fraction > 0.5 ? '#8ca65c' : fraction > 0.25 ? '#e4bd63' : '#b0523c';
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
