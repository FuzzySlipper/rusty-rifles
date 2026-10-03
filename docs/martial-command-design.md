# Martial command contracts

These contracts describe the party, equipment and combat rules.
[Gameplay and ownership](gameplay-design.md) maps them to their code owners.
Authored definitions under `content/` own tunable values.

## Product contract

One party occupies one dungeon cell and steps/rotates as a group. Charge is a special maneuver, never a replacement movement mode. The patrol uses a 3×3 internal formation with a fixed central commander and six soldiers. The enabled company experiment uses a 5×5 formation with 24 soldiers around the same fixed center; it still occupies one world cell. The commander cannot attack or be repositioned. Commander death ends the run even with surviving soldiers. Loss of all soldiers alone does not end the run; retreat remains possible.

Controls are blunt forward-facing orders: Fire, Melee, Fix bayonets, Unfix bayonets, and a unique ability list. There is no player-selected enemy for combat. Rotation changes forward. Feature focus and inventory/ally selection remain separate concerns.

## Forward attacks and formation defense

Formation positions influence protection and melee reach; they are not navigable world subcells. Offensive lanes are targeting preferences, not compulsory parallel firing tracks. Eligible soldiers prefer exposed targets in their own forward lane, then the nearest permitted adjacent lane, then nearest exposed enemy within the lane. Stable identity breaks remaining ties. Three musketeers facing three lanes should distribute naturally; facing one valid enemy they can concentrate fire.

Use actual world positions/crowd offsets and Engine spatial queries for obstruction. Shots stop at the first hostile body or world obstruction; friendly soldiers do not block outgoing fire and there is no friendly damage. World-feature targeting remains separate from combat orders.

Choose a target at action start and recheck at effect time. A lost/invalid target may be replaced within the original forward attack area; later rotation must not swing a committed attack into a new direction. Weapon reach still limits replacement. No valid participant means no action or cooldown expenditure and a useful reason.

Defensive screening is distinct from offensive target selection. Incoming attacks use front/right/rear/left sectors and authored lane coverage. Near-side soldiers screen before the commander; soldiers beyond the commander do not screen; corner positions cover their adjoining sectors. The outermost matching rank or file screens first. One living matching soldier receives an ordinary hit, with stable member identity resolving equal-depth ties and no excess-damage spill-through. An uncovered approach exposes the commander. The selected formation definition owns coverage and lane thresholds (`formation.json` for the patrol, `company-formation.json` for the experiment); the examples below illustrate the rule without introducing physical soldier collision.

## Orders and timing

Orders fan out to eligible soldiers; unready soldiers skip rather than queue a surprise attack. Each participant owns windup, effect, recovery and relevant cooldowns. An order is not a shared uniform cooldown. The ability menu groups by stable ability ID, displays eligible/total users and readiness, and starts all eligible owners. Party maneuvers such as Charge run once and coordinate their contributors, never separate movement operations or duplicate party buffs per soldier. Distinguish effect scope from the number of ability providers.

A short useful report explains who acted and why others did not. C# provides availability and execution checks from the same rules; TypeScript displays them. Existing packaged diagnostics should report target lane/fallback, obstruction, reach, readiness, screening recipient and commander exposure.

## Equipment and muskets

Use weapon, outfit and accessory slots. Sword-and-shield is one weapon loadout. The martial loadouts are musket, short sword/shield, sword and pike.

Typed weapon definitions author attacks, range/coverage, melee reach, charge eligibility, timings and bayonet modifiers. Short swords attack directly ahead; swords also cover adjacent frontage; pikes/fixed bayonets support authored longer reach past friendly positions. Fixed bayonets enable musket melee/charge, reduce accuracy and lengthen reload. Accuracy must have a real, tunable combat effect and legible outcomes; do not add an inert percentage field.

Ordinary ammunition is unlimited. Each musket retains loaded/unloaded state and automatic timed reload without an ammunition inventory prerequisite. Compatible muskets include their bayonet; it is not a separate inventory item or generic attachment system.

