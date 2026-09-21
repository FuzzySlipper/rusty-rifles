# Martial command campaign

Accepted planning direction — 2026-09-20. This document records the conversation's next implementation layer, not implemented behavior. Den owns live status and dependencies; [campaign index](campaign/martial-command-index.md) records the initial plan.

## Product contract

One party occupies one dungeon cell and steps/rotates as a group. Charge is a special maneuver, never a replacement movement mode. A 3×3 internal formation has a fixed central commander and six soldiers in eight surrounding positions. The commander cannot attack or be repositioned. Commander death ends the run even with surviving soldiers. Loss of all soldiers alone does not yet end the run; retreat remains possible. No drummer or new sound work belongs to this campaign.

Controls are blunt forward-facing orders: Fire, Melee, Fix bayonets, Unfix bayonets, and a unique ability list. There is no player-selected enemy for combat. Rotation changes forward. Feature focus and inventory/ally selection remain separate concerns.

## Forward attacks and formation defense

Formation positions influence protection and melee reach; they are not navigable world subcells. Offensive lanes are targeting preferences, not compulsory parallel firing tracks. Eligible soldiers prefer exposed targets in their own forward lane, then the nearest permitted adjacent lane, then nearest exposed enemy within the lane. Stable identity breaks remaining ties. Three musketeers facing three lanes should distribute naturally; facing one valid enemy they can concentrate fire.

Use actual world positions/crowd offsets and Engine spatial queries for obstruction. Shots stop at the first hostile body or world obstruction; friendly soldiers do not block outgoing fire and there is no friendly damage in this combat slice. Do not alter unrelated world-feature targeting to remove combat selection.

Choose a target at action start and recheck at effect time. A lost/invalid target may be replaced within the original forward attack area; later rotation must not swing a committed attack into a new direction. Weapon reach still limits replacement. No valid participant means no action or cooldown expenditure and a useful reason. Advanced damage prediction, reservation of kills, and automatic overkill avoidance are deferred.

Defensive screening is distinct from offensive target selection. Prototype front/right/rear/left approach sectors with explicit protection by occupied positions. Near-side soldiers screen before the commander; soldiers beyond the commander do not screen; corners may cover both adjoining sectors. Ordinary hits damage one recipient with no excess-damage spill-through. The exact edge attack distribution and gap rule are intentionally provisional: task M01 must illustrate front-center loss with both corners alive, a single surviving flank guard, a rear attack, and rotations. Choose one deterministic rule that makes gaps and coverage understandable, then implement and tune it. Do not silently treat any surviving soldier as universal protection or demand exact per-soldier collision.

## Orders and timing

Orders fan out to eligible soldiers; unready soldiers skip rather than queue a surprise attack. Each participant owns windup, effect, recovery and relevant cooldowns. An order is not a shared uniform cooldown. The ability menu groups by stable ability ID, displays eligible/total users and readiness, and starts all eligible owners. Party maneuvers such as Charge run once and coordinate their contributors, never six separate movement operations or six duplicate party buffs. Distinguish effect scope from the number of ability providers.

A short useful report explains who acted and why others did not. C# provides availability and execution checks from the same rules; TypeScript displays them. Existing packaged diagnostics should report target lane/fallback, obstruction, reach, readiness, screening recipient and commander exposure.

## Equipment and muskets

Use weapon, outfit and accessory slots. Sword-and-shield is one weapon loadout. Support the required martial set: musket, short sword/shield, sword and pike; do not expand into bows, crossbows or a generic ranged arsenal.

Typed weapon definitions author attacks, range/coverage, melee reach, charge eligibility, timings and bayonet modifiers. Short swords attack directly ahead; swords also cover adjacent frontage; pikes/fixed bayonets support authored longer reach past friendly positions. Fixed bayonets enable musket melee/charge, reduce accuracy and lengthen reload. Accuracy must have a real, tunable combat effect and legible outcomes; do not add an inert percentage field.

Ordinary ammunition is unlimited. Preserve loaded/unloaded state and automatic timed reload on individual muskets; remove ordinary player-ammo quantities, supply clutter and reload prerequisites from the active gameplay path. Special party ammo types are deferred. Compatible muskets include their bayonet for this slice; no separate bayonet inventory or generic attachment system.

