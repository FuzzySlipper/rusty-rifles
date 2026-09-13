# Reference-code navigation

These local repositories explain familiar gameplay concepts. They are not
Rifles dependencies, required fidelity targets, or sources to copy wholesale.
Their unfinished systems are explicitly called out in the maps. Start with a
specific behavior question; inspect the implementation and its callers before
adopting a rule. Rifles retains its C#/Engine boundaries and file-based tuning.

| Reference / map | Useful starting points | Important limits |
| --- | --- | --- |
| [DungeonMaster.NET](dungeonmaster-net.md) | C# party orientation, size/space layouts, throws, inventory storage, spell sequences | Incomplete medium layouts, ranged AI, spells and projectile methods; async polling is not an Engine timing model |
| [dungeonMaster-codex](dungeonmaster-codex.md) | Group occupancy/movement, timed attacks, ranged spacing, projectile impacts, drag/drop | TypeScript/React runtime is not a Rifles architecture; some legacy behaviors are approximations or unfinished |
| [DungeonEye](dungeoneye.md) | Corner-based floor items, slot equipment, party reach, spell metadata, visible monster placement | Major AI/attack/movement stubs; no reliable size enforcement or pathfinding |

Repository roots and Codebase Memory project names match exactly:

- `/home/research/blobber-games/DungeonMaster.NET` → `DungeonMaster.NET`
- `/home/research/blobber-games/dungeonMaster-codex` → `dungeonMaster-codex`
- `/home/research/blobber-games/dungeoneye` → `dungeoneye`

## Indexed lookup

All three indexes were populated on 2026-09-13 using the installed
`codebase-memory-mcp` CLI and the shared cache below. Indexes live outside this
repo and are rebuildable. Parsing is best-effort: the TypeScript index reported
three partial files; a missing search result is not evidence that behavior is
absent. Use coverage readback and exact text search to resolve gaps.

The same executable/cache are configured by the sibling Engine and Dagger
`.codex/config.toml` files. If that MCP is exposed in a future session, its
`list_projects`, `search_graph`, `get_code_snippet`, and `trace_path` tools offer
the indexed route. The CLI is usable without changing or restarting Codex.

```bash
export CBM_CACHE_DIR=/home/agent/.local/share/codebase-memory-mcp
codebase-memory-mcp cli --json list_projects
codebase-memory-mcp cli --json search_graph \
  --project DungeonMaster.NET --query 'Creature projectile' --limit 5 --format json
codebase-memory-mcp cli --json get_code_snippet \
  --project DungeonMaster.NET \
  --qualified-name 'DungeonMaster.NET.src.DungeonMasterEngine.DungeonContent.Entity.Creature.Creature.FindEnemies' \
  --include-neighbors true
codebase-memory-mcp cli --json check_index_coverage \
  '{"project":"DungeonMaster.NET","scopes":["src/DungeonMasterParser"]}'
```

Select the relevant project explicitly; use qualified names returned by a query.
For a changed checkout, refresh only that reference as needed:

```bash
CBM_CACHE_DIR=/home/agent/.local/share/codebase-memory-mcp \
  codebase-memory-mcp cli --json index_repository \
  --repo-path /home/research/blobber-games/DungeonMaster.NET \
  --mode full --name DungeonMaster.NET
```

Keep future maps bounded to useful concepts and concrete source entry points.
Do not grow them into whole-game parity checklists. Source locations and index
coverage may change after the research checkouts are updated.