After firing recovery, automatic reload starts when the soldier is able. Melee, bayonet orders and charge can interrupt unfinished reload; discard unfinished reload progress. Fix/unfix takes authored time; changed state takes effect on completion. Fixing after an interrupted reload leaves the gun unloaded. Weapon identity carries load and bayonet state through transfer, drop, save and travel; inactive/dropped weapons do not reload themselves. Equipment changes must not erase soldier recovery/cooldowns or duplicate a round.

## Formation planning and execution

The persistent formation UI is status, not a small live drag surface. Change formation opens a large paused planner containing a draft. The commander stays fixed; valid soldier positions are unique. Cancel discards the draft and restores the prior pause/play state. Execute with a changed valid draft resumes simulation and begins one timed transition. A no-op execute must not impose a cost.

During execution the party cannot step or rotate and affected soldiers cannot act; unchanged soldiers can fight. Use one authored transition duration, with old defensive positions until all planned positions commit together. Casualties remain casualties; no automatic gap fill. Show progress and destination clearly. After completion movement resumes. There is no extra post-transition cooldown or defense debuff: the time, movement lock and action loss are the cost. Do not allow reopening the planner to replace or bypass an executing transition.

Resolve interruption of affected soldiers' pending actions explicitly using existing commit/recovery rules; never erase already incurred recovery. Persist execution, while an unexecuted UI draft is not authoritative saved formation state. Existing save/pause behavior should have an explicit, coherent planner interaction.

## Charge

A forward-only explicit maneuver with authored maximum distance, step timing, commitment/recovery and melee bonus. Require at least one eligible charge-capable soldier and a reachable stopping cell in melee range. No corner routing, steering, teleporting or replacing normal movement.

Check approach and occupancy initially, then recheck at each admitted timed step using the existing movement/reservation owner and Engine mechanisms. During charge lock manual movement/rotation and incompatible attacks. Stop at the last legal cell when blocked. A departed target is not hit remotely; contact and actual weapon reach decide damage. At contact apply one bonus melee attack per surviving valid contributor, not per menu provider. Define and test cooldown/cost behavior for pre-start rejection, interruption after commitment and contact. Commander can be hurt during charge. Save/restore must not replay impact.

## Ownership and tuning

Extend PartyState/character entities for commander and formation state; RiflesCombat and concrete neighboring combat owners for orders, individual actions and maneuver coordination; ItemInventory plus a small concrete weapon-state owner for item state; existing ExplorationState/MovementGrid for timed movement. Stats/effects/tracks, lifecycle, admitted time, input, rendering, spatial queries, resources and persistence primitives remain Engine-owned. Keep TypeScript a DOM projection. Identify a narrow upstream gap if a packaged mechanism is missing; no downstream replacement.

Tunable choices live under content/ as typed validated local definitions: layout/coverage, weapon reach/aim, targeting preferences, action/reload/formation/charge timings, accuracy/bayonet modifiers, cooldowns and starter encounters. Invalid definitions give actionable errors. No silent defaults, general rules DSL, event bus or reusable blobber framework. Readability and later adjustment matter more than extensibility for hypothetical games.

Current-schema persistence includes committed gameplay state; development saves have no historical migration guarantee. Cooldowns advance on admitted simulation time, not wall time; pause, travel and reload must not grant free readiness.

## Screening and reach examples

The screening model classifies the source relative to the party's facing
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

Admitted content owns the exact thresholds. Formation positions remain
internal coverage and reach rules, not independently navigable world cells.

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

## Charge commitment policy

An inadmissible charge changes neither actions nor readiness. The first admitted
forward reservation commits the maneuver. Only unfinished reloads may be
interrupted to join it. Participating soldiers retain their weapon identities;
changing equipment cannot substitute a fresh contributor. Contact or interruption
after commitment incurs the authored charge recovery plus each contributor's
weapon recovery. The party still receives attacks during travel. Contact uses
current positions and reach, and ends the saved maneuver before resolving damage.

Fresh expeditions start paused by the authored run setting, allowing the player
to inspect the formation and orders before resuming. This also prevents combat
from advancing while the standalone host is waiting for its first browser.
