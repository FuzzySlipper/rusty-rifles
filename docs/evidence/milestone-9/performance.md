# Milestone 9 performance scope and working budgets

These are working regression budgets for the measured prototype and hosts,
not minimum-spec promises or a claim of zero startup stalls.

| Work | Host / scope | Working budget and evidence |
| --- | --- | --- |
| C# update | Ryzen 7 8845HS, populated live floor | p95 below 4 ms within a 16.67 ms simulation step; observed p95 2.392 ms during the reported slow browser session, with simulation progressing about 60/s. |
| Product DOM callback | Owner Brave, NVIDIA RTX 4080 SUPER browser machine | p95 below 2 ms; observed 0.6 ms and max 0.9 ms over the latest 256 samples. This excludes Engine decoding, layout and rendering. |
| HUD delivery | Same owner browser, active state changes | Approximately 100–125 ms between normal updates, rather than a full snapshot every simulation frame. Observed p95 119 ms. Identical state now has no publication requirement. |
| Resolved CPU floor generation | Ryzen 7 8845HS, 12-floor bank | Up to 6 s for the difficult bank case; retained run median 139 ms, max 5519 ms (seed 1 Stores). Engine activation and asset admission are separate. |
| Warm accelerated presentation | Remote GPU at 1280×720 | Target at least 50 submissions/s in ordinary populated navigation; earlier observed 55.7 Hz. Renderer identity, canvas size and changing sample sequence must match the observing client before interpreting any shared metrics. |
| Retained resources | Same art treatment after warmup, repeated restart/travel | No monotonic increase in geometry/material/texture or spatial owners; compare like-for-like settled scenes and record peaks separately. |

The generator harness intentionally runs both candidate and resolved generation.
Its 19.227 s total covers **24 generation calls**, plus four expedition graphs;
it is not a single floor's delay. The retained resolved-generation half totals
9.854 s across 12 floors. See `generation.txt` and `generation-bench.cs.txt`.

Browser asset admission remains a known Engine cost. The owner captured an
8-second resource-response long task at startup, followed by steady operation.
The pinned Engine hashes resource bytes synchronously; the timing attribution
can include subsequent promise work and does not isolate hashing alone.
`browser-ui-timing.md` retains that distinction. Reducing enemy density or
substituting a downstream transport is not an appropriate remedy.

Renderer diagnostics are shared latest-client observations. A software browser
can overwrite a GPU browser's displayed metrics. Stale or foreign observations
are not used as GPU acceptance, and unavailable GPU timers are not zero.

## Retention observations on 718a05f

| Settled scene | Geometry | Materials | Textures | Handles |
| --- | ---: | ---: | ---: | ---: |
| Seed 29 arrival, three ordinary restarts | 26 | 23 | 40 | 33 |
| Seed 29 Stores | 35 | 29 | 40 | 44 |
| Seed 29 Redoubt | 42 | 38 | 40 | 50 |
| Completed Redoubt restored after new-run reset | 42 | 38 | 40 | 50 |
| New seed 314159 arrival | 21 | 20 | 40 | 28 |
| Seed 314159 Stores before return trips | 35 | 29 | 40 | 44 |
| Same Stores after two arrival/Stores return trips | 35 | 29 | 40 | 44 |

Different resolved scenes have different geometry/material counts; those rows
are not a like-for-like leak comparison. Repeated restarts, same-floor restored
completion and the two return trips are the matching comparisons. None grew.
All recorded sprite/material fallback counts are zero. Readbacks are retained
as `resources-*.json`; return-trip script is
`922c366e-03f7-4a2b-8e11-d4935676ad68`. These are renderer owner observations,
not a process-heap measurement or an independent spatial-owner census.

Spatial ownership also has an explicit teardown path: each DungeonScene owns
one SpatialSession and disposes it with its scene. Product replacement disposes
the previous scene; FloorFactory temporary previews are scoped with `using`.
Inactive floors retain snapshots rather than a second live SpatialSession.
This source check complements the live travel/resource checks above; it is not
reported as an independently sampled native allocation count.

## Final accelerated sample

With the root software client stopped and only the Wolf GPU observer active,
`gpu-metrics.json` reports accelerated AMD rendering at 1280×720 backing/CSS,
58.77 submissions/s, 17.06 ms last interval and 1 ms synchronous submission.
The browser exposes the generic adapter string “Radeon HD 3200 Graphics, or
similar”; that string is not asserted to identify the physical card. GPU timer
availability remains separate from submission pacing. This is a warm sample,
not a startup-stall measurement or a sustained worst-case benchmark.
A later same-client sample (`gpu-metrics-later.json`) advanced render sequence
12123 → 18013 and remained at 58.53 submissions/s, 17.08 ms interval and 1 ms
synchronous submission. Thus the readout was advancing, not one stale GPU sample.
