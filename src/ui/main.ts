type Envelope = Readonly<{ value: unknown }>;
type UiContext = Readonly<{
  projection?: { current(): Envelope | null; subscribe(render: (value: Envelope | null) => void): () => void };
  intents?: { claim(intent: string, value: { kind: 'product-payload'; contract: string; data: Record<string, unknown> }): void };
}>;
type ItemSelection = Readonly<{ owner: string; token: string }>;
type DragIntent = Readonly<{ owner: string; token: string; destination: string; quantity: number; revision: string; inventoryRevision: string }>;
const gameplayKeys = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'KeyF', 'KeyR', 'KeyP', 'KeyK', 'KeyL']);

function record(value: unknown): Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}; }
function entries(value: unknown): Array<[string, Record<string, unknown>]> { return Object.entries(record(value)).map(([key, entry]) => [key, record(entry)]); }
function text(value: unknown, fallback = '—'): string { return value === null || value === undefined || value === '' ? fallback : String(value); }
function numeric(value: unknown, fallback = 0): number { const number = Number(value); return Number.isFinite(number) ? number : fallback; }
function inventoryOf(state: Record<string, unknown>): Record<string, unknown> { return record(state.inventory); }
function ownerEntries(state: Record<string, unknown>): Array<[string, Record<string, unknown>]> {
  const selectedOwner = `member:${text(state.selectedMember, '')}`;
  return entries(inventoryOf(state).owners)
    .filter(([, owner]) => owner.kind === 'member' || numeric(owner.reachable) === 1)
    .sort(([leftKey, left], [rightKey, right]) => {
      const leftRank = leftKey === selectedOwner ? -1 : left.kind === 'member' ? 0 : 1;
      const rightRank = rightKey === selectedOwner ? -1 : right.kind === 'member' ? 0 : 1;
      return leftRank - rightRank || leftKey.localeCompare(rightKey);
    });
}
function itemEntries(owner: Record<string, unknown>): Array<[string, Record<string, unknown>]> { return entries(owner.items); }
function ownerRevision(owner: Record<string, unknown>): string | null { const revision = owner.revision; return revision === null || revision === undefined || revision === '' ? null : String(revision); }

/**
 * The Engine host already rejects input from descendants of its downstream UI and
 * respects defaultPrevented. This capture guard also keeps focused controls from
 * leaking movement/use/save keys to the host document listener. UI claims intent;
 * projections remain the sole displayed-state owner.
 */
