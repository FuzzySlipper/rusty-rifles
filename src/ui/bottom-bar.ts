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

function authoredPositions(state: Values): Array<{ id: string; name: string; rank: number; offsetForward: number; offsetLeft: number }> {
  return entries(state.positions)
    .map(([id, position]) => ({
      id,
      name: text(position.name, id),
      rank: number(position.rank),
      offsetForward: number(position.offsetForward),
      offsetLeft: number(position.offsetLeft),
    }))
    .sort((left, right) => left.rank - right.rank || left.id.localeCompare(right.id));
}

function facingVector(facing: string): { x: number; y: number } {
  switch (facing.toLowerCase()) {
    case 'east': return { x: 1, y: 0 };
    case 'south': return { x: 0, y: 1 };
    case 'west': return { x: -1, y: 0 };
    default: return { x: 0, y: -1 };
  }
}

// World-aligned compass placement: formation-local (forward, left) offsets are
// rotated by the party facing into world dx (east+)/dy (south+) deltas, then
// mapped onto the north-up board. Front of a north-facing party lands at the
// top; turning east swings it to the right rim.
function boardPoint(forward: number, left: number, facing: string): { x: number; y: number } {
  const front = facingVector(facing);
  const side = { x: front.y, y: -front.x };
  const dx = front.x * forward + side.x * left;
  const dy = front.y * forward + side.y * left;
  const scale = 85;
  const clamp = (value: number): number => Math.min(90, Math.max(10, value));
  return { x: clamp(50 + dx * scale), y: clamp(50 + dy * scale) };
}

const tokenPalette = ['#7fb3d5', '#8ca65c', '#e4bd63', '#c96a5a', '#9b7bd5', '#6fc2b4', '#d58cc0', '#a5c65c', '#6a9ae0', '#e08c5a'];

