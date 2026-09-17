# Spells and party development

The four characters now have individual known spells, selected spells and three
assignable quick slots. Open **Spells & recovery**, select a character and spell,
then choose an ally when appropriate and Cast. Selecting, cancelling selection,
and assigning slots are free. The existing Interrupt control cancels an action's
windup; committed actions must finish recovery. A quick slot selects its spell,
so it cannot accidentally spend resource while preparing a target.

`content/definitions/spells.json` contains the catalogue and balance:

| Spell | Behavior |
| --- | --- |
| Spark | Single-target projectile damage |
| Burst | Projectile impact followed by an occluded radius attack |
| Ward | Timed ally defense bonus |
| Blight | Periodic injury, including enemy casting |
| Bind | Slows movement and action progress |
| Mend | Restores a living ally's vitality |
| Cleanse | Removes harmful ally conditions |
| Lantern | A dynamically lit Engine point light following the party |
| Reveal | Reveals nearby visible inventory counts and the gate mechanism clue |
| Hand | Activates a reachable, permitted lever through its existing interaction policy |
| Revive | Restores limited vitality to a fallen ally, spending a cordial and a limited revival |

Burst is learned through advancement. Spellbooks, costs, timings, projectile
speed/range, areas, status durations/periods, resistance profiles, rest/revival
requirements, rewards and advancement benefits are authored. This is a concrete
game catalogue, without a rune language, skill-tree framework or reusable kit.

## Shared action and world rules

Player and enemy casts use the same windup/commit/recovery state as rifle actions.
Costs settle once at commit; cancellation before commit is free. Failure to meet
a revalidated ally, resource or feature requirement grants no effect. A projectile
locks its aim at preparation and launches from its caster's logical source cell
and crowd anchor at commit. It does not home onto a moving target. Engine traces
resolve the first wall, closed gate or body. Area impacts check each victim's
radius and masonry/furniture occlusion independently. The existing authored
friendly-fire policy applies to both bullets and offensive magic.

The old prototype Bolt action is replaced by the spell catalogue; thrown items
retain their existing physical-flight path.

The marksman has an authored Blight assignment and an Engine resource track.
Once that resource is exhausted it continues its ordinary rifle/melee policy.
Body placement, crowd capacity, movement admission and pathfinding remain shared
with the existing encounter.

Engine `EffectsComponent` enforces one refreshable instance of each condition per
target. Refresh renews duration without postponing an injury's next tick. Rifles
advances duration and periodic consequences only on admitted simulation time.
Engine evaluates defense and speed contributions; slow changes movement/action
progress without slowing the expedition clock or condition expiry. Authored
resistance reduces offensive spell damage and harmful duration, with full
resistance preventing a harmful condition. Expiry, cleansing, death and completed
rest remove the appropriate effects; equipment recomputation retains independent
advancement and condition bonuses.

Hand never supplies a key or pressure-plate weight. The lever must be reachable,
visible, permitted by content and still at its captured revision. Reveal exposes
nearby facts within its authored radius and sight limits; it does not open doors,
consume items or bypass the puzzle. Lantern uses the existing dynamic lighting
path and existing illustrated sprites, with no new texture or normal-map pipeline.

## Recovery, advancement and persistence

Rest needs a still, idle party without threats and a tonic in the selected living
member's pack. It advances in game time. Movement, an action or hostile damage
interrupts it. Completion consumes one tonic, restores authored vitality/resource
to living members and removes harmful conditions. Partial rest grants nothing;
pause and load cannot complete it for free. Fallen characters retain their packs
and need explicit revival. Revival requires a living caster, enough resource, a
cordial, a remaining per-character revival, and no active threat.

Enemy spawn identities award party experience once. Two current encounter kills
provide an advancement point per member. Idle characters can choose benefits; an active action must finish first so its
locked cost remains stable across saves. Choices add power/defense or reduce spell
cost and unlock Burst. Power now contributes to ordinary party attack damage,
so character equipment and advancement have an actual combat effect.

Schema 6 adds spellbooks, quick slots, paid action phases, spell flight payloads,
enemy resource, condition/tick countdowns, partial rest, claimed encounter rewards
and chosen advancements. Restore validates these against the roster, catalogue
and resolved enemy identities before publishing a replacement expedition. Older
prototype save schemas are rejected explicitly.

## Validation

Game checks cover free selection, learned-only slots, idempotent rewards,
advancement benefits, forged snapshot rejection, status refresh, periodic timing,
callback-driven death removal, Engine slow evaluation, unscaled expedition time,
and casts restored in recovery without repeating settlement.

Ordinary browser controls verified Mend (Warden 28→40, Mender resource 20→16),
Ward save/load, a partial rest saved at 6.33 seconds and resumed to completion
with exactly one tonic consumed, Hand opening the properly keyed/weighted gate,
and a rifle shot followed by Spark defeating the raider and granting eight XP.
Runtime captures and further observations are indexed in
`docs/evidence/milestone-6/`. Build, browser interaction and GPU observation are
separate evidence layers; the record states the limits of each.

The external Wolf run visibly restored the paused encounter and rendered the
textured corridor and shaded sprites. Its native spell-panel click did not open
the panel, so it does not independently verify Lantern's timer or provide a
lighting comparison. See `docs/evidence/milestone-6/gpu-report.md`.
