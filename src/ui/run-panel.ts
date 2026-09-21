type Values = Record<string, unknown>;

const gameplayKeys = new Set(['Space', 'KeyV', 'KeyB', 'KeyN', 'KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'KeyF', 'KeyR', 'KeyP', 'KeyK', 'KeyL']);
const svgNamespace = 'http://www.w3.org/2000/svg';

function record(value: unknown): Values {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Values : {};
}

function entries(value: unknown): Array<[string, Values]> {
  return Object.entries(record(value)).map(([key, entry]) => [key, record(entry)]);
}

function text(value: unknown, fallback = '—'): string {
  return value === null || value === undefined || value === '' ? fallback : String(value);
}

function number(value: unknown, fallback = 0): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function element<K extends keyof HTMLElementTagNameMap>(tag: K, label?: string): HTMLElementTagNameMap[K] {
  const created = document.createElement(tag);
  if (label !== undefined) created.textContent = label;
  return created;
}

function button(label: string, click: () => void): HTMLButtonElement {
  const control = element('button', label);
  control.type = 'button';
  control.addEventListener('click', click);
  return control;
}

function svgElement(tag: string): SVGElement {
  return document.createElementNS(svgNamespace, tag);
}

function svgAttributes(element: SVGElement, values: Record<string, string | number>): void {
  for (const [name, value] of Object.entries(values)) element.setAttribute(name, String(value));
}

/**
 * Presents one expedition projection. Commands are intentionally passed through
 * the caller's product command bridge; this companion never owns map or run state.
 */
export function mountRunPanel(root: Element, command: (action: string, fields?: Record<string, unknown>) => void): Readonly<{
  update(raw: unknown): void;
  dispose(): void;
  element: HTMLElement;
}> {
  const panel = element('aside');
  panel.setAttribute('aria-label', 'Expedition run');
  panel.dataset.rustyUiInteractive = 'true';
  panel.style.cssText = 'box-sizing:border-box;position:fixed;right:12px;top:138px;z-index:1;width:min(330px,calc(100vw - 24px));max-height:calc(100vh - 150px);overflow:auto;padding:9px 10px;background:#171914e8;color:#eee6d5;border:1px solid #74694e;border-radius:5px;font:13px/1.35 system-ui;pointer-events:auto';

  const title = element('strong', 'Expedition');
  const location = element('output');
  location.dataset.runLocation = 'true';
  location.style.cssText = 'display:block;margin:3px 0;color:#c9c0ae';
  const status = element('output');
  status.dataset.runStatus = 'true';
  status.style.cssText = 'display:block;margin:4px 0';
  const objective = element('p');
  objective.dataset.runObjective = 'true';
  objective.style.cssText = 'margin:4px 0;color:#ead27e';
  const result = element('p');
  result.dataset.runResult = 'true';
  result.setAttribute('role', 'status');
  result.style.cssText = 'margin:4px 0;color:#f0cfad';

  const travel = element('section');
  travel.dataset.runTravel = 'true';
  travel.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin:7px 0';
  const travelMessage = element('output');
  travelMessage.dataset.runTravelProblem = 'true';
  travelMessage.style.cssText = 'display:block;min-height:1.35em;margin:3px 0;color:#c9c0ae';

  const complete = button('Complete expedition', () => command('complete'));
  complete.dataset.runComplete = 'true';
  const load = button('Load saved run', () => command('load'));
  load.dataset.runLoad = 'true';
  const restart = button('Restart run', () => command('restart'));
  restart.dataset.runRestart = 'true';
  const runActions = element('div');
  runActions.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin:6px 0';
  runActions.append(complete, load, restart);
  const completionMessage = element('output');
  completionMessage.dataset.runCompletionProblem = 'true';
  completionMessage.style.cssText = 'display:block;min-height:1.35em;margin:3px 0;color:#c9c0ae';

  const map = element('details');
  map.dataset.runMap = 'true';
  map.open = false;
  map.style.cssText = 'border-top:1px solid #574f3d;margin-top:8px;padding-top:6px';
  const mapSummary = element('summary', 'Discovered map');
  const floorLabel = element('label', 'Floor ');
  const floorSelect = element('select');
  floorSelect.dataset.runMapFloor = 'true';
  floorSelect.setAttribute('aria-label', 'Known map floor');
  floorLabel.append(floorSelect);
  floorLabel.style.cssText = 'display:block;margin:6px 0';
  const mapView = svgElement('svg');
  mapView.dataset.runMapSvg = 'true';
  mapView.setAttribute('role', 'img');
  mapView.setAttribute('aria-label', 'Discovered floor map');
  mapView.style.cssText = 'display:block;width:100%;max-height:250px;background:#10120f;border:1px solid #574f3d;border-radius:3px';
  const mapLegend = element('p', 'Each square is one discovered cell · ◆ landmark · ▲ party');
  mapLegend.dataset.runMapLegend = 'true';
  mapLegend.style.cssText = 'font-size:11px;margin:4px 0;color:#c9c0ae';
  map.append(mapSummary, floorLabel, mapView, mapLegend);

  const journal = element('details');
  journal.dataset.runJournal = 'true';
  journal.open = false;
  journal.style.cssText = 'border-top:1px solid #574f3d;margin-top:8px;padding-top:6px';
  const journalSummary = element('summary', 'Floor journal');
  const notes = element('p');
  notes.dataset.runNotes = 'true';
  notes.style.cssText = 'margin:5px 0;white-space:pre-wrap;color:#c9c0ae';
  journal.append(journalSummary, notes);

  const newRun = element('details');
  newRun.dataset.runNew = 'true';
  newRun.open = false;
  newRun.style.cssText = 'border-top:1px solid #574f3d;margin-top:8px;padding-top:6px';
  const newSummary = element('summary', 'New expedition');
  const seedLabel = element('label', 'Seed ');
  const seedInput = element('input');
  seedInput.dataset.runSeed = 'true';
  seedInput.type = 'text';
  seedInput.inputMode = 'numeric';
  seedInput.autocomplete = 'off';
  seedInput.setAttribute('aria-label', 'New expedition seed');
  seedLabel.append(seedInput);
  const difficultyLabel = element('label', ' Profile ');
  const difficulty = element('select');
  difficulty.dataset.runDifficulty = 'true';
  difficulty.setAttribute('aria-label', 'New expedition profile');
  difficultyLabel.append(difficulty);
  const newButton = button('Begin new run', () => command('new-run', {
    choice: JSON.stringify({ seed: seedInput.value, difficulty: difficulty.value }),
  }));
  newButton.dataset.runNewButton = 'true';
  const profileReadout = element('output');
  profileReadout.dataset.runProfile = 'true';
  profileReadout.style.cssText = 'display:block;margin-top:5px;color:#c9c0ae';
  newRun.append(newSummary, seedLabel, difficultyLabel, newButton, profileReadout);

  panel.append(title, location, status, objective, result, travel, travelMessage, runActions, completionMessage, map, journal, newRun);
  root.append(panel);

  let state: Values = {};
  let selectedFloor = '';
  let activeFloor = '';
  let runIdentity = '';
  let floorSignature = '';
  let mapSignature = '';
  let notesSignature = '';
  let travelSignature = '';
  let profileSignature = '';
  let suggestedSeedSignature = '';

  const currentMap = (): Values => record(record(state.maps)[selectedFloor]);
  const safeFloor = (): string => {
    const maps = record(state.maps);
    if (selectedFloor in maps) return selectedFloor;
    const active = text(state.floorKey, '');
    return active in maps ? active : Object.keys(maps)[0] ?? '';
  };

  const drawMap = (): void => {
    const floor = currentMap();
    const cells = entries(floor.cells).map(([, cell]) => ({
      x: number(cell.x), y: number(cell.y), level: number(cell.level), discovered: cell.discovered !== false,
    })).filter(cell => cell.discovered);
    const markers = entries(floor.markers).map(([id, marker]) => ({ id, x: number(marker.x), y: number(marker.y), label: text(marker.label, id) }));
    const activeHere = selectedFloor === text(state.floorKey, '');
    const pose = { x: number(state.x), y: number(state.y), facing: text(state.facing, 'North') };
    const signature = JSON.stringify([selectedFloor, cells, markers, activeHere ? pose : null]);
    if (signature === mapSignature) return;
    mapSignature = signature;
    mapView.replaceChildren();
    if (cells.length === 0) {
      mapView.setAttribute('viewBox', '0 0 10 10');
      const empty = svgElement('text');
      svgAttributes(empty, { x: 5, y: 5, 'text-anchor': 'middle', fill: '#c9c0ae', 'font-size': 1.1 });
      empty.textContent = 'No discovered cells';
      mapView.append(empty);
      return;
    }
    const minX = Math.min(...cells.map(cell => cell.x));
    const maxX = Math.max(...cells.map(cell => cell.x));
    const minY = Math.min(...cells.map(cell => cell.y));
    const maxY = Math.max(...cells.map(cell => cell.y));
    const width = maxX - minX + 1;
    const height = maxY - minY + 1;
    mapView.setAttribute('viewBox', `${minX - 0.4} ${minY - 0.4} ${width + 0.8} ${height + 0.8}`);
    mapView.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    for (const cell of cells) {
      const tile = svgElement('rect');
      // Grid and SVG both increase Y toward the south.
      const drawY = cell.y;
      const shade = cell.level > 0 ? '#8ca65c' : cell.level < 0 ? '#5e86a5' : '#d1bc78';
      svgAttributes(tile, { x: cell.x + 0.06, y: drawY + 0.06, width: 0.88, height: 0.88, rx: 0.08, fill: shade, stroke: '#10120f', 'stroke-width': 0.06 });
      mapView.append(tile);
    }
    for (const marker of markers) {
      const mark = svgElement('path');
      mark.dataset.runMapMarker = marker.id;
      const drawY = marker.y;
      svgAttributes(mark, { d: `M ${marker.x + 0.5} ${drawY + 0.18} L ${marker.x + 0.82} ${drawY + 0.5} L ${marker.x + 0.5} ${drawY + 0.82} L ${marker.x + 0.18} ${drawY + 0.5} Z`, fill: '#f0cf6a', stroke: '#281d0f', 'stroke-width': 0.06 });
      const label = svgElement('title'); label.textContent = marker.label; mark.append(label); mapView.append(mark);
    }
    if (activeHere) {
      const arrow = svgElement('path');
      arrow.dataset.runMapParty = 'true';
      const drawY = pose.y;
      const rotation = { north: 0, east: 90, south: 180, west: 270 }[pose.facing.toLowerCase()] ?? 0;
      svgAttributes(arrow, { d: 'M 0.5 0.08 L 0.84 0.82 L 0.5 0.65 L 0.16 0.82 Z', fill: '#f5eee1', stroke: '#251914', 'stroke-width': 0.08, transform: `rotate(${rotation} ${pose.x + 0.5} ${drawY + 0.5}) translate(${pose.x} ${drawY})` });
      const label = svgElement('title'); label.textContent = `Party facing ${pose.facing}`; arrow.append(label); mapView.append(arrow);
    }
  };

  const renderFloorChoices = (): void => {
    const maps = entries(state.maps);
    const signature = JSON.stringify(maps.map(([key, map]) => [key, text(map.title, key)]));
    const desired = safeFloor();
    if (signature !== floorSignature && document.activeElement !== floorSelect) {
      floorSignature = signature;
      const previous = floorSelect.value;
      floorSelect.replaceChildren();
      for (const [key, floor] of maps) {
        const option = element('option', text(floor.title, key)); option.value = key; floorSelect.append(option);
      }
      floorSelect.value = maps.some(([key]) => key === previous) ? previous : desired;
    }
    selectedFloor = desired;
    if (floorSelect.value !== selectedFloor) floorSelect.value = selectedFloor;
  };

  const renderTravel = (): void => {
    const connectors = entries(state.connections);
    const signature = JSON.stringify(connectors);
    if (signature === travelSignature) return;
    travelSignature = signature;
    travel.replaceChildren();
    let firstProblem = '';
    for (const [id, connector] of connectors) {
      const problem = text(connector.problem, '');
      if (!firstProblem && problem) firstProblem = problem;
      const action = button(text(connector.title, id), () => command('travel', { choice: id }));
      action.dataset.runTravelChoice = id;
      action.disabled = problem.length > 0;
      action.title = problem || `Travel at ${number(connector.x)}, ${number(connector.y)}`;
      travel.append(action);
    }
    if (connectors.length === 0) travel.textContent = 'No known routes from this floor.';
    travelMessage.textContent = firstProblem || 'Reach a marked connector to travel.';
  };

  const update = (raw: unknown): void => {
    state = record(raw);
    const nextRun = text(state.id, '');
    if (runIdentity !== nextRun) {
      runIdentity = nextRun; selectedFloor = ''; activeFloor = '';
      mapSignature = ''; notesSignature = ''; suggestedSeedSignature = '';
    }
    const nextActiveFloor = text(state.floorKey, '');
    if (activeFloor !== nextActiveFloor) { activeFloor = nextActiveFloor; selectedFloor = nextActiveFloor; }
    title.textContent = text(state.title, 'Expedition');
    location.textContent = `${text(state.floor, 'Unknown floor')} · ${text(state.floorKey, 'unknown')}`;
    const runStatus = text(state.status, 'Exploring');
    status.textContent = runStatus;
    objective.textContent = text(state.objective, 'Explore the expedition.');
    const resultText = text(state.result, '');
    const finale = text(state.finaleLabel, '');
    result.textContent = runStatus === 'Defeated'
      ? resultText || 'The expedition ended in defeat.'
      : runStatus === 'Complete'
        ? [finale, resultText].filter(Boolean).join(' · ') || 'The expedition is complete.'
        : resultText;
    result.hidden = result.textContent.length === 0;
    const completionProblem = text(state.completionProblem, '');
    complete.disabled = completionProblem.length > 0;
    complete.title = completionProblem || 'Complete this expedition.';
    completionMessage.textContent = completionProblem;
    renderTravel();
    renderFloorChoices();
    const selectedMap = currentMap();
    const nextNotes = text(selectedMap.notes, 'No journal notes for this floor.');
    const signature = `${selectedFloor}\n${nextNotes}`;
    if (signature !== notesSignature) { notesSignature = signature; notes.textContent = nextNotes; }
    drawMap();

    const profiles = entries(state.difficulties);
    const currentDifficulty = text(state.difficulty, '');
    const nextProfileSignature = JSON.stringify([profiles, currentDifficulty, state.seed, state.nextSeed]);
    if (nextProfileSignature !== profileSignature && document.activeElement !== difficulty) {
      profileSignature = nextProfileSignature;
      const previous = difficulty.value;
      difficulty.replaceChildren();
      for (const [id, profile] of profiles) {
        const option = element('option', text(profile.name, id)); option.value = id; option.title = text(profile.description, ''); difficulty.append(option);
      }
      difficulty.value = profiles.some(([id]) => id === previous) ? previous : profiles.some(([id]) => id === currentDifficulty) ? currentDifficulty : profiles[0]?.[0] ?? '';
    }
    const suggestedSeed = text(state.nextSeed, text(state.seed, ''));
    if (suggestedSeedSignature !== suggestedSeed && document.activeElement !== seedInput) {
      suggestedSeedSignature = suggestedSeed;
      seedInput.value = suggestedSeed;
    }
    const activeProfile = record(record(state.difficulties)[currentDifficulty]);
    const profileDescription = text(activeProfile.description, '');
    profileReadout.textContent = currentDifficulty
      ? `Saved profile: ${text(activeProfile.name, currentDifficulty)}${profileDescription ? ` — ${profileDescription}` : ''}`
      : 'No profile is reported.';
  };

  const changeFloor = (): void => { selectedFloor = floorSelect.value; notesSignature = ''; drawMap(); update(state); };
  floorSelect.addEventListener('change', changeFloor);
  const isolate = (event: Event): void => event.stopPropagation();
  const pointerEvents = ['pointerdown', 'pointermove', 'pointerup', 'pointercancel', 'mousedown', 'mousemove', 'mouseup', 'wheel', 'click'];
  for (const event of pointerEvents) panel.addEventListener(event, isolate);
  const stopGameplayKeys = (event: KeyboardEvent): void => {
    if (!gameplayKeys.has(event.code)) return;
    event.preventDefault(); event.stopPropagation();
  };
  panel.addEventListener('keydown', stopGameplayKeys, true);
  panel.addEventListener('keyup', stopGameplayKeys, true);

  return { update, element: panel, dispose() {
    floorSelect.removeEventListener('change', changeFloor);
    for (const event of pointerEvents) panel.removeEventListener(event, isolate);
    panel.removeEventListener('keydown', stopGameplayKeys, true);
    panel.removeEventListener('keyup', stopGameplayKeys, true);
    panel.remove();
  } };
}