export function mountProductUi(root: Element, context: UiContext): Readonly<{ dispose(): void }> {
  const panel = document.createElement('aside');
  panel.setAttribute('aria-label', 'Expedition'); panel.dataset.rustyUiInteractive = 'true';
  panel.style.cssText = 'box-sizing:border-box;color:#eee6d5;background:#171914e8;border:1px solid #74694e;border-radius:5px;font:13px/1.35 system-ui;left:12px;margin:0;max-height:calc(100vh - 24px);overflow:auto;padding:10px 12px;pointer-events:auto;position:fixed;top:12px;width:min(620px,calc(100vw - 24px))';
  const title = document.createElement('strong'); title.textContent = 'Rusty Rifles';
  const help = document.createElement('p'); help.textContent = 'W/S step · A/D sidestep · Q/E turn · F use · R cycle · P pause · K save · L load'; help.style.cssText = 'font-size:11px;margin:3px 0;color:#c9c0ae';
  const status = document.createElement('output'); status.dataset.inventoryStatus = 'true';
  const feedback = document.createElement('p'); feedback.dataset.inventoryFeedback = 'true'; feedback.setAttribute('role', 'status'); feedback.style.cssText = 'min-height:1.35em;margin:4px 0;color:#ead27e';
  const roster = document.createElement('div'); roster.dataset.roster = 'true'; roster.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:5px;margin:8px 0';
  const actions = document.createElement('nav'); actions.setAttribute('aria-label', 'Expedition controls'); actions.style.cssText = 'display:flex;flex-wrap:wrap;gap:3px';
  const focus = document.createElement('p'); focus.style.cssText = 'margin:5px 0';
  const puzzle = document.createElement('section'); puzzle.dataset.puzzle = 'true'; puzzle.style.cssText = 'margin:5px 0';
  const partyTools = document.createElement('details'); partyTools.dataset.partyTools = 'true'; partyTools.open = false; partyTools.style.cssText = 'border-top:1px solid #574f3d;margin-top:8px;padding-top:7px';
  const inventory = document.createElement('details'); inventory.dataset.inventory = 'true'; inventory.open = false; inventory.style.cssText = 'border-top:1px solid #574f3d;margin-top:8px;padding-top:7px';
  const inventoryTitle = document.createElement('summary'); inventoryTitle.textContent = 'Inventory & equipment'; inventory.append(inventoryTitle);
  const inventoryFeedback = document.createElement('p'); inventoryFeedback.dataset.inventoryFeedback = 'true'; inventoryFeedback.setAttribute('role', 'status'); inventoryFeedback.style.cssText = 'min-height:1.35em;margin:5px 0;color:#ead27e';
  const inventoryBody = document.createElement('div'); inventoryBody.dataset.inventoryBody = 'true'; inventoryBody.style.cssText = 'max-height:36vh;overflow:auto;padding-right:3px';
  const inventoryControls = document.createElement('div'); inventoryControls.dataset.inventoryControls = 'true'; inventoryControls.style.cssText = 'background:#171914f5;border-top:1px solid #574f3d;bottom:0;margin-top:7px;padding-top:7px;position:sticky';
  inventory.append(inventoryFeedback, inventoryBody, inventoryControls);

  let state: Record<string, unknown> = {};
  let selectedItem: ItemSelection | null = null;
  let hoveredItem: ItemSelection | null = null;
  let selectedDestination: string | null = null;
  let previousRoster = '', previousPartyTools = '', previousInventory = '', previousInventoryControls = '', previousPuzzle = '';
  let drag: DragIntent | null = null;
  let uiFeedback = '';
  let productFeedback = '';
  const setUiFeedback = (message: string): void => { uiFeedback = message; inventoryFeedback.textContent = message; };
  const command = (action: string, extra: Record<string, unknown> = {}, captured?: Pick<DragIntent, 'revision' | 'inventoryRevision'>): void => {
    const revision = captured?.revision ?? String(state.commandRevision ?? '');
    const inventoryRevision = captured?.inventoryRevision ?? text(inventoryOf(state).revision, '');
    const inventoryAction = new Set(['transfer', 'equip', 'unequip', 'consume', 'item-feature', 'open-container', 'close-container']);
    context.intents?.claim('rifles.command', { kind: 'product-payload', contract: 'rifles.command.v1', data: { revision, action, ...extra, ...(inventoryAction.has(action) ? { inventoryRevision } : {}) } });
  };
  const button = (label: string, action: () => void): HTMLButtonElement => {
    const element = document.createElement('button'); element.type = 'button'; element.textContent = label;
    element.style.cssText = 'background:#33392f;color:#eee6d5;border:1px solid #827556;border-radius:3px;padding:4px 6px;cursor:pointer;font:inherit'; element.addEventListener('click', action); return element;
  };
  const pause = button('Pause', () => command('pause'));
  const use = button('Use feature', () => command('use', { target: state.focusId, targetRevision: state.focusRevision }));
  actions.append(pause, button('Save', () => command('save')), button('Load', () => command('load')), button('Restart', () => command('restart')), use);

  const selectedItemData = (): Record<string, unknown> | null => {
    if (selectedItem === null) return null;
    const owner = record(record(inventoryOf(state).owners)[selectedItem.owner]); const item = record(record(owner.items)[selectedItem.token]);
    return Object.keys(item).length === 0 ? null : item;
  };
  const itemData = (selection: ItemSelection | null): Record<string, unknown> | null => {
    if (selection === null) return null;
    const owner = record(record(inventoryOf(state).owners)[selection.owner]); const item = record(record(owner.items)[selection.token]);
    return Object.keys(item).length === 0 ? null : item;
  };
  const cancelDrag = (): void => { drag = null; };
  const transfer = (source: string, token: string, destination: string, captured?: DragIntent): void => {
    if (source === destination) { setUiFeedback('Choose another inventory owner.'); return; }
    const item = record(record(record(inventoryOf(state).owners)[source]).items)[token];
    const maximum = Math.max(1, numeric(record(item).quantity, 1)); const input = inventoryControls.querySelector<HTMLInputElement>('[data-inventory-quantity]');
    const quantity = Math.min(maximum, Math.max(1, Math.floor(captured?.quantity ?? numeric(input?.value, maximum))));
    command('transfer', { source, destination, item: token, quantity }, captured); setUiFeedback('Transfer requested; waiting for the authoritative inventory update.');
  };
  const equip = (source: string, token: string, slot: string, destination = `member:${text(state.selectedMember)}`, captured?: DragIntent): void => {
    command('equip', { source, destination, item: token, slot }, captured);
    setUiFeedback('Equipment change requested; waiting for the authoritative inventory update.');
  };
  const inventoryControlsSignature = (): string => {
    const puzzleState = record(state.puzzle);
    return JSON.stringify([
      selectedItem, selectedDestination, state.selectedMember, state.focusId, state.focusRevision,
      puzzleState.doorId, puzzleState.doorRevision,
    ]);
  };
  const selectedQuantity = (): string | undefined => inventoryControls.querySelector<HTMLInputElement>('[data-inventory-quantity]')?.value;
  const refreshInventorySelection = (preservedQuantity?: string): void => {
    previousInventoryControls = inventoryControlsSignature();
    inventoryBody.querySelectorAll<HTMLElement>('[data-inventory-item]').forEach(element => {
      const match = selectedItem !== null && element.dataset.owner === selectedItem.owner && element.dataset.item === selectedItem.token;
      element.setAttribute('aria-pressed', String(match)); element.style.borderColor = match ? '#e4bd63' : '#827556';
    });
    inventoryBody.querySelectorAll<HTMLElement>('[data-inventory-owner]').forEach(element => { element.style.outline = element.dataset.owner === selectedDestination ? '1px solid #e4bd63' : ''; });
    inventoryControls.replaceChildren();
    const item = selectedItemData();
    if (selectedItem === null || item === null) { inventoryControls.textContent = 'Select an item to transfer, equip, consume, or use it.'; return; }
    const quantity = document.createElement('input'); quantity.type = 'number'; quantity.min = '1'; quantity.max = String(Math.max(1, numeric(item.quantity, 1)));
    quantity.value = String(Math.min(numeric(quantity.max, 1), Math.max(1, numeric(preservedQuantity, numeric(quantity.max, 1)))));
    quantity.dataset.inventoryQuantity = 'true'; quantity.setAttribute('aria-label', 'Transfer quantity'); quantity.style.cssText = 'width:4.5em;margin-right:5px';
    const destination = selectedDestination ?? selectedItem.owner; const destinationName = text(record(record(inventoryOf(state).owners)[destination]).name, destination);
    const itemActions = document.createElement('div'); itemActions.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;align-items:center';
    itemActions.append(quantity, button(`Transfer to ${destinationName}`, () => transfer(selectedItem!.owner, selectedItem!.token, destination)));
    const slots = text(item.allowedSlots ?? item.slots, '').split(',').map(value => value.trim()).filter(Boolean);
    for (const slot of slots)
      itemActions.append(button(`Equip ${slot}`, () => equip(selectedItem!.owner, selectedItem!.token, slot)));
    const useKind = text(item.use, '').toLowerCase();
    if (useKind === 'vitality' || useKind === 'resource') itemActions.append(button(`Use on ${text(state.selectedMember)}`, () => command('consume', { source: selectedItem!.owner, item: selectedItem!.token, member: state.selectedMember })));
    const focusTarget = numeric(state.focusId, -1);
    const focusRevision = numeric(state.focusRevision, -1);
    if (focusTarget > 0 && focusRevision > 0)
      itemActions.append(button('Use on feature', () => command('item-feature', { source: selectedItem!.owner, item: selectedItem!.token, target: focusTarget, targetRevision: focusRevision })));
    const puzzleState = record(state.puzzle);
    const doorTarget = numeric(puzzleState.doorId, -1);
    const doorRevision = numeric(puzzleState.doorRevision, -1);
    if (useKind === 'key' && doorTarget > 0 && doorRevision > 0)
      itemActions.append(button('Use key on gate', () => command('item-feature', { source: selectedItem!.owner, item: selectedItem!.token, target: doorTarget, targetRevision: doorRevision })));
    const compared = itemData(hoveredItem) ?? item;
    const compare = document.createElement('output'); compare.dataset.gearComparison = 'true'; compare.style.cssText = 'display:block;margin-top:5px;color:#c9c0ae'; compare.textContent = `${hoveredItem ? `Comparing ${text(compared.name)}: ` : 'Gear: '}power ${text(compared.power, '0')} · defense ${text(compared.defense, '0')} · requires power ${text(compared.minimumPower, '0')} · ammo ${text(compared.ammunition, '—')}`;
    inventoryControls.append(itemActions, compare);
  };
  const selectItem = (owner: string, token: string, preservedQuantity?: string): void => { selectedItem = { owner, token }; selectedDestination = owner; setUiFeedback('Selected item. Choose a destination, equipment slot, or use action.'); refreshInventorySelection(preservedQuantity); };
  const chooseOwner = (owner: string): void => { selectedDestination = owner; refreshInventorySelection(selectedQuantity()); };
  const beginDrag = (event: DragEvent, owner: string, token: string): void => {
    const sameSelection = selectedItem?.owner === owner && selectedItem.token === token;
    const item = record(record(record(inventoryOf(state).owners)[owner]).items)[token];
    const maximum = Math.max(1, numeric(record(item).quantity, 1));
    const quantity = Math.min(maximum, Math.max(1, Math.floor(numeric(sameSelection ? selectedQuantity() : undefined, maximum))));
    const captured: DragIntent = { owner, token, destination: `member:${text(state.selectedMember)}`, quantity, revision: String(state.commandRevision ?? ''), inventoryRevision: text(inventoryOf(state).revision, '') };
    drag = captured;
    event.dataTransfer?.setData('text/plain', JSON.stringify(captured));
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
    selectItem(owner, token, sameSelection ? String(quantity) : undefined);
  };
  const dropOn = (event: DragEvent, destination: string): void => {
    event.preventDefault(); event.stopPropagation(); const payload = drag;
    if (payload) transfer(payload.owner, payload.token, destination, payload); cancelDrag();
  };
  const dropOnEquipment = (event: DragEvent, slot: string): void => {
    event.preventDefault(); event.stopPropagation();
    if (drag !== null) equip(drag.owner, drag.token, slot, drag.destination, drag);
    cancelDrag();
  };
  const renderRoster = (): void => {
    const members = record(state.party); const signature = JSON.stringify([members, state.selectedMember]); if (signature === previousRoster) return; previousRoster = signature;
    const slots = ['FrontLeft', 'FrontRight', 'RearLeft', 'RearRight'];
    const sortedMembers: Array<Record<string, unknown>> = entries(members).map(([id, member]) => ({ id, ...member }));
    roster.replaceChildren(...sortedMembers.sort((left, right) => slots.indexOf(text(left.slot)) - slots.indexOf(text(right.slot))).map(member => {
      const card = button(`${text(member.name)} · ${text(member.vitality)}/${text(member.maximumVitality)} · ${text(member.resource, '0')}/${text(member.maxResource, '0')}`, () => command('select', { member: member.id }));
      card.dataset.member = String(member.id); card.setAttribute('aria-pressed', String(member.id === state.selectedMember)); card.setAttribute('aria-label', `${text(member.name)}, ${text(member.slot)}; melee ${text(member.melee, '0')}, ranged ${text(member.ranged, '0')}, casting ${text(member.casting, '0')}`); card.title = `Power ${text(member.power, '0')} · Defense ${text(member.defense, '0')} · Melee ${text(member.melee, '0')} · Ranged ${text(member.ranged, '0')} · Casting ${text(member.casting, '0')}`;
      if (member.id === state.selectedMember) card.style.borderColor = '#e4bd63'; return card;
    }));
  };
  const renderPartyTools = (): void => {
    const signature = JSON.stringify([state.party, state.presets, state.preset, state.selectedMember]); if (signature === previousPartyTools) return; previousPartyTools = signature;
    const members = entries(state.party); partyTools.replaceChildren();
    const partySummary = document.createElement('summary'); partySummary.textContent = 'Formation & party preset';
    partyTools.append(partySummary);
    const formationTitle = document.createElement('strong'); formationTitle.textContent = 'Formation';
    const first = document.createElement('select'), second = document.createElement('select'); first.dataset.formationMember = 'true'; second.dataset.formationOtherMember = 'true';
    for (const [id, member] of members) for (const select of [first, second]) { const option = document.createElement('option'); option.value = id; option.textContent = text(member.name, id); select.append(option); }
    first.value = text(state.selectedMember, members[0]?.[0] ?? ''); second.value = members.find(([id]) => id !== first.value)?.[0] ?? first.value;
    partyTools.append(formationTitle, first, second, button('Swap positions', () => command('formation', { member: first.value, otherMember: second.value })));
    const presets = entries(state.presets);
    if (presets.length > 0) { const presetTitle = document.createElement('strong'); presetTitle.textContent = 'Party preset'; presetTitle.style.marginLeft = '8px'; const select = document.createElement('select'); select.dataset.partyPreset = 'true'; for (const [id, preset] of presets) { const option = document.createElement('option'); option.value = id; option.textContent = text(preset.name, id); select.append(option); } select.value = text(state.preset, presets[0][0]); partyTools.append(presetTitle, select, button('Restart with party', () => command('choose-party', { preset: select.value }))); }
  };
  const renderInventory = (): void => {
    const inventoryState = inventoryOf(state);
    const signature = JSON.stringify([inventoryState, state.equipmentSlots]);
    if (signature === previousInventory) {
      if (previousInventoryControls !== inventoryControlsSignature()) refreshInventorySelection(selectedQuantity());
      return;
    }
    const preservedQuantity = selectedItem === null ? undefined : selectedQuantity();
    previousInventory = signature;
    const owners = ownerEntries(state); if (selectedItem && !record(record(record(inventoryState.owners)[selectedItem.owner]).items)[selectedItem.token]) selectedItem = null; if (selectedDestination && !record(inventoryState.owners)[selectedDestination]) selectedDestination = null;
    inventoryBody.replaceChildren(); const ownerList = document.createElement('div'); ownerList.style.cssText = 'display:grid;gap:7px;grid-template-columns:repeat(2,minmax(0,1fr));margin-top:6px';
    for (const [key, owner] of owners) {
      const ownerPanel = document.createElement('section'); ownerPanel.dataset.inventoryOwner = 'true'; ownerPanel.dataset.owner = key; ownerPanel.style.cssText = 'background:#10120f99;border:1px solid #574f3d;border-radius:3px;padding:5px'; ownerPanel.addEventListener('dragover', event => event.preventDefault()); ownerPanel.addEventListener('drop', event => dropOn(event, key));
      const heading = document.createElement('div'); heading.style.cssText = 'display:flex;gap:5px;align-items:center;justify-content:space-between'; const ownerButton = button(`${text(owner.name, key)} · ${text(owner.mass, '0')}/${text(owner.maxMass, '0')} mass · ${text(owner.space, '0')}/${text(owner.maxSpace, '0')} space`, () => { if (selectedItem && selectedItem.owner !== key) transfer(selectedItem.owner, selectedItem.token, key); else chooseOwner(key); }); ownerButton.dataset.ownerDestination = key; heading.append(ownerButton);
      if (owner.kind === 'container' && numeric(owner.opened) !== 1) { const revision = ownerRevision(owner); const target = numeric(owner.id, -1), targetRevision = numeric(revision, -1); const open = button('Open', () => { if (target <= 0 || targetRevision <= 0) { setUiFeedback('Container information changed; focus it and try Use again.'); return; } command('open-container', { target, targetRevision }); }); open.disabled = target <= 0 || targetRevision <= 0; heading.append(open); }
      if (owner.kind === 'container' && numeric(owner.opened) === 1) { const revision = ownerRevision(owner); const target = numeric(owner.id, -1), targetRevision = numeric(revision, -1); const close = button('Close', () => command('close-container', { target, targetRevision })); close.disabled = target <= 0 || targetRevision <= 0; heading.append(close); }
      ownerPanel.append(heading); const items = document.createElement('div'); items.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin-top:5px';
      if (owner.kind !== 'container' || numeric(owner.opened) === 1) {
        for (const [token, item] of itemEntries(owner)) {
          const itemButton = button('', () => selectItem(key, token));
          const label = document.createElement('span');
          label.textContent = `${text(item.name, token)} ×${text(item.quantity, '1')}`;
          itemButton.draggable = true; itemButton.dataset.inventoryItem = 'true'; itemButton.dataset.owner = key;
          itemButton.dataset.item = token; itemButton.dataset.definition = text(item.definition, '');
          itemButton.dataset.entity = text(item.entity, ''); itemButton.dataset.imageIndex = text(item.imageIndex, '');
          itemButton.title = `${text(item.name, token)} · ${text(item.allowedSlots ?? item.slots, 'no equipment slot')} · power ${text(item.power, '0')} · defense ${text(item.defense, '0')}`;
          itemButton.style.display = 'inline-flex'; itemButton.style.alignItems = 'center'; itemButton.style.gap = '4px';
          const imageIndex = numeric(item.imageIndex, -1);
          if (typeof item.image === 'string' && item.image.length > 0 && imageIndex >= 0 && imageIndex < 8) {
            const icon = document.createElement('span');
            const column = imageIndex % 4; const row = Math.floor(imageIndex / 4);
            icon.dataset.inventoryIcon = 'true'; icon.setAttribute('aria-hidden', 'true');
            icon.style.cssText = `background-image:url("${item.image.replaceAll('"', '%22')}");background-position:${(column * 100) / 3}% ${row * 100}%;background-repeat:no-repeat;background-size:400% 200%;display:inline-block;flex:0 0 48px;height:48px;width:48px`;
            itemButton.append(icon);
          }
          itemButton.append(label);
          itemButton.addEventListener('dragstart', event => beginDrag(event, key, token));
          itemButton.addEventListener('dragend', cancelDrag);
          itemButton.addEventListener('mouseenter', () => { hoveredItem = { owner: key, token }; if (selectedItem !== null) refreshInventorySelection(selectedQuantity()); });
          itemButton.addEventListener('mouseleave', () => { hoveredItem = null; if (selectedItem !== null) refreshInventorySelection(selectedQuantity()); });
          items.append(itemButton);
        }
      }
      if (items.childElementCount === 0) { const empty = document.createElement('span'); empty.textContent = owner.kind === 'container' && numeric(owner.opened) !== 1 ? 'Closed' : 'Empty'; empty.style.color = '#aaa18e'; items.append(empty); }
      ownerPanel.append(items); ownerList.append(ownerPanel);
    }
    const equipment = document.createElement('section'); equipment.dataset.equipment = 'true'; equipment.style.cssText = 'margin-top:7px'; const equipmentTitle = document.createElement('strong'); equipmentTitle.textContent = 'Equipped'; equipment.append(equipmentTitle); const equipmentItems = document.createElement('div'); equipmentItems.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin-top:4px';
    for (const [slot, equipped] of entries(state.equipmentSlots)) {
      const token = text(equipped.token, '');
      const name = text(equipped.name, 'Empty');
      const gear = button(`${slot}: ${name}`, () => {
        if (selectedItem !== null) { equip(selectedItem.owner, selectedItem.token, slot); return; }
        if (!token) { setUiFeedback('Select an item or drag it here to equip it.'); return; }
        command('unequip', { source: text(inventoryState.selectedOwner, `member:${text(state.selectedMember)}`), item: token });
      });
      gear.dataset.equipmentSlot = slot; gear.dataset.item = token;
      gear.title = token ? `Click to unequip ${name}; drag an item here to replace it.` : `Drag or select an item to equip to ${slot}.`;
      gear.addEventListener('dragover', event => event.preventDefault());
      gear.addEventListener('drop', event => dropOnEquipment(event, slot));
      equipmentItems.append(gear);
    }
    if (equipmentItems.childElementCount === 0) equipmentItems.textContent = 'No equipment'; equipment.append(equipmentItems); inventoryBody.append(ownerList, equipment); refreshInventorySelection(preservedQuantity);
  };
  const render = (envelope: Envelope | null): void => {
    if (!envelope) { status.textContent = 'Preparing the expedition…'; return; }
    state = record(envelope.value);
    const nextFeedback = text(state.feedback, '');
    if (nextFeedback !== productFeedback) uiFeedback = '';
    productFeedback = nextFeedback;
    status.textContent = `${text(state.status)} · ${text(state.facing)} · (${text(state.x)}, ${text(state.y)}) · ${Math.floor(numeric(state.seconds))}s · Seed ${text(state.seed)}`;
    focus.textContent = text(state.focusLabel);
    use.disabled = !state.focusId || numeric(state.paused) === 1;
    feedback.textContent = nextFeedback;
    pause.textContent = numeric(state.paused) === 1 ? 'Resume' : 'Pause';
    inventoryFeedback.textContent = uiFeedback || nextFeedback;
    artStatus.textContent = `${text(state.artStyle)} · Light ${text(state.lightPosition)} · ${numeric(state.roomLights) === 1 ? 'Room lights on' : 'Room fill off'}`;
    const puzzleState = record(state.puzzle);
    const leverTarget = numeric(puzzleState.leverId, -1);
    const leverRevision = numeric(puzzleState.leverRevision, -1);
    const puzzleSignature = JSON.stringify([puzzleState.status, leverTarget, leverRevision, state.paused]);
    if (puzzleSignature !== previousPuzzle) {
      previousPuzzle = puzzleSignature;
      puzzle.replaceChildren();
      const puzzleStatus = document.createElement('span');
      puzzleStatus.textContent = `Puzzle: ${text(puzzleState.status, 'No nearby puzzle')}`;
      puzzle.append(puzzleStatus);
      if (leverTarget > 0 && leverRevision > 0) {
        const lever = button('Use nearby lever', () => command('use', { target: leverTarget, targetRevision: leverRevision }));
        lever.dataset.puzzleLever = String(leverTarget);
        lever.disabled = numeric(state.paused) === 1;
        puzzle.append(lever);
      }
    }
    renderRoster(); renderPartyTools(); renderInventory();
  };
  const stopGameplayKeys = (event: KeyboardEvent): void => { if (!gameplayKeys.has(event.code)) return; event.preventDefault(); event.stopPropagation(); if (event.code === 'Escape') cancelDrag(); };
  const escape = (event: KeyboardEvent): void => { if (event.code === 'Escape') cancelDrag(); };
  const outside = (event: PointerEvent): void => { if (event.target instanceof Node && !panel.contains(event.target)) cancelDrag(); };
  const focusOutside = (event: FocusEvent): void => { if (!(event.relatedTarget instanceof Node) || !panel.contains(event.relatedTarget)) cancelDrag(); };
  panel.addEventListener('keydown', stopGameplayKeys, true); panel.addEventListener('keyup', stopGameplayKeys, true); panel.addEventListener('focusout', focusOutside); window.addEventListener('blur', cancelDrag); window.addEventListener('keydown', escape, true); document.addEventListener('pointerdown', outside, true);
  const art = document.createElement('details'); const artTitle = document.createElement('summary'); artTitle.textContent = 'Art comparison'; const artStatus = document.createElement('p'); art.append(artTitle, artStatus, button('Switch treatment', () => command('art-style')), button('Move light', () => command('art-light')), button('Toggle room lights', () => command('art-fill')));
  panel.append(title, help, status, roster, actions, focus, puzzle, feedback, partyTools, inventory, art); root.append(panel); render(context.projection?.current() ?? null); const unsubscribe = context.projection?.subscribe(render) ?? (() => {});
  return { dispose() { unsubscribe(); panel.removeEventListener('focusout', focusOutside); window.removeEventListener('blur', cancelDrag); window.removeEventListener('keydown', escape, true); document.removeEventListener('pointerdown', outside, true); panel.remove(); } };
}