function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[words.length - 1][0]).toUpperCase();
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
  bar.style.cssText = 'box-sizing:border-box;position:fixed;left:0;right:0;bottom:0;z-index:1;display:grid;grid-template-columns:48px 200px minmax(0,1fr) minmax(360px,420px);gap:10px;align-items:stretch;padding:10px 14px;background:#141610f2;border-top:1px solid #74694e;color:#eee6d5;font:13px/1.35 system-ui;pointer-events:auto;max-height:min(300px,44vh)';

  const railSection = document.createElement('nav');
  railSection.setAttribute('aria-label', 'Quick actions');
  railSection.style.cssText = 'display:flex;flex-direction:column;gap:5px;align-items:stretch;justify-content:flex-start';
  const railButtons: HTMLButtonElement[] = [];
  const railAction = (label: string, glyph: string, title: string, action: string): void => {
    const shortcut = document.createElement('button');
    shortcut.type = 'button';
    shortcut.textContent = glyph;
    shortcut.title = title;
    shortcut.setAttribute('aria-label', title);
    shortcut.dataset.barAction = action;
    shortcut.style.cssText = 'height:36px;width:100%;padding:0;background:#33392f;color:#e4bd63;border:1px solid #574f3d;border-radius:3px;cursor:pointer;font:700 11px/1 system-ui;letter-spacing:0.04em';
    shortcut.addEventListener('click', () => command(action));
    railSection.append(shortcut);
    railButtons.push(shortcut);
  };
  // Intent shortcuts only: every action below already exists in C# and
  // reports its own feedback (including rejection reasons). The rail adds
  // no authority — it is the mockup's icon column over existing commands.
  railAction('pause', 'II', 'Pause or resume (P)', 'pause');
  railAction('attack', 'ATK', 'Attack selected enemy (Space)', 'attack');
  railAction('reload', 'RLD', 'Reload (T)', 'reload');
  railAction('rest', 'RST', 'Rest and recover', 'rest');
  railAction('save', 'SAV', 'Save expedition (K)', 'save');
  railAction('load', 'LOD', 'Load saved expedition (L)', 'load');

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
  (mapView as unknown as HTMLElement).style.cssText = 'display:block;width:100%;height:118px;background:#10120f;border:1px solid #574f3d;border-radius:3px';
  const vitalsLine = document.createElement('output');
  vitalsLine.dataset.barVitals = 'true';
  vitalsLine.textContent = 'Load — · —';
  vitalsLine.style.cssText = 'display:block;margin-top:4px;color:#c9c0ae;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis';
  mapSection.append(mapLocation, mapView as unknown as Node, vitalsLine);

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
  const formationTitle = document.createElement('div');
  formationTitle.textContent = 'Party formation';
  formationTitle.style.cssText = 'margin-bottom:2px;color:#e4bd63;font-size:11px;letter-spacing:0.12em;text-transform:uppercase;text-align:center';
  const formationFacing = document.createElement('output');
  formationFacing.dataset.barFacing = 'true';
  formationFacing.textContent = 'Party faces North';
  formationFacing.style.cssText = 'display:block;margin-bottom:4px;color:#c9c0ae;font-size:12px;text-align:center';
  const formationGrid = document.createElement('div');
  formationGrid.dataset.barFormation = 'true';
  formationGrid.tabIndex = -1;
  formationGrid.setAttribute('role', 'group');
  formationGrid.setAttribute('aria-label', 'Formation compass. Drag a member onto another ring to swap, onto a dashed gap to move.');
  formationGrid.style.cssText = 'position:relative;width:min(100%,188px);aspect-ratio:1/1;margin:0 auto;border-radius:50%;border:2px solid #6b5f45;background:radial-gradient(circle at 50% 42%,#1d201b 0%,#141610 62%,#0e100d 100%),repeating-linear-gradient(0deg,transparent 0 calc(25% - 1px),#ffffff10 calc(25% - 1px) 25%),repeating-linear-gradient(90deg,transparent 0 calc(25% - 1px),#ffffff10 calc(25% - 1px) 25%);box-shadow:inset 0 0 24px #000000aa,0 0 0 4px #141610,0 0 0 5px #2e2a22';
  const formationHint = document.createElement('p');
  formationHint.textContent = 'Drag onto a ring to swap, onto a gap to move — or click a dashed gap to move the selected member. Chevron shows facing.';
  formationHint.style.cssText = 'margin:5px 0 0;color:#c9c0ae;font-size:11px;text-align:center';
  const formationOverflow = document.createElement('div');
  formationOverflow.dataset.barFormationOverflow = 'true';
  formationOverflow.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;justify-content:center;margin-top:4px;min-height:0';
  formationSection.append(formationTitle, formationFacing, formationGrid, formationOverflow, formationHint);

  bar.append(railSection, mapSection, logSection, formationSection);
  root.append(bar);

  type Token = Readonly<{
    anchor: HTMLElement;
    select: HTMLButtonElement;
    marker: SVGElement;
    chevron: SVGElement;
    emblem: HTMLElement;
    name: HTMLElement;
    detail: HTMLElement;
    health: HTMLElement;
  }>;
  // HTML5 drag source: the dragged member id. dataTransfer carries it too,
  // but a local is robust when the drop lands in the same document.
  let formationDragId: string | null = null;
  const readFormationDrag = (event: DragEvent): string | null => {
    if (formationDragId) return formationDragId;
    try {
      const text = event.dataTransfer?.getData('text/member-id') ?? '';
      return text || null;
    } catch {
      return null;
    }
  };
  const createToken = (key: string): Token => {
    const anchor = document.createElement('div');
    anchor.dataset.barAnchor = key;
    anchor.style.cssText = 'position:absolute;transform:translate(-50%,-50%);display:flex;flex-direction:column;align-items:center;width:76px;z-index:2';
    const chevron = document.createElementNS(svgNamespace, 'svg');
    chevron.setAttribute('viewBox', '0 0 14 14');
    chevron.setAttribute('aria-hidden', 'true');
    (chevron as unknown as HTMLElement).style.cssText = 'height:13px;width:13px;visibility:hidden;margin-bottom:-2px';
    const marker = document.createElementNS(svgNamespace, 'path');
    marker.setAttribute('d', 'M 7 1 L 12.5 12 L 7 9.6 L 1.5 12 Z');
    marker.setAttribute('fill', '#c9b98a');
    marker.setAttribute('stroke', '#251914');
    marker.setAttribute('stroke-width', '1');
    chevron.append(marker);
    const select = document.createElement('button');
    select.type = 'button';
    select.dataset.barMember = key;
    select.disabled = true;
    select.style.cssText = 'height:40px;width:40px;border-radius:50%;border:2px solid #574f3d;background:radial-gradient(circle at 50% 35%,#3d4338,#22251f 75%);color:#f0e6d2;cursor:pointer;font:700 13px/1 system-ui;display:flex;align-items:center;justify-content:center;box-shadow:0 2px 6px #00000088';
    const emblem = document.createElement('span');
    emblem.setAttribute('aria-hidden', 'true');
    select.append(emblem);
    const name = document.createElement('strong');
    name.style.cssText = 'display:block;font-size:10px;max-width:74px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:1px;text-shadow:0 1px 2px #000';
    const detail = document.createElement('span');
    detail.style.cssText = 'display:block;font-size:10px;color:#c9c0ae;text-shadow:0 1px 2px #000';
    const track = document.createElement('span');
    track.style.cssText = 'display:block;height:4px;width:40px;border-radius:2px;background:#3a352a;margin-top:2px;overflow:hidden';
    const health = document.createElement('span');
    health.style.cssText = 'display:block;height:100%;width:0%;background:#8ca65c';
    track.append(health);
    anchor.append(chevron as unknown as Node, select, name, detail, track);
    formationGrid.append(anchor);
    select.addEventListener('click', () => {
      const memberId = select.dataset.memberId;
      const gapPosition = select.dataset.gapPosition;
      if (memberId) {
        command('select', { member: memberId });
        return;
      }
      // Click alternative to dragging: a dashed gap moves the selected
      // member there. Dragged swaps keep the legacy Swap button as backup.
      if (gapPosition && selectedMemberId) command('move', { member: selectedMemberId, position: gapPosition });
    });
    select.addEventListener('dragstart', event => {
      const memberId = select.dataset.memberId;
      if (!memberId) {
        event.preventDefault();
        return;
      }
      formationDragId = memberId;
      try {
        event.dataTransfer?.setData('text/member-id', memberId);
        if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
      } catch {
        // setData may throw in locked-down contexts; the local still works.
      }
    });
    select.addEventListener('dragend', () => {
      formationDragId = null;
    });
    anchor.addEventListener('dragover', event => {
      if (select.dataset.memberId || select.dataset.gapPosition) event.preventDefault();
    });
    anchor.addEventListener('drop', event => {
      event.preventDefault();
      event.stopPropagation();
      const dragged = readFormationDrag(event);
      if (!dragged) return;
      const targetMember = select.dataset.memberId;
      const gapPosition = select.dataset.gapPosition;
      if (targetMember) {
        if (targetMember !== dragged) command('formation', { member: dragged, otherMember: targetMember });
      } else if (gapPosition) {
        command('move', { member: dragged, position: gapPosition });
      }
      formationDragId = null;
    });
    return { anchor, select, marker, chevron, emblem, name, detail, health };
  };
  // Keyed by member id while occupied, by `empty:<slot>` for baseline gaps.
  // Tokens persist across renders so focus and drag state survive updates.
  const memberTokens = new Map<string, Token>();
  let selectedMemberId = '';
  const paintCompass = (): void => {
    if (formationGrid.querySelector('[data-compass]')) return;
    for (const [label, x, y] of [['N', 50, 1], ['E', 99, 50], ['S', 50, 99], ['W', 1, 50]] as Array<[string, number, number]>) {
      const mark = document.createElement('span');
      mark.dataset.compass = label;
      mark.textContent = label;
      mark.setAttribute('aria-hidden', 'true');
      mark.style.cssText = `position:absolute;left:${x}%;top:${y}%;transform:translate(-50%,-50%);color:#e4bd63;font-size:11px;font-weight:700;text-shadow:0 1px 2px #000;z-index:1`;
      formationGrid.append(mark);
    }
  };

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
    selectedMemberId = selectedMember;
    const partyFacing = text(state.facing, 'North');
    const facingLine = `Party faces ${partyFacing}`;
    if (formationFacing.textContent !== facingLine) formationFacing.textContent = facingLine;
    paintCompass();
    const signature = JSON.stringify([party, selectedMember, state.positions, partyFacing]);
    if (signature === formationSignature) return;
    formationSignature = signature;
    // Compass placement: every authored position renders at its rotated
    // (forward, left) offset on the north-up board, with a dashed gap where
    // unoccupied. Members on unauthored positions overflow into the strip
    // below the dial so a larger future party never breaks the layout.
    const claimed = new Set<string>();
    const placed: Array<{ key: string; id: string; member: Values; positionId: string; color: string } | { key: string; id: null; position: { id: string; name: string; offsetForward: number; offsetLeft: number } }> = [];
    const layout = authoredPositions(state);
    const colorFor = (index: number): string => tokenPalette[index % tokenPalette.length];
    for (const [index, position] of layout.entries()) {
      const found = entries(party).find(([id, member]) => !claimed.has(id) && text(member.position, '') === position.id);
      if (found) {
        claimed.add(found[0]);
        // Member keys live in a separate namespace from `empty:<id>` gap
        // keys so a hostile member id can never alias a gap token. See F7.
        placed.push({ key: `member:${found[0]}`, id: found[0], member: found[1], positionId: position.id, color: colorFor(index) });
      } else {
        placed.push({ key: `empty:${position.id}`, id: null, position });
      }
    }
    const overflow: Array<{ key: string; id: string; member: Values; color: string }> = [];
    for (const [id, member] of entries(party)) {
      if (!claimed.has(id)) {
        claimed.add(id);
        overflow.push({ key: `member:${id}`, id, member, color: colorFor(layout.length + overflow.length) });
      }
    }
    const hadFocus = formationSection.contains(document.activeElement);
    const liveKeys = new Set([...placed.map(item => item.key), ...overflow.map(item => item.key)]);
    for (const [key, token] of memberTokens) {
      if (!liveKeys.has(key)) {
        token.anchor.remove();
        memberTokens.delete(key);
      }
    }
    const paintOccupied = (
      item: { key: string; id: string; member: Values; color: string },
      point: { x: number; y: number } | null,
    ): void => {
      let token = memberTokens.get(item.key);
      if (!token) {
        token = createToken(item.key);
        memberTokens.set(item.key, token);
      }
      const member = item.member;
      const positionName = text(member.positionName, member.position);
      const facing = text(member.facing, partyFacing);
      const vitality = number(member.vitality);
      const maximum = Math.max(1, number(member.maximumVitality, 1));
      const fraction = Math.min(1, Math.max(0, vitality / maximum));
      const selected = item.id === selectedMember;
      const name = text(member.name, item.id);
      token.anchor.style.left = point ? `${point.x}%` : '';
      token.anchor.style.top = point ? `${point.y}%` : '';
      token.anchor.style.position = point ? 'absolute' : 'static';
      token.anchor.style.transform = point ? 'translate(-50%,-50%)' : 'none';
      token.anchor.style.width = point ? '76px' : 'auto';
      if (point) formationGrid.append(token.anchor);
      else formationOverflow.append(token.anchor);
      token.select.dataset.barMember = item.id;
      token.select.dataset.memberId = item.id;
      delete token.select.dataset.gapPosition;
      token.select.disabled = false;
      token.select.draggable = true;
      token.select.setAttribute('aria-pressed', String(selected));
      token.select.setAttribute('aria-label', `${name}, ${positionName}; facing ${facing}; vitality ${vitality} of ${maximum}. Drag onto another member to swap.`);
      token.select.title = `${positionName} · facing ${facing} · Power ${text(member.power, '0')} · Defense ${text(member.defense, '0')} · drag to swap`;
      token.select.style.borderColor = selected ? '#e4bd63' : item.color;
      token.select.style.boxShadow = selected ? `0 0 0 2px #e4bd63,0 2px 6px #00000088` : '0 2px 6px #00000088';
      token.emblem.textContent = initials(name);
      token.name.textContent = name;
      // Vitality only: the position name lives in title/aria-label, since
      // the 76px caption wraps a combined line and pushes the dial over.
      token.detail.textContent = `${vitality}/${maximum}`;
      token.marker.setAttribute('transform', `rotate(${facingRotation(facing)} 7 7)`);
      token.marker.setAttribute('fill', item.color);
      (token.chevron as unknown as HTMLElement).style.visibility = 'visible';
      token.health.style.width = `${Math.round(fraction * 100)}%`;
      token.health.style.background = fraction > 0.5 ? '#8ca65c' : fraction > 0.25 ? '#e4bd63' : '#b0523c';
    };
    for (const item of placed) {
      if (item.id === null) {
        let token = memberTokens.get(item.key);
        if (!token) {
          token = createToken(item.key);
          memberTokens.set(item.key, token);
        }
        const point = boardPoint(item.position.offsetForward, item.position.offsetLeft, partyFacing);
        // Re-append like occupied tokens so DOM/Tab/SR order tracks rank
        // order, not creation order. Re-appending a focused node preserves
        // focus; true removals were already swept, with post-loop fallback.
        formationGrid.append(token.anchor);
        token.anchor.style.left = `${point.x}%`;
        token.anchor.style.top = `${point.y}%`;
        token.anchor.style.position = 'absolute';
        token.anchor.style.transform = 'translate(-50%,-50%)';
        token.anchor.style.width = '76px';
        token.select.dataset.barMember = item.key;
        token.select.dataset.memberId = '';
        token.select.dataset.gapPosition = item.position.id;
        // Gaps stay enabled so they remain drop targets and the click
        // alternative (move selected member here) works by keyboard too.
        // aria-disabled announces the no-selection inert state to AT
        // without disabling (which would also kill drop targeting).
        token.select.disabled = false;
        token.select.setAttribute('aria-disabled', selectedMember ? 'false' : 'true');
        token.select.draggable = false;
        token.select.removeAttribute('aria-pressed');
        const selectedName = selectedMember ? text(record(party[selectedMember]).name, selectedMember) : '';
        const hint = selectedMember ? `Activate to move ${selectedName} here` : 'Empty position';
        token.select.setAttribute('aria-label', `${item.position.name}, empty. ${hint}. Or drop a member here to move them.`);
        token.select.title = `${item.position.name} · empty · drop to move here`;
        token.select.style.borderColor = '#574f3d';
        token.select.style.borderStyle = 'dashed';
        token.select.style.boxShadow = 'none';
        token.emblem.textContent = '·';
        // Clear the occupied-state announcements too, or a screen reader keeps
        // describing the departed member. See F2.
        token.name.textContent = item.position.name;
        token.detail.textContent = selectedMember ? `Move ${selectedName} ▸` : 'Empty';
        (token.chevron as unknown as HTMLElement).style.visibility = 'hidden';
        token.health.style.width = '0%';
        token.health.style.background = '#8ca65c';
        continue;
      }
      const authored = layout.find(position => position.id === item.positionId);
      const point = authored ? boardPoint(authored.offsetForward, authored.offsetLeft, partyFacing) : null;
      paintOccupied(item, point);
    }
    // Appending moves live nodes, so re-appending in order keeps the strip
    // sorted without detaching focus the way a full clear would.
    for (const item of overflow) paintOccupied(item, null);
    // Post-loop relocation: removing the focused token drops focus to the
    // body, so restore it once every token is final.
    if (hadFocus && !formationSection.contains(document.activeElement)) {
      const fallback = [...memberTokens.values()].map(candidate => candidate.select).find(button => !button.disabled);
      (fallback ?? formationGrid).focus();
    }
  };

  const renderVitals = (state: Values): void => {
    // Display only: party load summed over member-owned inventories plus
    // the expedition clock. No wealth line — the game has no currency yet,
    // and inventing a gold figure would be fiction, not projection.
    let mass = 0, capacity = 0;
    for (const [, owner] of entries(record(state.inventory).owners)) {
      if (text(owner.kind, '') !== 'member') continue;
      mass += number(owner.mass);
      capacity += number(owner.maxMass);
    }
    const total = Math.max(0, Math.floor(number(state.seconds)));
    const clock = `${Math.floor(total / 3600)}:${String(Math.floor(total / 60) % 60).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
    const line = `Load ${mass}/${capacity} · ${clock}`;
    if (vitalsLine.textContent !== line) vitalsLine.textContent = line;
    const pauseButton = railButtons[0];
    const pausedLabel = number(state.paused) === 1 ? 'GO' : 'II';
    if (pauseButton && pauseButton.textContent !== pausedLabel) pauseButton.textContent = pausedLabel;
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
    renderVitals(state);
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
