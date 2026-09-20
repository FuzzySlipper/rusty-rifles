# Lane: Existing product reuse

**Always on.** Run this lane on every task.

## One question

Does this change create a competing mechanism instead of extending this
repository's existing owner for that behavior?

## Why

The same failure as upstream reinvention, one level down. An agent working from
a task description will add a new service, catalog, or state holder without
noticing that this repository already owns that concept. The result is two owners
for one concept, which is worse than an absent feature because both look correct
in isolation.

Campaign #8356 is migrating scattered authorities toward canonical owners
(authored archetypes, composed character facade over canonical entities,
`StatsComponent`-owned stats, equipment/effect contributions, concrete
combat/action owners, active-floor aggregate, one current-state save codec).
During the migration there will legitimately be two paths for a short time;
the task must name the temporary adaptation explicitly. A second path with no
migration note and no removal plan is a finding.

## Basis required for an actionable finding

Name all four:

1. the existing owner in this repository, by file and type or member
   (e.g. `Party/PartyState.cs`, `Magic/MagicState.cs`, `Items/`,
   `RiflesProduct.*.cs`, `Generation/`, `Expedition/`, `Content/`);
2. the new duplicate, by file and line;
3. the overlapping state, behavior, or authority the two share;
4. the consequence — which callers now disagree, or which invariant can no longer
   hold — and the relevant callers by name.

Search before concluding: read `AGENTS.md` for the ownership split and
`docs/gameplay-design.md` (once #8357 lands) for the landed-owner map rather
than assuming from a directory name.

## Placement and mechanism shape

When the change adds gameplay behavior rather than duplicating it, check two
further things as part of this same lane:

1. **Concrete owner home.** Gameplay behavior belongs in its concrete
   game-domain owner (party, inventory, combat/action, magic/progression,
   floor lifetime, save codec), composed explicitly by the product root.
   Growing another `RiflesProduct.*` partial that keeps member action
   dictionaries, enemy actions, and orchestration in the product root instead
   of moving them to the owning domain is a finding with the same four-part
   basis: the correct owner, the landed location, the shared concept, and
   which future callers now look in the wrong place. This repository builds
   one game directly — never propose a reusable blobber kit, ruleset
   framework, universal event bus, reflection discovery, scripting DSL, or
   plugin system as the "correct" home.
2. **Direct-operation path.** Simple reads and actions go through direct typed
   methods on the owning service. Typed contribution events exist only for
   actual independently authored contributors. A new ad-hoc resolution path,
   generic bus, ambient `Resolve<T>()`, reflection scan, or gameplay DSL that
   parallels the existing direct calls is a finding with the same four-part
   basis: the existing resolution owner, the new parallel path, the overlapping
   participants, and which callers now resolve through different machinery.

## Not a finding

- A new file, or a similar name. Overlap must be shown in state or behavior.
- Extension of an existing owner that happens to add a type, member, or file.
  That is the desired outcome of this lane.
- A deliberate second implementation that the task explicitly calls for, for
  example a named temporary adaptation with its consumer and removal plan.
- Simple reads/actions done through direct methods rather than an event path.
  Demanding indirection for simple work is ceremony, and belongs to the
  runtime-trust lane.