After firing recovery, automatic reload starts when the soldier is able. Melee, bayonet orders and charge can interrupt unfinished reload; initially discard unfinished reload progress. Fix/unfix takes authored time; changed state takes effect on completion. Fixing after an interrupted reload leaves the gun unloaded. Weapon identity carries load and bayonet state through transfer, drop, save and travel; inactive/dropped weapons do not reload themselves. Equipment changes must not erase soldier recovery/cooldowns or duplicate a round.

## Formation planning and execution

The persistent formation UI is status, not a small live drag surface. Change formation opens a large paused planner containing a draft. The commander stays fixed; valid soldier positions are unique. Cancel discards the draft and restores the prior pause/play state. Execute with a changed valid draft resumes simulation and begins one timed transition. A no-op execute must not impose a cost.

During execution the party cannot step or rotate and affected soldiers cannot act; unchanged soldiers can fight. Use one authored transition duration initially, with old defensive positions until all planned positions commit together. Casualties remain casualties; no automatic gap fill. Show progress and destination clearly. After completion movement resumes. No extra post-transition cooldown or defense debuff initially: the time, movement lock and action loss are the cost. Do not allow reopening the planner to replace or bypass an executing transition.

Resolve interruption of affected soldiers' pending actions explicitly using existing commit/recovery rules; never erase already incurred recovery. Persist execution, while an unexecuted UI draft is not authoritative saved formation state. Existing save/pause behavior should have an explicit, coherent planner interaction.

## Charge

A forward-only explicit maneuver with authored maximum distance, step timing, commitment/recovery and melee bonus. Require at least one eligible charge-capable soldier and a reachable stopping cell in melee range. No corner routing, steering, teleporting or replacing normal movement.

Check approach and occupancy initially, then recheck at each admitted timed step using the existing movement/reservation owner and Engine mechanisms. During charge lock manual movement/rotation and incompatible attacks. Stop at the last legal cell when blocked. A departed target is not hit remotely; contact and actual weapon reach decide damage. At contact apply one bonus melee attack per surviving valid contributor, not per menu provider. Define and test cooldown/cost behavior for pre-start rejection, interruption after commitment and contact. Commander can be hurt during charge. Save/restore must not replay impact.

## Ownership and tuning

Extend PartyState/character entities for commander and formation state; RiflesCombat and concrete neighboring combat owners for orders, individual actions and maneuver coordination; ItemInventory plus a small concrete weapon-state owner for item state; existing ExplorationState/MovementGrid for timed movement. Stats/effects/tracks, lifecycle, admitted time, input, rendering, spatial queries, resources and persistence primitives remain Engine-owned. Keep TypeScript a DOM projection. Identify a narrow upstream gap if a packaged mechanism is missing; no downstream replacement.

Tunable choices live under content/ as typed validated local definitions: layout/coverage, weapon reach/aim, targeting preferences, action/reload/formation/charge timings, accuracy/bayonet modifiers, cooldowns and starter encounters. Invalid definitions give actionable errors. No silent defaults, general rules DSL, event bus or reusable blobber framework. Readability and later adjustment matter more than extensibility for hypothetical games.

Each task includes its real content, UI/projection, and current-schema persistence changes where relevant. Development saves may break; historical migration is not required. Do not postpone all save integration to the final task. Cooldowns advance on admitted simulation time, not wall time; pause, travel and reload must not grant free readiness.

## Bounded scope and acceptance

Retain exploration, generated floors, feature interactions, inventories, progression and whole-run travel/saves. Adapt existing spell/ability callers to the command model as needed; do not redesign the entire magic catalogue or force targeted support effects into enemy-selection UI. Explicit ally selection can remain where meaningful. Enemy behavior should exercise frontal pressure, flanks and commander exposure without becoming a new AI campaign.

Deferred: drummer/sounds, morale/suppression, multi-cell formations, independently navigating soldiers, physical formation-transition paths, layered penetration, finite basic ammo/special ammo types, separate bayonet items, ranged-weapon variety, last-soldier automatic defeat, advanced target prediction, new art campaign.

