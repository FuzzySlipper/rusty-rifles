# Debug tools

The top-right Debug toolbar mounts Engine's packaged console and renderer metrics.
The console provides the generated catalog, completion, response transcript,
diagnostics and product/runtime telemetry. Use the matching pair's
`rusty-live-debug --origin URL --command COMMAND` for CLI inspection.

Useful Engine commands include `engine.renderer`, `engine.renderer.presentation`,
`engine.input` and the catalog's `playtest.*`, `entities.*` and `interaction.*`
modules. Command availability comes from the running host catalog.
`interaction.inspect` reports labels, IDs/revisions and rejection reasons;
`interaction.use ID REVISION` uses the product's ordinary handler with explicit
semantic target assistance. Inspect again after any mutation.

Rifles contributes `rifles.audio.read`, `rifles.profile.read`, `rifles.profile.start`, `rifles.profile.stop`, `rifles.profile.ui`,
`rifles.floor.read`, `rifles.floor.navigation`, expedition inspection and martial
drill commands. Read the catalog for argument signatures; do not substitute an
old milestone transcript for current help. Floor inspection describes resolved
cells, routes, items, generated mechanisms, encounter placements and bounded
navigation decisions without regenerating a floor.

`rifles.drill.list` loads and validates the authored drill definitions on demand.
`rifles.drill.start ID true` explicitly replaces the current run with the selected
fixture. Without the true replacement intent it refuses. Drills use the normal
floor admission and gameplay owners. A malformed drill file does not block boot.

For visible testing use crew-services `playtest` with an owned host from
`.den-serve.json`. The product's assist adapter reports cardinal pose, current
pause/difficulty, party/enemy state and ordinary action bindings/durations.
Held/action-driven time lets observation remain free while ordinary inputs advance
admitted simulation. Free look is not a gameplay action; use the observer camera
when an inspection view is needed. Record assistance, original captures and
frame/step correlation separately from product acceptance.

The offline floor exporter lives in Game checks:

```sh
dotnet run --project tests/Game -c Release -- --export-floors /tmp/rifles-floors
python3 scripts/render-floor-plan.py /tmp/rifles-floors/29-arrival.json /tmp/arrival.svg
```

`python3 scripts/generate-audio.py` rebuilds committed deterministic WAV assets from
the audio recipe. Compare their hashes when editing the recipe or generator.
Store dated timings, raw profiles, logs and screenshots on the Den task, rather
than adding an evidence directory to the repository.

Use `rifles.drill.start skeleton-musketeer true` in an owned test session to
prepare one skeleton musket watch in a normal ranged lane. It starts paused;
ordinary Resume, Fire and movement controls exercise the real combat owners.
Menu → Art comparison → Move light / Toggle room lights provides repeatable
lighting comparisons. Drill setup replaces the run and is inspection assistance; it is
not ordinary traversal evidence.
