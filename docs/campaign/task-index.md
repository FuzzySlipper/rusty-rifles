# Classic blobber task campaign

Next-layer work: [martial command campaign #8388](martial-command-index.md).
This page retains the original foundation campaign rather than tracking new work.

Den project: `rusty-rifles` · Aggregate campaign: **#8187**.

Created 2026-09-13: **63 implementation tasks, nine milestones, 136 dependency links**. All tasks were read back from Den with matching acceptance descriptions, parent, priority and dependencies. The graph is acyclic and every implementation task feeds final acceptance #8250. Creation is planning, not implementation completion.

Start with **#8188 (A01), typed file-authored gameplay and presentation definitions**. It is the only initially dependency-ready implementation task. The priority-5 parent is an aggregate, not an implementation lane. After A01, content/art work can proceed alongside the expedition skeleton; the milestone groups are not sequential gates.

Use Den for current status, assignments, dependencies and task-linked progress. This index and the [task plan](task-plan.json)/[ID map](den-tasks.json) record the initial campaign, not a second task tracker or synchronization system. [Strategy](../campaign-strategy.md) owns the overall scope; [art direction](../art-direction.md) and [prompt recipes](../../content/art/prompts/README.md) guide visual experiments.

Tasks include concrete behavior, file tuning, real callers and appropriate validation. If an Engine capability is absent, identify the narrow upstream dependency rather than implement a downstream substitute or mark the behavior complete. No gameplay implementation was performed while creating this campaign.

## 1. Durable expedition skeleton

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| A01 | #8188 | Load typed file-authored gameplay and presentation definitions | — |
| A02 | #8189 | Persist an identified expedition and its resolved starting floor | A01 (#8188) |
| A03 | #8191 | Unify party and actor movement on shared cells and edges | A02 (#8189) |
| A04 | #8196 | Make grid stepping and quarter-turn controls smooth and predictable | A03 (#8191) |
| A05 | #8192 | Route party UI actions and pause through the authoritative product | A02 (#8189) |
| A06 | #8197 | Select and use reachable world features through Engine targeting | A03 (#8191), A05 (#8192) |

## 2. First visual room

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| B01 | #8190 | Generate and catalogue a coherent first art comparison set | A01 (#8188) |
| B02 | #8193 | Apply generated repeating textures to voxel materials | B01 (#8190) |
| B03 | #8201 | Render clutter and interactable images as grounded world billboards | B02 (#8193), A06 (#8197) |
| B04 | #8198 | Render consistent directional stills for individual enemies | B01 (#8190), A03 (#8191) |
| B05 | #8203 | Light and compare the first textured room and sprite ensemble | B03 (#8201), B04 (#8198) |

## 3. Characters, items, and hands-on exploration

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| C01 | #8199 | Handle party roster, formation and derived character statistics | A05 (#8192) |
| C02 | #8194 | Own inventory items and stacks with atomic transfers | A02 (#8189) |
| C03 | #8202 | Equip characters and compare usable gear | C01 (#8199), C02 (#8194) |
| C04 | #8204 | Deliver drag-and-drop inventory with a click alternative | C03 (#8202), A05 (#8192) |
| C05 | #8205 | Pick up and place items at real world anchors | C02 (#8194), A06 (#8197), B03 (#8201) |
| C06 | #8206 | Use consumables and items on eligible targets | C03 (#8202), C05 (#8205) |
| C07 | #8207 | Open containers and transfer their stored contents | C04 (#8204), C05 (#8205) |
| C08 | #8208 | Solve doors, keys, levers and weighted plates with real items | C05 (#8205), C06 (#8206), C07 (#8207) |

## 4. First ranged-and-melee fight

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| D01 | #8209 | Execute real-time actions with commit and recovery phases | A03 (#8191), A05 (#8192), C06 (#8206) |
| D02 | #8210 | Select individual targets and resolve legal firing lanes | D01 (#8209), B04 (#8198), C08 (#8208) |
| D03 | #8211 | Resolve damage, defenses, death and recoverable remains | D01 (#8209), C03 (#8202) |
| D04 | #8213 | Fight with loaded rifles and meaningful reload timing | D02 (#8210), D03 (#8211), C04 (#8204) |
| D05 | #8214 | Launch travelling projectiles and thrown inventory objects | D02 (#8210), D03 (#8211), C05 (#8205) |
| D06 | #8217 | Encounter simple melee and ranged opponents in a real fight | D04 (#8213), D05 (#8214), B05 (#8203) |
| D07 | #8220 | Make the first fight readable through HUD and feedback | D06 (#8217) |

## 5. Crowds and tactical enemies

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| E01 | #8221 | Admit mixed-size enemies into authored cell footprints | A03 (#8191), D06 (#8217) |
| E02 | #8224 | Keep crowded enemy placements stable and repack smoothly | E01 (#8221), B04 (#8198) |
| E03 | #8225 | Resolve competing enemy movement reservations fairly | E01 (#8221), D01 (#8209) |
| E04 | #8228 | Route different enemy sizes with bounded Engine path queries | E03 (#8225) |
| E05 | #8222 | Give enemies sight, noise awareness and last-known targets | D06 (#8217), A06 (#8197) |
| E06 | #8232 | Patrol, pursue, search and return using the shared enemy model | E04 (#8228), E05 (#8222) |
| E07 | #8233 | Position ranged enemies for firing and reload opportunities | E06 (#8232), D04 (#8213) |
| E08 | #8234 | Author and integrate mixed-size mixed-range encounter groups | E02 (#8224), E07 (#8233), D07 (#8220) |

## 6. Spells and party development

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| F01 | #8212 | Select and prepare known spells through party controls | C04 (#8204), D01 (#8209) |
| F02 | #8215 | Cast and interrupt spells with exact resource settlement | F01 (#8212), D03 (#8211) |
| F03 | #8218 | Deliver projectile and area spells through shared targeting | F02 (#8215), D05 (#8214) |
| F04 | #8219 | Apply buffs, debuffs and resistances with bounded lifetimes | F02 (#8215), C03 (#8202) |
| F05 | #8223 | Use utility spells on exploration and world features | F03 (#8218), F04 (#8219), C08 (#8208) |
| F06 | #8226 | Recover party injuries and resources through explicit rest rules | F04 (#8219), C06 (#8206), E05 (#8222) |
| F07 | #8235 | Reward advancement and publish a useful starter spell catalogue | F05 (#8223), F06 (#8226), E08 (#8234) |

## 7. Rich procedural floors

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| G01 | #8195 | Generate expedition graphs with distinct floor roles | A02 (#8189) |
| G02 | #8200 | Place varied functional rooms from authored catalogues | G01 (#8195), B02 (#8193) |
| G03 | #8216 | Route loops and corridors for ranged and close combat | G02 (#8200), D02 (#8210) |
| G04 | #8227 | Realize generated doors, protected barriers and secrets | G03 (#8216), C08 (#8208), F05 (#8223) |
| G05 | #8229 | Compose solvable item puzzles and timed hazards | G04 (#8227), D05 (#8214), F05 (#8223) |
| G06 | #8230 | Traverse generated stairs, pits and bridges across floor levels | G04 (#8227), A04 (#8196) |
| G07 | #8231 | Detail voxel architecture without changing unintended routes | G04 (#8227), B05 (#8203) |
| G08 | #8236 | Populate generated spaces with size-aware tactical encounters | G03 (#8216), E08 (#8234) |
| G09 | #8237 | Budget loot, ammunition, recovery resources and clues by route | G05 (#8229), G08 (#8236), F07 (#8235) |
| G10 | #8239 | Validate, inspect and repair complete generated floors | G06 (#8230), G07 (#8231), G09 (#8237) |

## 8. Complete expedition loop

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| H01 | #8238 | Travel between floors and revisit persistent worlds | G06 (#8230), G08 (#8236), A02 (#8189) |
| H02 | #8240 | Save and restore the complete live expedition | H01 (#8238), F07 (#8235), D05 (#8214), E08 (#8234) |
| H03 | #8241 | Complete objectives, a finale and one-time rewards | G09 (#8237), F07 (#8235), H01 (#8238) |
| H04 | #8243 | Handle party defeat, recovery choice and a new expedition | H02 (#8240), H03 (#8241) |
| H05 | #8244 | Tune difficulty and supply pressure across the run | H03 (#8241), H04 (#8243) |
| H06 | #8242 | Guide exploration with discovered maps and useful notes | G05 (#8229), H01 (#8238), A05 (#8192) |
| H07 | #8245 | Assemble a coherent three-floor playable expedition | H05 (#8244), H06 (#8242), G10 (#8239), B05 (#8203) |

## 9. Readability and closure

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| I01 | #8246 | Refine combat, targeting and inventory usability in live play | H07 (#8245) |
| I02 | #8247 | Make level art, sprites, lighting and sound coherent | H07 (#8245), E02 (#8224) |
| I03 | #8248 | Keep populated-floor rendering and generation responsive | H07 (#8245) |
| I04 | #8249 | Close seed and live-save regressions across game systems | H02 (#8240), G10 (#8239), H07 (#8245) |
| I05 | #8250 | Play and close the complete classic blobber foundation | I01 (#8246), I02 (#8247), I03 (#8248), I04 (#8249) |