Validate with focused rule/state tests plus the required repository check lane (pnpm install --frozen-lockfile once, then bash scripts/check.sh). Use an existing broker session for visible acceptance. Report source/build checks, runtime launch and visible interaction separately. Demonstrate three-lane volley and fallback concentration, weapon-specific melee reach, interruptible reload/bayonets, a casualty exposing the commander, planner cancel/execute under pressure, charge contact/blockage, per-soldier ability readiness, commander defeat with living soldiers, and save/resume/travel without timing resets or duplicated effects.

Finish with bounded tuning from visible play, recording what felt wrong, authored adjustments and remaining limitations. This is an iteration-ready martial layer, not certification of the aspirational brief.

## M01 prototype examples: screening and reach

The initial screening model classifies the source relative to the party's facing
into a cardinal approach sector and one of three lateral lanes. Sector selection
uses the dominant axis; exact diagonals choose front/rear consistently. Lane
boundaries are authored angular ratios, so a tightly grouped distant crowd may
fall in the center lane. This is a tuning choice, not physical soldier collision.
World queries still determine whether the attack reaches the party at all.

Read this formation facing upward; C is the fixed commander, dots are empty
positions, and letters identify living soldiers rather than character classes:

```text
Intact front       Center casualty     One left guard
 A B D               A . D               . . .
 . C .               . C .               G C .
 E F H               E F H               . . .
```

- An intact front screens left, center and right frontal approaches with A, B
  and D respectively. One ordinary hit has one recipient; lethal excess does
  not spill through to the commander.
- With B gone, a central frontal attack reaches C. A and D retain their own
  frontal coverage, but neither becomes a universal replacement for B.
- G screens the center lane of a left-side approach. G does not protect against
  a frontal or right-side attack simply because G is alive.
- In the intact example, a central rear approach meets F. If F is absent, B
  cannot protect C from behind: B lies beyond the commander on that approach.
- Corner soldiers cover their corresponding corner lanes on both adjoining
  sectors. They do not cover the middle lane of either sector by default.
- Rotating the whole party rotates every coverage assignment. Turning right
  presents the original front to an attacker to the world's right; the same
  relative attack has the same recipient after both positions are rotated.
- If the relevant screening position has no living soldier, the commander is
  exposed regardless of living soldiers on other sides. Fallen soldiers do
  not absorb attacks.

Outgoing preference is separate. Soldiers first need a legal weapon reach and
an exposed enemy in the committed forward area. A short sword has direct-lane
front-rank access; a sword can cover neighboring frontage; pikes and fixed
bayonets can reach from farther back past friendly positions. Muskets can aim
across the permitted frontage. Preference selects own lane before the nearest
permitted alternative, then nearest enemy, with stable identity resolving ties.
An empty preferred lane never authorizes a short sword to exceed its reach.

These examples establish the prototype's expected behavior; admitted content
and focused checks implement the exact thresholds. Runtime commander and combat
integration follow in M02/M04. Visible tuning in M09 may change coverage without
turning the formation into independently navigable world cells.

## Commander integration policy

The commander is an explicit authored character role, not an instance-name test.
Actual expedition presets and restores require one commander at the reserved
center. Generic small party fixtures remain usable for domain checks. Defeat is
commander death; living soldiers cannot restore movement or casting after it.
Soldiers may retreat with the living commander after all other soldiers fall.

Directional weapon hits and hostile magic use the incoming approach and current
screening positions. Directionless hostile magic reaches the commander. Existing
whole-party environmental hazards damage each living member, including the
commander; ongoing conditions stay on their actual recipient rather than being
screened again on every tick. A fallen commander cannot be revived. The commander
has no starting spells, direct actions or advancement choices. Formation status
projects the role and exposed approaches from C#.

## Shared ability settlement

A shared party-effect order starts every eligible provider's own cast, resource
cost and recovery. The ready provider with the lowest stable member ID owns the
single shared effect; the other casts are supporting contributions. That role
is saved with each committed action. If the effect owner falls or is interrupted
before impact, the shared effect fails rather than silently transferring to
another contributor. Already incurred recovery remains. Individual hostile and
ally effects still resolve once per eligible provider.
