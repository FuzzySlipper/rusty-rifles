import { UiProfile } from './ui-profile.js';
import { mountRunPanel } from './run-panel.js';
import { mountAbilityMenu } from './ability-menu.js';
import { mountFormationPlanner } from './formation-planner.js';
import { mountBottomBar } from './bottom-bar.js';
import { mountDebugTools } from './debug.js';

type Envelope = Readonly<{ value: unknown }>;
type UiContext = Readonly<{
  projection?: { current(): Envelope | null; subscribe(render: (value: Envelope | null) => void): () => void };
  intents?: { claim(intent: string, value: { kind: 'product-payload'; contract: string; data: Record<string, unknown> }): void };
}>;
type ItemSelection = Readonly<{ owner: string; token: string }>;
type DragIntent = Readonly<{ owner: string; token: string; destination: string; quantity: number }>;
const gameplayKeys = new Set(['Space', 'KeyV', 'KeyB', 'KeyN', 'KeyC', 'KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'KeyF', 'KeyR', 'KeyT', 'KeyP', 'KeyK', 'KeyL']);

function record(value: unknown): Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}; }
function entries(value: unknown): Array<[string, Record<string, unknown>]> { return Object.entries(record(value)).map(([key, entry]) => [key, record(entry)]); }
function text(value: unknown, fallback: unknown = '—'): string { const pick = value === null || value === undefined || value === '' ? fallback : value; return pick === null || pick === undefined ? '—' : String(pick); }
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

/**
 * The Engine host already rejects input from descendants of its downstream UI and
 * respects defaultPrevented. This capture guard also keeps focused controls from
 * leaking movement/use/save keys to the host document listener. UI claims intent;
 * projections remain the sole displayed-state owner.
 */
