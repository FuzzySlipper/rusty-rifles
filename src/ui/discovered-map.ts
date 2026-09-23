type Values = Record<string, unknown>;

const svgNamespace = 'http://www.w3.org/2000/svg';

function record(value: unknown): Values {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Values : {};
}

function entries(value: unknown): Array<[string, Values]> {
  return Object.entries(record(value)).map(([key, entry]) => [key, record(entry)]);
}

function number(value: unknown): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function text(value: unknown, fallback = ''): string {
  return value === null || value === undefined || value === '' ? fallback : String(value);
}

function svgElement(tag: string): SVGElement {
  return document.createElementNS(svgNamespace, tag);
}

function attributes(element: SVGElement, values: Record<string, string | number>): void {
  for (const [name, value] of Object.entries(values)) element.setAttribute(name, String(value));
}

export function enemyMapSignature(combatValue: unknown): string {
  return JSON.stringify(entries(record(combatValue).enemies)
    .filter(([, enemy]) => number(enemy.vitality) > 0)
    .map(([id, enemy]) => [id, text(enemy.name), number(enemy.x), number(enemy.y)]));
}

/** Draws only projected knowledge and live enemies on the current floor. */
export function drawDiscoveredMap(mapView: SVGElement, runValue: unknown, combatValue: unknown, floorKey: string): void {
  const run = record(runValue);
  const floor = record(record(run.maps)[floorKey]);
  const cells = entries(floor.cells).map(([, cell]) => ({ x: number(cell.x), y: number(cell.y), level: number(cell.level) }));
  const markers = entries(floor.markers).map(([id, marker]) => ({ id, x: number(marker.x), y: number(marker.y), label: text(marker.label, id) }));
  const activeHere = floorKey === text(run.floorKey);
  const known = new Set(cells.map(cell => `${cell.x},${cell.y}`));
  const enemies = activeHere ? entries(record(combatValue).enemies)
    .filter(([, enemy]) => number(enemy.vitality) > 0 && known.has(`${number(enemy.x)},${number(enemy.y)}`)) : [];

  mapView.replaceChildren();
  if (cells.length === 0) {
    mapView.setAttribute('viewBox', '0 0 10 10');
    const empty = svgElement('text');
    attributes(empty, { x: 5, y: 5, 'text-anchor': 'middle', fill: '#c9c0ae', 'font-size': 1.1 });
    empty.textContent = 'No discovered cells';
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
    const tile = svgElement('rect');
    const shade = cell.level > 0 ? '#8ca65c' : cell.level < 0 ? '#5e86a5' : '#d1bc78';
    attributes(tile, { x: cell.x + 0.06, y: cell.y + 0.06, width: 0.88, height: 0.88, rx: 0.08, fill: shade, stroke: '#10120f', 'stroke-width': 0.06 });
    mapView.append(tile);
  }
  for (const marker of markers) {
    const mark = svgElement('path');
    mark.dataset.runMapMarker = marker.id;
    attributes(mark, { d: `M ${marker.x + 0.5} ${marker.y + 0.18} L ${marker.x + 0.82} ${marker.y + 0.5} L ${marker.x + 0.5} ${marker.y + 0.82} L ${marker.x + 0.18} ${marker.y + 0.5} Z`, fill: '#f0cf6a', stroke: '#281d0f', 'stroke-width': 0.06 });
    const label = svgElement('title'); label.textContent = marker.label; mark.append(label); mapView.append(mark);
  }

  // Group crowd occupants by logical cell. A hollow ring remains legible even
  // when the party arrow occupies the same cell; the count exposes sharing.
  const groups = new Map<string, { x: number; y: number; names: string[] }>();
  for (const [id, enemy] of enemies) {
    const x = number(enemy.x), y = number(enemy.y), key = `${x},${y}`;
    const group = groups.get(key) ?? { x, y, names: [] };
    group.names.push(text(enemy.name, id));
    groups.set(key, group);
  }
  for (const [key, group] of groups) {
    const ring = svgElement('circle');
    ring.dataset.mapEnemy = key;
    attributes(ring, { cx: group.x + 0.5, cy: group.y + 0.5, r: 0.39, fill: 'none', stroke: '#e05b49', 'stroke-width': 0.15 });
    const label = svgElement('title'); label.textContent = `${group.names.join(', ')} (${group.x}, ${group.y})`;
    ring.append(label); mapView.append(ring);
    if (group.names.length > 1) {
      const count = svgElement('text');
      attributes(count, { x: group.x + 0.5, y: group.y + 0.9, 'text-anchor': 'middle', fill: '#fff1df', stroke: '#251914', 'stroke-width': 0.08, 'paint-order': 'stroke', 'font-size': 0.32, 'font-weight': 700 });
      count.textContent = String(group.names.length);
      mapView.append(count);
    }
  }
  if (activeHere) {
    const x = number(run.x), y = number(run.y), facing = text(run.facing, 'North');
    const rotation = { north: 0, east: 90, south: 180, west: 270 }[facing.toLowerCase()] ?? 0;
    const arrow = svgElement('path');
    arrow.dataset.runMapParty = 'true';
    attributes(arrow, { d: 'M 0.5 0.08 L 0.84 0.82 L 0.5 0.65 L 0.16 0.82 Z', fill: '#f5eee1', stroke: '#251914', 'stroke-width': 0.08, transform: `rotate(${rotation} ${x + 0.5} ${y + 0.5}) translate(${x} ${y})` });
    const label = svgElement('title'); label.textContent = `Party facing ${facing}`; arrow.append(label); mapView.append(arrow);
  }
}