export function mountProductUi(root: Element, context: UiContext): Readonly<{ dispose(): void }> {
  const uiProfile = new UiProfile();
  const debugTools = mountDebugTools(root);
  const panel = document.createElement('aside');
  panel.setAttribute('aria-label', 'Expedition'); panel.dataset.rustyUiInteractive = 'true';
  panel.style.cssText = 'box-sizing:border-box;color:#eee6d5;background:#171914e8;border:1px solid #74694e;border-radius:5px;font:13px/1.35 system-ui;left:12px;margin:0;max-height:calc(100vh - 80px);overflow:auto;padding:10px 12px;pointer-events:auto;position:fixed;top:12px;width:min(380px,calc(100vw - 24px))';
  const title = document.createElement('strong'); title.textContent = 'Rusty Rifles';

  const status = document.createElement('output'); status.dataset.inventoryStatus = 'true';
  const feedback = document.createElement('p'); feedback.dataset.inventoryFeedback = 'true'; feedback.setAttribute('role', 'status'); feedback.style.cssText = 'min-height:1.35em;margin:4px 0;color:#ead27e';
  const combat = document.createElement('section'); combat.dataset.combatHud = 'true'; combat.style.cssText = 'background:#251614ed;border:1px solid #aa6a4d;border-radius:4px;margin:8px 0;padding:7px';
  const combatTitle = document.createElement('strong'); combatTitle.textContent = 'Combat';
  const combatStatus = document.createElement('output'); combatStatus.dataset.combatStatus = 'true'; combatStatus.style.cssText = 'display:block;margin:3px 0;color:#ffd9bf';
  const combatTargets = document.createElement('div'); combatTargets.dataset.combatTargets = 'true'; combatTargets.style.cssText = 'display:grid;gap:4px;grid-template-columns:repeat(2,minmax(0,1fr));margin:5px 0';
  const combatMembers = document.createElement('div'); combatMembers.dataset.combatMembers = 'true'; combatMembers.style.cssText = 'display:grid;gap:3px;margin:5px 0';
  const combatActions = document.createElement('div'); combatActions.dataset.combatActions = 'true'; combatActions.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin:5px 0';
  const combatLog = document.createElement('output'); combatLog.dataset.combatLog = 'true'; combatLog.style.cssText = 'display:block;color:#e7c8b1;max-height:3.9em;overflow:auto;white-space:pre-line';
  const magicPanel = document.createElement('details'); magicPanel.dataset.spellsRecovery = 'true'; magicPanel.open = false; magicPanel.style.cssText = 'border-top:1px solid #574f3d;margin-top:8px;padding-top:7px';
  const magicTitle = document.createElement('summary'); magicTitle.textContent = 'Spells & recovery';
  const magicStatus = document.createElement('output'); magicStatus.dataset.spellStatus = 'true'; magicStatus.style.cssText = 'display:block;margin:5px 0;color:#c9c0ae';
  const spellList = document.createElement('div'); spellList.dataset.spellList = 'true'; spellList.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin:5px 0';
  const spellDetails = document.createElement('div'); spellDetails.dataset.spellDetails = 'true'; spellDetails.style.cssText = 'background:#10120f99;border:1px solid #574f3d;border-radius:3px;padding:5px';
  const spellInfo = document.createElement('output'); spellInfo.dataset.spellInfo = 'true'; spellInfo.style.cssText = 'display:block;white-space:pre-line';
  const spellTarget = document.createElement('label'); spellTarget.textContent = 'Ally target '; spellTarget.style.cssText = 'display:block;margin-top:5px';
  const allyTarget = document.createElement('select'); allyTarget.dataset.spellTarget = 'true'; allyTarget.setAttribute('aria-label', 'Spell ally target'); spellTarget.append(allyTarget);
  const spellButtons = document.createElement('div'); spellButtons.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin-top:5px';
  spellDetails.append(spellInfo, spellTarget, spellButtons);
  const spellHotbar = document.createElement('div'); spellHotbar.dataset.spellHotbar = 'true'; spellHotbar.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;margin:5px 0';
  const restControls = document.createElement('div'); restControls.dataset.restControls = 'true'; restControls.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;align-items:center;margin:6px 0';
  const restStatus = document.createElement('output'); restStatus.dataset.restStatus = 'true'; restStatus.style.cssText = 'color:#c9c0ae';
  restControls.append(restStatus);
  const advancement = document.createElement('section'); advancement.dataset.advancement = 'true'; advancement.style.cssText = 'border-top:1px solid #574f3d;margin-top:7px;padding-top:6px';
  const advancementTitle = document.createElement('strong'); advancementTitle.textContent = 'Party development';
  const advancementRows = document.createElement('div'); advancementRows.dataset.advancementRows = 'true'; advancementRows.style.cssText = 'display:grid;gap:5px;margin-top:5px';
  advancement.append(advancementTitle, advancementRows);
  magicPanel.append(magicTitle, magicStatus, spellList, spellDetails, spellHotbar, restControls, advancement);
  const roster = document.createElement('div'); roster.dataset.roster = 'true'; roster.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:5px;margin:8px 0';
  const actions = document.createElement('nav'); actions.setAttribute('aria-label', 'Expedition controls'); actions.style.cssText = 'display:flex;flex-wrap:wrap;gap:3px';
  const focus = document.createElement('p'); focus.style.cssText = 'margin:5px 0';
  const puzzle = document.createElement('section'); puzzle.dataset.puzzle = 'true'; puzzle.style.cssText = 'margin:5px 0';
  const partyTools = document.createElement('details'); partyTools.dataset.partyTools = 'true'; partyTools.open = false; partyTools.style.cssText = 'border-top:1px solid #574f3d;margin-top:8px;padding-top:7px';
  const inventory = document.createElement('details'); inventory.dataset.inventory = 'true'; inventory.open = false; inventory.setAttribute('aria-label', 'Party inventory'); inventory.dataset.rustyUiInteractive = 'true'; inventory.style.cssText = 'position:fixed;left:12px;bottom:12px;z-index:2;box-sizing:border-box;width:min(620px,calc(100vw - 24px));max-height:65vh;overflow:auto;padding:10px 12px;color:#eee6d5;background:#171914f5;border:1px solid #74694e;border-radius:5px;font:13px/1.35 system-ui;pointer-events:auto';
  const inventoryTitle = document.createElement('summary'); inventoryTitle.textContent = 'Inventory & equipment'; inventory.append(inventoryTitle);
  const inventoryFeedback = document.createElement('p'); inventoryFeedback.dataset.inventoryFeedback = 'true'; inventoryFeedback.setAttribute('role', 'status'); inventoryFeedback.style.cssText = 'min-height:1.35em;margin:5px 0;color:#ead27e';
  const inventoryBody = document.createElement('div'); inventoryBody.dataset.inventoryBody = 'true'; inventoryBody.style.cssText = 'max-height:36vh;overflow:auto;padding-right:3px';
  const inventoryControls = document.createElement('div'); inventoryControls.dataset.inventoryControls = 'true'; inventoryControls.style.cssText = 'background:#171914f5;border-top:1px solid #574f3d;bottom:0;margin-top:7px;padding-top:7px;position:sticky';
  inventory.append(inventoryFeedback, inventoryBody, inventoryControls);
  const syncPanelWidth = (): void => {
    panel.style.width = partyTools.open || magicPanel.open
      ? 'min(620px,calc(100vw - 24px))'
      : 'min(380px,calc(100vw - 24px))';
  };
  inventory.addEventListener('toggle', syncPanelWidth);
  partyTools.addEventListener('toggle', syncPanelWidth);
  magicPanel.addEventListener('toggle', syncPanelWidth);

  let state: Record<string, unknown> = {};
  let selectedItem: ItemSelection | null = null;
  let hoveredItem: ItemSelection | null = null;
  let selectedDestination: string | null = null;
  let presentedRun = '';
  let previousRoster = '', previousPartyTools = '', previousInventory = '', previousInventoryControls = '', previousPuzzle = '', previousMagicSpells = '', previousMagicTargets = '';
  let drag: DragIntent | null = null;
  let uiFeedback = '';
  let productFeedback = '';
  const setUiFeedback = (message: string): void => { uiFeedback = message; inventoryFeedback.textContent = message; };
  const command = (action: string, extra: Record<string, unknown> = {}): void => {
    context.intents?.claim('rifles.command', { kind: 'product-payload', contract: 'rifles.command.v1', data: { action, ...extra } });
  };
  const button = (label: string, action: () => void): HTMLButtonElement => {
    const element = document.createElement('button'); element.type = 'button'; element.textContent = label;
    element.style.cssText = 'background:#33392f;color:#eee6d5;border:1px solid #827556;border-radius:3px;padding:4px 6px;cursor:pointer;font:inherit'; element.addEventListener('click', action); return element;
  };
  const spellCancel = button('Cancel spell', () => command('spell-cancel')); spellCancel.dataset.spellCancel = 'true';
  const spellCast = button('Cast', () => command('cast', { spell: text(record(record(state.combat).magic).selectedSpell, ''), member: allyTarget.value || text(state.selectedMember, '') })); spellCast.dataset.spellCast = 'true';
  const spellAssign = ['0', '1', '2'].map(slot => {
    const assign = button(`Assign ${Number(slot) + 1}`, () => command('spell-assign', { spell: text(record(record(state.combat).magic).selectedSpell, ''), slot }));
    assign.dataset.spellAssign = 'true'; assign.dataset.slot = slot; return assign;
  });
  spellButtons.append(spellCancel, spellCast, ...spellAssign);
  const hotbarButtons = ['0', '1', '2'].map(slot => {
    const quick = button('', () => command('spell-hotbar', { slot })); quick.dataset.spellHotbarSlot = slot; return quick;
  });
  spellHotbar.append(...hotbarButtons);
  const rest = button('Rest', () => command('rest')); rest.dataset.rest = 'true';
  const restCancel = button('Cancel rest', () => command('rest-cancel')); restCancel.dataset.restCancel = 'true';
  restControls.append(rest, restCancel);
  const pause = button('Pause', () => command('pause'));
  const use = button('Use feature', () => command('use', { target: numeric(state.focusId), targetRevision: numeric(state.focusRevision) }));
  actions.append(pause, button('Save', () => command('save')), button('Load', () => command('load')), button('Restart', () => command('restart')), use);
  const currentCombatTarget = (): number => numeric(record(state.combat).selectedTarget, -1);
  const throwSelectedItem = (destination?: 'plate'): void => {
    if (selectedItem === null) { setUiFeedback('Select an inventory item before throwing it.'); return; }
    const payload: Record<string, unknown> = { source: selectedItem.owner, item: selectedItem.token };
    if (destination) payload.destination = destination;
    else {
      const target = currentCombatTarget();
      if (target <= 0) { setUiFeedback('Select a visible enemy before throwing an item.'); return; }
      payload.target = target;
    }
    command('throw', payload);
  };
  const attack = button('Fire [Space]', () => command('fire'));
  const melee = button('Melee [V]', () => command('melee'));
  const reload = button('Reload [R]', () => command('reload')); reload.dataset.order = 'reload';
  attack.dataset.order = 'fire'; melee.dataset.order = 'melee';
  const fixBayonets = button('Fix bayonets [B]', () => command('fix-bayonets'));
  const unfixBayonets = button('Unfix bayonets [N]', () => command('unfix-bayonets'));
  const bolt = button('Spark', () => { magicPanel.open = true; command('spell-select', { spell: 'spark' }); }); bolt.dataset.spellShortcut = 'spark';
  const interrupt = button('Interrupt', () => command('interrupt'));
  const toss = button('Throw selected', () => throwSelectedItem());
  const plate = button('Toss onto plate', () => throwSelectedItem('plate'));
  combatActions.append(attack, melee, reload, fixBayonets, unfixBayonets, bolt, interrupt, toss, plate);
  combat.append(combatTitle, combatStatus, combatTargets, combatMembers, combatActions, combatLog);
  const enemyRows = new Map<string, Readonly<{ row: HTMLElement; target: HTMLButtonElement; details: HTMLOutputElement }>>();
  const memberRows = new Map<string, HTMLOutputElement>();
  const advancementMemberRows = new Map<string, Readonly<{ row: HTMLElement; status: HTMLOutputElement; choice: HTMLSelectElement; advance: HTMLButtonElement }>>();

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
    command('transfer', { source, destination, item: token, quantity }); setUiFeedback('Transfer requested; waiting for the authoritative inventory update.');
  };
  const equip = (source: string, token: string, slot: string, destination = `member:${text(state.selectedMember)}`, captured?: DragIntent): void => {
    command('equip', { source, destination, item: token, slot });
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
    const captured: DragIntent = { owner, token, destination: `member:${text(state.selectedMember)}`, quantity };
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
    const sortedMembers: Array<Record<string, unknown>> = entries(members).map(([id, member]) => ({ id, ...member }));
    const present = new Set<string>();
    for (const member of sortedMembers.sort((left, right) => numeric(left.rank) - numeric(right.rank) || text(left.name).localeCompare(text(right.name)))) {
      const id = String(member.id); present.add(id);
      let card = roster.querySelector<HTMLButtonElement>(`button[data-member="${CSS.escape(id)}"]`);
      if (!card) { card = button('', () => command('select', { member: id })); card.dataset.member = id; }
      const commander = numeric(member.commander) === 1;
      const role = commander ? 'Commander' : 'Soldier';
      const name = text(member.name, id);
      const protection = text(member.protection, 'Protection unavailable');
      card.textContent = `${role} · ${name} · ${text(member.vitality)}/${text(member.maximumVitality)} · ${text(member.resource, '0')}/${text(member.maxResource, '0')}`;
      card.setAttribute('aria-pressed', String(id === state.selectedMember));
      card.setAttribute('aria-label', `${role} ${name}, ${text(member.positionName, member.position)}; ${protection}; orders follow the equipped weapon's reach and readiness`);
      card.title = `${commander ? 'Fixed at the center' : text(member.positionName, member.position)} · ${protection} · Power ${text(member.power, '0')} · Defense ${text(member.defense, '0')}`;
      card.style.borderColor = id === state.selectedMember ? '#e4bd63' : '#827556';
      // Keep focused controls alive while health and recovery values change.
      const index = sortedMembers.findIndex(candidate => candidate.id === member.id);
      if (roster.children[index] !== card) roster.insertBefore(card, roster.children[index] ?? null);
    }
    roster.querySelectorAll<HTMLButtonElement>('button[data-member]').forEach(card => {
      if (!present.has(card.dataset.member!)) card.remove();
    });
  };
  const renderPartyTools = (): void => {
    const signature = JSON.stringify([entries(state.party).map(([id, member]) => [id, member.name, member.position, member.commander]), state.positions, state.presets, state.preset, state.selectedMember]); if (signature === previousPartyTools) return; previousPartyTools = signature;
    partyTools.replaceChildren();
    const partySummary = document.createElement('summary'); partySummary.textContent = 'Formation & party preset';
    partyTools.append(partySummary, button('Change formation', () => command('formation-open')));
    const presets = entries(state.presets);
    if (presets.length > 0) { const presetTitle = document.createElement('strong'); presetTitle.textContent = 'Party preset'; presetTitle.style.marginLeft = '8px'; const select = document.createElement('select'); select.dataset.partyPreset = 'true'; for (const [id, preset] of presets) { const option = document.createElement('option'); option.value = id; option.textContent = text(preset.name, id); select.append(option); } select.value = text(state.preset, presets[0][0]); partyTools.append(presetTitle, select, button('Restart with party', () => command('choose-party', { preset: select.value }))); }
  };
  const renderInventory = (): void => {
    // The drag captures item identity. Preserve its DOM source until drop/cancel;
    // the authoritative command still rejects changed ownership or lost reach.
    if (drag !== null) return;
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
      if (owner.kind === 'container' && numeric(owner.opened) !== 1) { const target = numeric(owner.id, -1); const open = button('Open', () => { if (target <= 0) { setUiFeedback('Container information changed; focus it and try Use again.'); return; } command('open-container', { target }); }); open.disabled = target <= 0; heading.append(open); }
      if (owner.kind === 'container' && numeric(owner.opened) === 1) { const target = numeric(owner.id, -1); const close = button('Close', () => command('close-container', { target })); close.disabled = target <= 0; heading.append(close); }
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
  const renderMagic = (): void => {
    const magicState = record(record(state.combat).magic);
    magicPanel.hidden = Object.keys(magicState).length === 0;
    if (magicPanel.hidden) return;
    const spells = record(magicState.spells);
    const selectedSpell = text(magicState.selectedSpell, '');
    const selected = record(spells[selectedSpell]);
    const selectedKnown = numeric(selected.known) === 1;
    const selectedAvailable = numeric(selected.available) === 1;
    const spellSignature = JSON.stringify([spells, selectedSpell]);
    if (spellSignature !== previousMagicSpells) {
      previousMagicSpells = spellSignature;
      spellList.replaceChildren();
      for (const [id, spell] of entries(spells)) {
        if (numeric(spell.known) !== 1) continue;
        const select = button(text(spell.name, id), () => command('spell-select', { spell: id }));
        select.dataset.spellSelect = id; select.dataset.spell = id; select.setAttribute('aria-pressed', String(id === selectedSpell));
        select.title = text(spell.description, 'No description');
        if (id === selectedSpell) select.style.borderColor = '#e4bd63';
        spellList.append(select);
      }
      if (spellList.childElementCount === 0) spellList.textContent = 'No spells are known.';
    }
    const targetSignature = JSON.stringify([entries(state.party).map(([id, member]) => [id, member.name]), state.selectedMember]);
    if (targetSignature !== previousMagicTargets && document.activeElement !== allyTarget) {
      previousMagicTargets = targetSignature;
      const previousTarget = allyTarget.value;
      allyTarget.replaceChildren();
      for (const [id, member] of entries(state.party)) {
        const option = document.createElement('option'); option.value = id; option.textContent = text(member.name, id); allyTarget.append(option);
      }
      const selectedMember = text(state.selectedMember, '');
      allyTarget.value = entries(state.party).some(([id]) => id === previousTarget)
        ? previousTarget
        : entries(state.party).some(([id]) => id === selectedMember) ? selectedMember : entries(state.party)[0]?.[0] ?? '';
    }
    if (!selectedKnown) {
      spellInfo.textContent = 'Select a known spell to inspect it, assign a quick slot, or cast it.';
    } else {
      const unavailable = selectedAvailable ? 'Available' : `Unavailable: ${text(selected.reason, 'No reason reported')}`;
      spellInfo.textContent = `${text(selected.name, selectedSpell)} · ${text(selected.target, 'target unknown')}\n${text(selected.description, 'No description')}\nCost ${text(selected.cost, '0')} · windup ${text(selected.windup, '0')}s · recovery ${text(selected.recovery, '0')}s\n${unavailable}`;
    }
    // display, not hidden: the label carries display:block, which overrides
    // the hidden attribute (see the readme view NOTE). See F10.
    spellTarget.style.display = selectedKnown ? 'block' : 'none';
    spellCancel.disabled = !selectedKnown;
    spellCast.disabled = !selectedKnown || !selectedAvailable;
    for (const assign of spellAssign) assign.disabled = !selectedKnown;
    const hotbar = record(magicState.hotbar);
    for (const [index, slot] of ['0', '1', '2'].entries()) {
      const spellId = text(hotbar[slot], '');
      const assigned = record(spells[spellId]);
      hotbarButtons[index].textContent = `${index + 1}: ${spellId && numeric(assigned.known) === 1 ? text(assigned.name, spellId) : 'Empty'}`;
      hotbarButtons[index].disabled = !spellId || numeric(assigned.known) !== 1;
      hotbarButtons[index].title = spellId && numeric(assigned.known) === 1 ? text(assigned.description, '') : 'Assign the selected spell to this quick slot.';
    }
    const restState = record(magicState.rest);
    const resting = numeric(restState.active) === 1;
    const restReason = text(restState.reason, '');
    restStatus.textContent = resting
      ? `Resting${numeric(restState.remaining) > 0 ? ` · ${numeric(restState.remaining).toFixed(1)}s remaining` : ''}${restReason ? ` · ${restReason}` : ''}`
      : restReason ? restReason : 'Recovery is ready when the expedition is safe.';
    rest.disabled = resting;
    restCancel.disabled = !resting;

    const choices = record(magicState.choices);
    const choiceSignature = JSON.stringify(choices);
    const presentMembers = new Set<string>();
    for (const [id, member] of entries(magicState.members)) {
      presentMembers.add(id);
      let row = advancementMemberRows.get(id);
      if (!row) {
        const element = document.createElement('div'); element.dataset.advancementMember = id; element.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;align-items:center';
        const memberStatus = document.createElement('output'); memberStatus.dataset.memberDevelopment = id; memberStatus.style.cssText = 'flex:1 1 100%';
        const choice = document.createElement('select'); choice.dataset.advancementChoice = id; choice.setAttribute('aria-label', `${text(member.name, id)} advancement choice`);
        const advance = button('Advance', () => command('advance', { member: id, choice: choice.value })); advance.dataset.advance = id;
        element.append(memberStatus, choice, advance); advancementRows.append(element);
        row = { row: element, status: memberStatus, choice, advance }; advancementMemberRows.set(id, row);
      }
      if (row.choice.dataset.choiceSignature !== choiceSignature && document.activeElement !== row.choice) {
        const previousChoice = row.choice.value;
        row.choice.replaceChildren();
        for (const [choiceId, choice] of entries(choices)) {
          const option = document.createElement('option'); option.value = choiceId; option.textContent = text(choice.name, choiceId); option.title = text(choice.description, ''); row.choice.append(option);
        }
        row.choice.value = entries(choices).some(([choiceId]) => choiceId === previousChoice) ? previousChoice : entries(choices)[0]?.[0] ?? '';
        row.choice.dataset.choiceSignature = choiceSignature;
      }
      const conditions = text(member.conditions, 'Healthy').split(/[,;|]/).map(value => value.trim()).filter(Boolean).map(value => value.charAt(0).toUpperCase() + value.slice(1)).join(', ') || 'Healthy';
      row.status.textContent = `${text(member.name, id)} · ${conditions} · ${text(member.experience, '0')} XP · ${text(member.unspent, '0')} unspent · ${text(member.revivals, '0')} revivals`;
      row.advance.disabled = numeric(member.unspent) <= 0 || !row.choice.value;
      row.advance.title = row.advance.disabled ? 'Earn an unspent advancement before choosing this benefit.' : text(record(choices[row.choice.value]).description, 'Apply this advancement.');
      advancementRows.append(row.row);
    }
    for (const [id, row] of advancementMemberRows) {
      if (!presentMembers.has(id)) { row.row.remove(); advancementMemberRows.delete(id); }
    }
    advancement.hidden = presentMembers.size === 0;
    magicStatus.textContent = selectedKnown
      ? `${text(selected.name, selectedSpell)} selected${selectedAvailable ? '' : ` · ${text(selected.reason, 'Unavailable')}`}`
      : 'Choose a known spell. Selection and cancellation do not spend resources.';
  };
  const renderCombat = (): void => {
    const combatState = record(state.combat);
    combat.hidden = Object.keys(combatState).length === 0;
    if (combat.hidden) return;
    const selectedTarget = text(combatState.selectedTarget, '');
    const selectedEnemy = record(record(combatState.enemies)[selectedTarget]);
    const defeated = numeric(combatState.defeated) === 1;
    combatStatus.textContent = defeated ? 'The commander has fallen.' : 'Orders attack forward. Turn the party to change direction.';
    const targetVisible = numeric(selectedEnemy.visible) === 1;
    for (const [kind, control] of [['fire', attack], ['melee', melee], ['reload', reload], ['fix-bayonets', fixBayonets], ['unfix-bayonets', unfixBayonets]] as const) {
      const order = record(record(combatState.orders)[kind]);
      control.textContent = `${{ fire: 'Fire [Space]', melee: 'Melee [V]', reload: 'Reload [R]', 'fix-bayonets': 'Fix [B]', 'unfix-bayonets': 'Unfix [N]' }[kind]} · ${numeric(order.eligible)}/${numeric(order.total)}`;
      control.disabled = defeated || numeric(state.paused) === 1 || numeric(order.eligible) === 0;
      control.title = entries(order.members).map(([id, member]) => `${text(record(record(state.party)[id]).name, id)}: ${numeric(member.eligible) === 1 ? (text(member.target) ? `target ${text(member.target)} · ${text(member.lane)}` : text(member.reason, 'Ready')) : text(member.reason)}`).join('\n');
    }
    bolt.disabled = defeated;
    toss.disabled = defeated || selectedItem === null || !targetVisible;
    plate.disabled = defeated || selectedItem === null;

    const presentEnemies = new Set<string>();
    for (const [id, enemy] of entries(combatState.enemies)) {
      presentEnemies.add(id);
      let row = enemyRows.get(id);
      if (!row) {
        const element = document.createElement('div');
        element.dataset.combatEnemy = id; element.style.cssText = 'display:grid;gap:2px';
        const target = button('', () => {
          const targetId = numeric(id, -1);
          if (targetId > 0) command('target', { target: targetId });
        });
        const details = document.createElement('output'); details.style.cssText = 'color:#e7c8b1;font-size:11px';
        element.append(target, details); combatTargets.append(element);
        row = { row: element, target, details }; enemyRows.set(id, row);
      }
      const remaining = Math.max(0, numeric(enemy.remaining));
      row.target.textContent = `${text(enemy.name, id)} · ${text(enemy.vitality, '0')}/${text(enemy.maxVitality, '0')}`;
      row.target.title = `${text(enemy.name, id)} · ${text(enemy.position)}${numeric(enemy.visible) === 1 ? '' : ' · out of sight'}`;
      row.target.dataset.target = id; row.target.setAttribute('aria-pressed', String(id === selectedTarget));
      row.target.disabled = numeric(enemy.visible) !== 1 || numeric(id, -1) <= 0 || defeated;
      row.details.textContent = `${text(enemy.kind)} · ${text(enemy.phase)}${remaining > 0 ? ` ${remaining.toFixed(1)}s` : ''} · ${text(enemy.conditions)}`;
    }
    for (const [id, row] of enemyRows) {
      if (!presentEnemies.has(id)) { row.row.remove(); enemyRows.delete(id); }
    }

    const party = record(state.party);
    const presentMembers = new Set<string>();
    for (const [id, member] of entries(combatState.members)) {
      presentMembers.add(id);
      let row = memberRows.get(id);
      if (!row) {
        row = document.createElement('output'); row.dataset.combatMember = id;
        combatMembers.append(row); memberRows.set(id, row);
      }
      const partyMember = record(party[id]);
      const remaining = Math.max(0, numeric(member.remaining));
      row.textContent = `${text(partyMember.name, id)} · ${text(member.phase)}${remaining > 0 ? ` ${remaining.toFixed(1)}s` : ''} · ${text(member.weapon, 'unarmed')}${numeric(member.reloadSeconds, 0) > 0 ? ` · ${numeric(member.loaded) === 1 ? 'loaded' : 'unloaded'}` : ''}${numeric(member.reloadSeconds, 0) > 0 ? ` · bayonet ${numeric(member.bayonetFixed, 0) === 1 ? 'fixed' : 'unfixed'} · ${Math.round(numeric(member.accuracy, 0) * 100)}% accuracy · reload ${numeric(member.reloadSeconds, 0).toFixed(1)}s` : ''}`;
    }
    for (const [id, row] of memberRows) {
      if (!presentMembers.has(id)) { row.remove(); memberRows.delete(id); }
    }
    const log = text(combatState.log, '');
    if (combatLog.textContent !== log) { combatLog.textContent = log; combatLog.scrollTop = combatLog.scrollHeight; }
  };
  // Party panel interactions. Click alternatives mirror the drag paths:
  // cell-to-cell arranges, cross-owner drops transfer into the party, and
  // equip rows run the shared equip flow (which swaps displaced gear home).
  const dropOnPartyCell = (event: DragEvent, slot: number): void => {
    event.preventDefault(); event.stopPropagation();
    const payload = drag;
    if (!payload) return;
    if (payload.owner === 'party') command('arrange', { source: 'party', item: payload.token, partySlot: slot });
    else transfer(payload.owner, payload.token, 'party', payload);
    cancelDrag();
  };
  const clickPartyCell = (slot: number, token: string): void => {
    if (selectedItem !== null && selectedItem.owner === 'party' && selectedItem.token === token) return;
    if (selectedItem !== null && selectedItem.owner === 'party') {
      command('arrange', { source: 'party', item: selectedItem.token, partySlot: slot });
      setUiFeedback('Rearrange requested; waiting for the authoritative inventory update.');
      return;
    }
    if (selectedItem !== null) { transfer(selectedItem.owner, selectedItem.token, 'party'); return; }
    if (token) selectItem('party', token);
    else setUiFeedback('Select an item, then choose a grid cell — or drag it here.');
  };
  const clickEquipmentRow = (slot: string, memberKey: string, token: string): void => {
    if (selectedItem !== null) { equip(selectedItem.owner, selectedItem.token, slot, memberKey); return; }
    if (!token) { setUiFeedback('Select an item or drag it here to equip it.'); return; }
    command('unequip', { source: memberKey, item: token });
  };
  // Cached controls: projections arrive continuously, so grid cells and
  // equipment rows persist by key and update in place instead of rebuilding.
  const partyCellCache = new Map<number, HTMLButtonElement>();
  const partyEquipCache = new Map<string, { row: HTMLElement; gear: HTMLButtonElement }>();
  let previousPartyPanel = '';
  const paintPartyCell = (cell: HTMLButtonElement, slot: number, found: [string, Record<string, unknown>] | undefined): void => {
    cell.dataset.partySlot = String(slot);
    cell.replaceChildren();
    delete cell.dataset.partyItem;
    if (!found) {
      cell.textContent = '+';
      cell.style.borderStyle = 'dashed';
      cell.style.color = '#5a5348';
      cell.title = `Empty grid slot ${slot + 1}. Drop an item here, or select one and click.`;
      cell.setAttribute('aria-label', cell.title);
      return;
    }
    const [token, item] = found;
    cell.dataset.partyItem = token;
    cell.style.borderStyle = 'solid';
    cell.style.color = '#f0e6d2';
    const icon = gridIcon(item, 30);
    if (icon) cell.append(icon);
    else cell.textContent = text(item.name, token).slice(0, 2).toUpperCase();
    const quantity = numeric(item.quantity, 1);
    if (quantity > 1) {
      const badge = document.createElement('span');
      badge.textContent = String(quantity);
      badge.setAttribute('aria-hidden', 'true');
      badge.style.cssText = 'position:absolute;right:2px;bottom:2px;background:#33392f;border:1px solid #827556;border-radius:3px;padding:0 3px;font:700 10px/1.4 system-ui';
      cell.append(badge);
    }
    const name = text(item.name, token);
    cell.title = `${name}${quantity > 1 ? ` ×${quantity}` : ''} · power ${text(item.power, '0')} · defense ${text(item.defense, '0')} · drag to move, click to select`;
    cell.setAttribute('aria-label', `${name}, grid slot ${slot + 1}${quantity > 1 ? `, quantity ${quantity}` : ''}.`);
  };
  const renderPartyPanel = (): void => {
    if (drag !== null) return;
    const inventoryState = inventoryOf(state);
    const equipped = record(record(record(state.combat).members)[text(state.selectedMember)]);
    const signature = JSON.stringify([inventoryState, state.equipmentSlots, state.selectedMember, state.party, equipped.meleeReach, equipped.fireReach]);
    if (signature === previousPartyPanel) return;
    previousPartyPanel = signature;
    const memberId = text(state.selectedMember, '');
    const memberKey = `member:${memberId}`;
    const member = record(record(state.party)[memberId]);
    memberName.textContent = `${text(member.name, memberId)} · ${text(member.vitality, '?')}/${text(member.maximumVitality, '?')}`;
    const statLines = [
      `Health ${text(member.vitality, '?')}/${text(member.maximumVitality, '?')}`,
      `Energy ${text(member.resource, '0')}/${text(member.maxResource, '0')}`,
      `Power ${text(member.power, '0')} · Defense ${text(member.defense, '0')}`,
      `Weapon reach: melee ${text(equipped.meleeReach, 'none')} · fire ${text(equipped.fireReach, 'none')}`,
      `Station ${text(member.positionName, member.position)}`,
      `Facing ${text(member.facing, '')}`,
    ];
    if (memberStats.childElementCount !== statLines.length) {
      memberStats.replaceChildren();
      for (const _line of statLines) memberStats.append(document.createElement('span'));
    }
    Array.from(memberStats.children).forEach((child, index) => { (child as HTMLElement).textContent = statLines[index] ?? ''; });
    for (const [slot, equipped] of entries(state.equipmentSlots)) {
      let row = partyEquipCache.get(slot);
      if (!row) {
        const element = document.createElement('div');
        element.style.cssText = 'flex:1;display:flex;flex-direction:column;gap:3px;align-items:stretch;min-width:0';
        const label = document.createElement('span');
        label.textContent = slot;
        label.style.cssText = 'color:#e4bd63;font-size:10px;letter-spacing:0.1em;text-transform:uppercase;text-align:center;white-space:nowrap;overflow:hidden;text-overflow:ellipsis';
        const gear = button('', () => {});
        gear.style.cssText = 'position:relative;height:72px;width:100%;padding:0;background:#10120f99;border:1px solid #574f3d;border-radius:3px;cursor:pointer;display:flex;align-items:center;justify-content:center;overflow:hidden';
        gear.dataset.partyEquipSlot = slot;
        gear.addEventListener('click', () => clickEquipmentRow(slot, `member:${text(state.selectedMember, '')}`, gear.dataset.partyItem ?? ''));
        gear.addEventListener('dragover', event => event.preventDefault());
        gear.addEventListener('drop', event => dropOnEquipment(event, slot));
        gear.addEventListener('dragstart', event => {
          const worn = gear.dataset.partyItem;
          if (!worn) { event.preventDefault(); return; }
          beginDrag(event, `member:${text(state.selectedMember, '')}`, worn);
        });
        gear.addEventListener('dragend', cancelDrag);
        element.append(label, gear);
        equipmentList.append(element);
        row = { row: element, gear };
        partyEquipCache.set(slot, row);
      }
      const token = text(record(equipped).token, '');
      const name = text(record(equipped).name, 'Empty');
      const detail = record(record(record(record(inventoryState.owners)[memberKey]).items)[token]);
      row.gear.replaceChildren();
      if (token) {
        const icon = gridIcon(detail, 44);
        if (icon) row.gear.append(icon);
        else row.gear.textContent = name.slice(0, 2).toUpperCase();
        row.gear.style.borderStyle = 'solid';
        row.gear.style.fontSize = '';
        row.gear.style.color = '#f0e6d2';
      } else {
        row.gear.textContent = 'Empty';
        row.gear.style.fontSize = '11px';
        row.gear.style.color = '#5a5348';
        row.gear.style.borderStyle = 'dashed';
      }
      row.gear.dataset.partyItem = token;
      row.gear.draggable = token !== '';
      if (token && detail.allowedSlots !== undefined) {
        row.gear.title = `${name} — power ${text(detail.power, '0')} · click to unequip, drag to the grid.`;
      } else {
        row.gear.title = token ? `${name} — click to unequip, drag to the grid.` : `${slot} is empty — drop or select an item to equip.`;
      }
      row.gear.setAttribute('aria-label', `${slot}: ${name}.`);
    }
    for (const [slot, cached] of partyEquipCache) {
      if (!record(state.equipmentSlots)[slot]) { cached.row.remove(); partyEquipCache.delete(slot); }
    }
    const owner = partyOwner();
    const count = numeric(owner.gridSlots, 0);
    const bySlot = new Map<number, [string, Record<string, unknown>]>();
    for (const [token, item] of itemEntries(owner)) {
      const at = numeric(record(item).slot, -1);
      if (at >= 0 && at < count && !bySlot.has(at)) bySlot.set(at, [token, record(item)]);
    }
    const live = new Set<number>();
    for (let slot = 0; slot < count; slot++) {
      live.add(slot);
      let cell = partyCellCache.get(slot);
      if (!cell) {
        const fresh = gridCellButton('');
        fresh.addEventListener('click', () => clickPartyCell(numeric(fresh.dataset.partySlot, -1), fresh.dataset.partyItem ?? ''));
        fresh.addEventListener('dragover', event => event.preventDefault());
        fresh.addEventListener('drop', event => dropOnPartyCell(event, numeric(fresh.dataset.partySlot, -1)));
        fresh.addEventListener('dragstart', event => {
          const token = fresh.dataset.partyItem;
          if (!token) { event.preventDefault(); return; }
          beginDrag(event, 'party', token);
        });
        fresh.addEventListener('dragend', cancelDrag);
        partyCellCache.set(slot, fresh);
        partyGrid.append(fresh);
        cell = fresh;
      }
      paintPartyCell(cell, slot, bySlot.get(slot));
      cell.draggable = bySlot.has(slot);
    }
    for (const [slot, cell] of partyCellCache) {
      if (!live.has(slot)) { cell.remove(); partyCellCache.delete(slot); }
    }
    gridStatus.textContent = `Party load ${text(owner.mass, '0')}/${text(owner.maxMass, '0')} · ${bySlot.size}/${count} slots`;
  };
  const renderState = (envelope: Envelope | null): void => {
    if (!envelope) { status.textContent = 'Preparing the expedition…'; bottomBar.update({}); return; }
    state = record(envelope.value);
    const nextRun = text(record(state.run).id, '');
    if (presentedRun !== nextRun) {
      presentedRun = nextRun;
      cancelDrag(); selectedItem = null; hoveredItem = null; selectedDestination = null;
      uiFeedback = ''; previousInventory = ''; previousInventoryControls = ''; previousPartyPanel = '';
      previousMagicTargets = ''; allyTarget.value = '';
    }
    runPanel.update(state.run);
    bottomBar.update(state);
    formationPlanner.update(state);
    abilityMenu.update(state);
    const nextFeedback = text(state.feedback, '');
    if (nextFeedback !== productFeedback) uiFeedback = '';
    productFeedback = nextFeedback;
    status.textContent = `${text(state.status)} · ${text(state.room, 'Passage')} · Level ${text(state.level, '0')} · ${text(state.facing)} · (${text(state.x)}, ${text(state.y)}) · ${Math.floor(numeric(state.seconds))}s · Seed ${text(state.seed)}`;
    focus.textContent = text(state.focusLabel);
    use.disabled = !state.focusId || numeric(state.paused) === 1;
    feedback.textContent = nextFeedback;
    pause.textContent = numeric(state.paused) === 1 ? 'Resume' : 'Pause';
    inventoryFeedback.textContent = uiFeedback || nextFeedback;
    partyStatus.textContent = uiFeedback || nextFeedback;
    menuStatus.textContent = uiFeedback || nextFeedback;
    menuPause.textContent = numeric(state.paused) === 1 ? 'Pause: on' : 'Pause: off';
    // A detour carry only means "menu-caused pause outstanding" while the
    // game stays paused. Any observed live projection retires it, so a stale
    // carry can never disarm a later manually-paused entry.
    if (numeric(state.paused) !== 1) menuDetourLive = false;
    artStatus.textContent = `${text(state.artStyle)} · Light ${text(state.lightPosition)} · ${numeric(state.roomLights) === 1 ? 'Room lights on' : 'Room fill off'}`;
    const puzzleState = record(state.puzzle);
    const leverTarget = numeric(puzzleState.leverId, -1);
    const leverRevision = numeric(puzzleState.leverRevision, -1);
    const puzzleSignature = JSON.stringify([puzzleState.status, leverTarget, leverRevision, state.paused]);
    if (puzzleSignature !== previousPuzzle) {
      previousPuzzle = puzzleSignature;
      puzzle.replaceChildren();
      const puzzleStatus = document.createElement('span');
      puzzleStatus.textContent = `Arrival-room puzzle: ${text(puzzleState.status, 'No nearby puzzle')}`;
      puzzle.append(puzzleStatus);
      if (leverTarget > 0 && leverRevision > 0) {
        const lever = button('Use nearby lever', () => command('use', { target: leverTarget, targetRevision: leverRevision }));
        lever.dataset.puzzleLever = String(leverTarget);
        lever.disabled = numeric(state.paused) === 1;
        puzzle.append(lever);
      }
    }
    renderRoster(); renderPartyTools(); renderInventory(); renderPartyPanel(); renderCombat(); renderMagic();
  };
  const render = (envelope: Envelope | null): void => uiProfile.measure(() => renderState(envelope));
  const stopGameplayKeys = (event: KeyboardEvent): void => { if (!gameplayKeys.has(event.code)) return; event.preventDefault(); event.stopPropagation(); };
  const escape = (event: KeyboardEvent): void => { if (event.code === 'Escape') cancelDrag(); };
  const outside = (event: PointerEvent): void => { if (event.target instanceof Node && !panel.contains(event.target) && !inventory.contains(event.target)) cancelDrag(); };
  const focusOutside = (event: FocusEvent): void => { if (!(event.relatedTarget instanceof Node) || !panel.contains(event.relatedTarget) && !inventory.contains(event.relatedTarget)) cancelDrag(); };
  inventory.addEventListener('keydown', stopGameplayKeys, true); inventory.addEventListener('keyup', stopGameplayKeys, true); inventory.addEventListener('focusout', focusOutside);
  panel.addEventListener('keydown', stopGameplayKeys, true); panel.addEventListener('keyup', stopGameplayKeys, true); panel.addEventListener('focusout', focusOutside); window.addEventListener('blur', cancelDrag); window.addEventListener('keydown', escape, true); document.addEventListener('pointerdown', outside, true);
  const art = document.createElement('details'); const artTitle = document.createElement('summary'); artTitle.textContent = 'Art comparison'; const artStatus = document.createElement('p'); art.append(artTitle, artStatus, button('Switch treatment', () => command('art-style')), button('Move light', () => command('art-light')), button('Toggle room lights', () => command('art-fill')));
  panel.append(title, status, roster, actions, focus, feedback, combat, magicPanel, partyTools, puzzle, art); root.append(panel, inventory); const runPanel = mountRunPanel(root, command); const bottomBar = mountBottomBar(root, (action, extra = {}) => command(action, extra), () => { partyPanel.hidden = false; });
  const formationPlanner = mountFormationPlanner(root, command);
  const abilityMenu = mountAbilityMenu(root, bottomBar.element.querySelector('[aria-label="Party orders"]')!, command);
  // Party panel: the game-UI inventory. Right side, full height above the
  // bottom bar. Current-member equipment plus a party cycler on top, the
  // fixed party grid below. All mutations reuse the legacy flows (transfer,
  // equip, arrange) and server-side validation; this panel only lays out the
  // new model (shared party grid, equipment-only members).
  const partyPanel = document.createElement('aside');
  partyPanel.dataset.partyPanel = 'true';
  partyPanel.hidden = true;
  partyPanel.setAttribute('aria-label', 'Party inventory');
  partyPanel.dataset.rustyUiInteractive = 'true';
  partyPanel.style.cssText = 'box-sizing:border-box;position:fixed;right:12px;top:12px;bottom:192px;z-index:1;width:320px;overflow:auto;padding:10px 12px;color:#eee6d5;background:#171914f5;border:1px solid #74694e;border-radius:5px;font:13px/1.35 system-ui;pointer-events:auto';
  const partyHead = document.createElement('div');
  partyHead.style.cssText = 'display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:6px';
  const partyTitle = document.createElement('strong'); partyTitle.textContent = 'Party inventory'; partyTitle.style.cssText = 'color:#e4bd63;letter-spacing:0.12em;text-transform:uppercase;font-size:12px';
  const partyClose = button('×', () => { partyPanel.hidden = true; });
  partyClose.setAttribute('aria-label', 'Close party inventory');
  partyHead.append(partyTitle, partyClose);
  const partyStatus = document.createElement('output');
  partyStatus.dataset.partyFeedback = 'true';
  partyStatus.setAttribute('role', 'status');
  partyStatus.style.cssText = 'display:block;min-height:1.4em;margin:0 0 6px;color:#ead27e';
  const memberRow = document.createElement('div');
  memberRow.style.cssText = 'display:flex;align-items:center;gap:6px;margin-bottom:6px';
  const memberPrev = button('◀', () => cycleMember(-1));
  memberPrev.setAttribute('aria-label', 'Previous party member');
  const memberNext = button('▶', () => cycleMember(1));
  memberNext.setAttribute('aria-label', 'Next party member');
  const memberName = document.createElement('strong');
  memberName.dataset.partyMember = 'true';
  memberName.style.cssText = 'flex:1;text-align:center';
  memberRow.append(memberPrev, memberName, memberNext);
  const memberStats = document.createElement('div');
  memberStats.dataset.partyStats = 'true';
  memberStats.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:2px 10px;margin-bottom:8px;color:#c9c0ae;font-size:12px';
  const equipmentList = document.createElement('div');
  equipmentList.dataset.partyEquipment = 'true';
  equipmentList.setAttribute('role', 'group');
  equipmentList.setAttribute('aria-label', 'Equipped gear. Drop items here to equip, drag worn gear to the grid.');
  equipmentList.style.cssText = 'display:flex;gap:8px;margin-bottom:8px';
  const gridStatus = document.createElement('output');
  gridStatus.dataset.partyLoad = 'true';
  gridStatus.style.cssText = 'display:block;margin-bottom:4px;color:#c9c0ae;font-size:12px';
  const partyGrid = document.createElement('div');
  partyGrid.dataset.partyGrid = 'true';
  partyGrid.setAttribute('role', 'group');
  partyGrid.setAttribute('aria-label', 'Party inventory grid. Drag items between cells, or onto equipment above.');
  partyGrid.style.cssText = 'display:grid;grid-template-columns:repeat(6,1fr);gap:4px';
  partyPanel.append(partyHead, partyStatus, memberRow, memberStats, equipmentList, gridStatus, partyGrid);
  root.append(partyPanel);
  const rosterOrder = (): string[] => entries(state.party)
    .map(([id, member]) => ({ id, rank: numeric(record(member).rank) }))
    .sort((left, right) => left.rank - right.rank || left.id.localeCompare(right.id))
    .map(member => member.id);
  const cycleMember = (direction: -1 | 1): void => {
    const order = rosterOrder();
    if (order.length === 0) return;
    const current = order.indexOf(text(state.selectedMember, ''));
    const next = order[(current < 0 ? 0 : current + direction + order.length) % order.length];
    command('select', { member: next });
  };
  const partyOwner = (): Record<string, unknown> => record(record(inventoryOf(state).owners).party);
  const gridCellButton = (label: string): HTMLButtonElement => {
    const cell = button(label, () => {});
    cell.style.cssText = 'position:relative;height:44px;width:100%;padding:0;background:#10120f99;border:1px solid #574f3d;border-radius:3px;cursor:pointer;font:700 13px/1 system-ui;color:#f0e6d2;display:flex;align-items:center;justify-content:center;overflow:hidden';
    return cell;
  };
  const gridIcon = (item: Record<string, unknown>, size: number): HTMLElement | null => {
    const imageIndex = numeric(item.imageIndex, -1);
    if (typeof item.image !== 'string' || item.image.length === 0 || imageIndex < 0 || imageIndex >= 8) return null;
    const icon = document.createElement('span');
    icon.dataset.partyIcon = 'true';
    icon.setAttribute('aria-hidden', 'true');
    const column = imageIndex % 4, row = Math.floor(imageIndex / 4);
    icon.style.cssText = `background-image:url("${String(item.image).replaceAll('"', '%22')}");background-position:${(column * 100) / 3}% ${row * 100}%;background-repeat:no-repeat;background-size:400% 200%;display:block;height:${size}px;width:${size}px;pointer-events:none`;
    return icon;
  };
  // Escape menu: the game-UI front door. Centered button list; opening it
  // pauses a live expedition, closing via Resume restores only a
  // menu-caused pause. Legacy agent panels stay in the DOM behind the menu
  // toggle so existing dataset hooks and exercised commands keep working.
  const menu = document.createElement('div');
  menu.dataset.gameMenu = 'true';
  menu.hidden = true;
  menu.setAttribute('role', 'dialog');
  menu.setAttribute('aria-label', 'Game menu');
  menu.dataset.rustyUiInteractive = 'true';
  menu.style.cssText = 'box-sizing:border-box;position:fixed;left:50%;top:50%;transform:translate(-50%,-50%);z-index:5;width:min(360px,calc(100vw - 48px));max-height:calc(100vh - 48px);overflow:auto;padding:14px 16px;color:#eee6d5;background:#171914f5;border:1px solid #74694e;border-radius:6px;font:13px/1.4 system-ui;pointer-events:auto;box-shadow:0 12px 48px #000000cc';
  const menuHead = document.createElement('div');
  menuHead.style.cssText = 'display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:8px';
  const legacyToggle = document.createElement('button'); legacyToggle.type = 'button'; legacyToggle.textContent = 'Show legacy panels'; legacyToggle.dataset.legacyToggle = 'true'; legacyToggle.dataset.rustyUiInteractive = 'true'; legacyToggle.setAttribute('aria-expanded', 'false');
  legacyToggle.style.cssText = 'background:none;color:#c9c0ae;border:1px solid #574f3d;border-radius:3px;padding:3px 6px;cursor:pointer;font:11px/1.35 system-ui';
  const menuTitle = document.createElement('strong'); menuTitle.textContent = 'Menu'; menuTitle.style.cssText = 'color:#e4bd63;letter-spacing:0.12em;text-transform:uppercase;font-size:12px';
  const menuHeadSpacer = document.createElement('span'); menuHeadSpacer.style.cssText = 'width:40px';
  menuHead.append(legacyToggle, menuTitle, menuHeadSpacer);
  // Host-input guard (see F4): stop gameplay keys here so the Engine host
  // never sees them, but do NOT preventDefault — Space/Enter must still
  // activate the button through the default action.
  const stopToggleKeys = (event: KeyboardEvent): void => { if (!gameplayKeys.has(event.code)) return; event.stopPropagation(); };
  legacyToggle.addEventListener('keydown', stopToggleKeys, true);
  legacyToggle.addEventListener('keyup', stopToggleKeys, true);
  partyPanel.addEventListener('keydown', stopToggleKeys, true);
  partyPanel.addEventListener('keyup', stopToggleKeys, true);
  const setLegacyVisible = (visible: boolean): void => {
    panel.hidden = !visible; inventory.hidden = !visible; runPanel.element.hidden = !visible;
    inventory.style.bottom = visible ? '190px' : '12px';
    legacyToggle.textContent = visible ? 'Hide legacy panels' : 'Show legacy panels';
    legacyToggle.setAttribute('aria-expanded', String(visible));
  };
  legacyToggle.addEventListener('click', () => setLegacyVisible(panel.hidden));
  const menuStatus = document.createElement('output');
  menuStatus.dataset.menuFeedback = 'true';
  menuStatus.setAttribute('role', 'status');
  menuStatus.style.cssText = 'display:block;min-height:1.4em;margin:0 0 8px;color:#ead27e;text-align:center';
  const menuList = document.createElement('div');
  menuList.style.cssText = 'display:grid;gap:6px';
  const menuButton = (label: string, action: () => void): HTMLButtonElement => {
    const element = button(label, action);
    element.style.textAlign = 'center';
    element.style.padding = '7px 6px';
    menuList.append(element);
    return element;
  };
  const readmeView = document.createElement('div');
  readmeView.hidden = true;
  // NOTE: do not put display:grid in cssText — inline display overrides the
  // hidden attribute (UA display:none loses), so the view would always show.
  // Visibility is driven by style.display in the toggle handlers below.
  readmeView.style.cssText = 'gap:8px';
  readmeView.style.display = 'none';
  const readmeTitle = document.createElement('strong'); readmeTitle.textContent = 'Field guide'; readmeTitle.style.cssText = 'text-align:center;color:#e4bd63';
  const readmeBody = document.createElement('div');
  readmeBody.style.cssText = 'display:grid;gap:6px;color:#e7dcc4;font-size:12px;white-space:pre-line';
  const readmeSection = (heading: string, body: string): void => {
    const section = document.createElement('section');
    const title = document.createElement('strong'); title.textContent = heading; title.style.cssText = 'display:block;color:#e4bd63;margin-bottom:2px';
    const text = document.createElement('span'); text.textContent = body;
    section.append(title, text);
    readmeBody.append(section);
  };
  readmeSection('Controls', 'W/S step · A/D sidestep · Q/E turn · Space fire · V melee · B fix bayonets · N unfix bayonets · C charge · R reload (also automatic) · F use · T cycle interactable · P pause · K save · L load · Esc or the Menu button for this menu.');
  readmeSection('Formation', 'The left panel shows a 3×3 formation with the commander fixed in the center. The chevron marks each member\u2019s facing. Select any member for inventory or ally targeting; Change formation pauses into a larger planner. Execute resumes a timed repositioning order, locking movement and affected soldiers; Cancel discards the draft.');
  readmeSection('Inventory', 'Open Inventory from the bottom hotbar or this menu: the current member\u2019s equipment on top (cycle members with the arrows), the shared grid below. Drag items between grid cells, onto equipment to equip (swapping what is worn), or drag worn gear back to unequip. Clicking works too: select, then click the destination. Loot the world through the legacy panels for now.');
  readmeSection('Menu', 'Esc or the Menu button pauses and opens this menu. Resume returns to the expedition. Rest needs a safe spot; save, load and restart run here. Legacy panels are the older debug views, kept for troubleshooting.');
  readmeView.append(readmeTitle, readmeBody, button('Back', () => { readmeView.hidden = true; readmeView.style.display = 'none'; menuList.hidden = false; menuList.style.display = 'grid'; }));
  (readmeView.lastChild as HTMLElement).style.textAlign = 'center';
  menu.append(menuHead, menuStatus, menuList, readmeView);
  // Gameplay-key guard like every other panel (F7): stop propagation so the
  // Engine host never sees menu keypresses, without preventDefault so
  // Space/Enter still activate focused buttons. Escape is not a gameplay
  // key and passes through to the window menuEscape handler.
  menu.addEventListener('keydown', stopToggleKeys, true);
  menu.addEventListener('keyup', stopToggleKeys, true);
  root.append(menu);
  const resumeButton = menuButton('Resume', () => closeMenu(true));
  let menuOpen = false;
  // Entry-state snapshot, not a write-once flag: any pause flip while the
  // menu is open (menu Pause button, Load restoring paused state, P key)
  // must be honored at close, so close compares live state to entry state
  // instead of trusting a flag set at open. See F1/F2.
  let menuEntryPaused = false;
  // Detour carry: Inventory/Formation close the menu to reveal legacy panels
  // while the menu-caused pause is still outstanding. The next open would
  // re-snapshot "paused" and disarm Resume, so the detour carries the
  // entered-live bit across one reopen. Consumed on open; close still
  // compares live state, so intervening flips stay honored.
  let menuDetourLive = false;
  let menuReturnFocus: Element | null = null;
  const closeMenu = (resume: boolean): void => {
    if (!menuOpen) return;
    menuOpen = false;
    menu.hidden = true;
    readmeView.hidden = true;
    readmeView.style.display = 'none';
    menuList.hidden = false;
    menuList.style.display = 'grid';
    // Resume only a menu-observed live game: entered live and still paused.
    // Entered-paused games are never resumed; entered-live games resume iff
    // still paused at close, whatever paused them (menu, Load, P key).
    if (resume && !menuEntryPaused && numeric(state.paused) === 1) command('pause');
    if (menuReturnFocus instanceof HTMLElement && document.contains(menuReturnFocus)) menuReturnFocus.focus();
  };
  const openMenu = (): void => {
    if (menuOpen) return;
    menuOpen = true;
    menuEntryPaused = numeric(state.paused) === 1 && !menuDetourLive;
    menuDetourLive = false;
    menuReturnFocus = document.activeElement instanceof Element ? document.activeElement : null;
    menu.hidden = false;
    // Gate the toggle on actual live state, not the entry flag: a detour
    // carry can force entry-live while the game is already paused, and
    // pause is a toggle — firing here would unpause under the open menu.
    if (numeric(state.paused) !== 1) command('pause');
    resumeButton.focus();
  };
  menuButton('Readme', () => { menuList.hidden = true; menuList.style.display = 'none'; readmeView.hidden = false; readmeView.style.display = 'grid'; });
  menuButton('Inventory & equipment', () => { menuDetourLive = !menuEntryPaused; partyPanel.hidden = false; closeMenu(false); });
  menuButton('Formation & party', () => { menuDetourLive = !menuEntryPaused; setLegacyVisible(true); partyTools.open = true; closeMenu(false); });
  const menuPause = menuButton('Pause', () => command('pause'));
  menuButton('Rest', () => command('rest'));
  menuButton('Save', () => command('save'));
  menuButton('Load', () => command('load'));
  menuButton('Restart', () => { command('restart'); closeMenu(false); });
  const menuEscape = (event: KeyboardEvent): void => {
    if (event.code !== 'Escape') return;
    if (numeric(record(state.formation).open) === 1) { event.preventDefault(); event.stopPropagation(); command('formation-cancel'); return; }
    // Never steal Escape from the Engine debug console (its bubble-phase
    // isolation runs after this window-capture listener) or from editable
    // fields. Menu-focus Escape still toggles: the menu holds no inputs.
    if (event.target instanceof Node) {
      if (event.target instanceof HTMLElement && event.target.closest('input,textarea,select,[contenteditable="true"],#rifles-debug-console,[data-ability-menu]')) return;
      if (document.querySelector('[aria-label="Debug tools"]')?.contains(event.target)) return;
    }
    event.preventDefault();
    event.stopPropagation();
    if (menu.hidden) openMenu(); else closeMenu(true);
  };
  window.addEventListener('keydown', menuEscape, true);
  // Non-Escape opener (pointer-lock and remapped keyboards may never deliver
  // Esc): small fixed Menu button where the legacy toggle used to live. See F6.
  const menuButtonTop = document.createElement('button');
  menuButtonTop.type = 'button';
  menuButtonTop.textContent = 'Menu';
  menuButtonTop.title = 'Open the game menu (Esc)';
  menuButtonTop.setAttribute('aria-label', 'Open the game menu');
  menuButtonTop.dataset.rustyUiInteractive = 'true';
  menuButtonTop.style.cssText = 'position:fixed;left:12px;top:12px;z-index:3;background:#33392f;color:#eee6d5;border:1px solid #827556;border-radius:3px;padding:4px 6px;cursor:pointer;font:12px/1.35 system-ui';
  menuButtonTop.addEventListener('keydown', stopToggleKeys, true);
  menuButtonTop.addEventListener('keyup', stopToggleKeys, true);
  menuButtonTop.addEventListener('click', openMenu);
  root.append(menuButtonTop);
  setLegacyVisible(false);
  render(context.projection?.current() ?? null); const unsubscribe = context.projection?.subscribe(render) ?? (() => {});
  return { dispose() { runPanel.dispose(); bottomBar.dispose(); formationPlanner.dispose(); abilityMenu.dispose(); debugTools.dispose(); uiProfile.dispose(); unsubscribe(); inventory.removeEventListener('toggle', syncPanelWidth); partyTools.removeEventListener('toggle', syncPanelWidth); magicPanel.removeEventListener('toggle', syncPanelWidth); panel.removeEventListener('focusout', focusOutside); window.removeEventListener('blur', cancelDrag); window.removeEventListener('keydown', escape, true); window.removeEventListener('keydown', menuEscape, true); document.removeEventListener('pointerdown', outside, true); legacyToggle.removeEventListener('keydown', stopToggleKeys, true); legacyToggle.removeEventListener('keyup', stopToggleKeys, true); menu.removeEventListener('keydown', stopToggleKeys, true); menu.removeEventListener('keyup', stopToggleKeys, true); menuButtonTop.removeEventListener('keydown', stopToggleKeys, true); menuButtonTop.removeEventListener('keyup', stopToggleKeys, true); menuButtonTop.remove(); partyPanel.removeEventListener('keydown', stopToggleKeys, true); partyPanel.removeEventListener('keyup', stopToggleKeys, true); menu.remove(); partyPanel.remove(); inventory.remove(); panel.remove(); } };
}
