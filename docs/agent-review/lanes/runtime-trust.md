# Lane: Runtime trust

**Always on (temporary counterbalance).** Run this lane on every task until
campaign #8356 has landed and ordinary trusted-path code is the established
gravity. When that happens, demote this lane to optional or retire it; do not
keep it as a permanent tax.

## One question

Does this change add validation, verification, or defensive machinery to a
trusted first-party runtime path without a concrete failure it prevents?

## Why

Content-admission thinking leaks into runtime, where it does not belong. This is
a single-player product built from trusted first-party code; there is no
multiplayer-cheating, MITM, wire-corruption, adversarial-content, or
future-multiplayer threat model that justifies policing every mutation. There
are no pointless SHA checks or repeated hashing gates on already-admitted bytes.

Campaign #8356 deliberately removes that ceremony: baseline
proposal/revision/replay/compatibility paths, global `commandRevision` /
inventory-revision / target-revision guards, whole-state snapshots and rollback
around ordinary gameplay, repeated hash admission of trusted live state,
constructing throwaway worlds to re-prove owned state, routing routine reads
through save capture (`Capture()` to read doors/dressing/anchors), validating
each retained floor against a synthetic active-party snapshot, and
schema-version / compatibility-fingerprint gates on current development data.
A reviewer that re-asks for the removed machinery undoes the campaign.

## Basis required for an actionable finding

Name all four:

1. the ceremony, quoted, with file and line — e.g. a propose/validate step, a
   hash/reverify on admitted content or a cache hit, a revision guard or
   snapshot/rollback around ordinary gameplay, a compatibility fingerprint or
   schema-version gate on current development data, a constructor revalidation
   of the same immutable definitions, a historical fallback reader;
2. why the path is trusted — admitted definitions, attached live components,
   Engine-delivered resources, current-schema save state the owning boundary
   already established;
3. the concrete failure it claims to prevent, and why that failure has no
   identified caller — or where the owning check already establishes it;
4. the simpler shape: direct mutation, the existing safeguard, deletion, or
   the boundary the check actually belongs at (usually content admission or
   offline import/tooling).

A finding that names only "this could be invalid" is not actionable. Name
the caller, the input that reaches the path, and what goes wrong without
the machinery.

## Where checking belongs

- Content admission (typed, validated C# records loaded through Engine content
  services at the owning domain's boundary; `content/definitions/`,
  `content/tuning/`): reject meaningful missing/duplicate authored references
  once, with actionable errors. Not this lane's target.
- Offline import/tooling (`Rifles.Procgen.Tool`, artifact I/O, pure-procgen
  correctness/decoder checks, build-time artifact hashes): validation is
  expected. Not this lane's target.
- Runtime (attached stats/effects/inventory, admitted content, Engine services,
  current-schema saves): default to trust. Keep checks that protect an actual
  requirement: concrete gameplay eligibility (identity lifetime, capacity,
  reach, equipped-state, busy/cooldown, delayed-impact recheck), track bounds,
  valid current content references, coherent current-save relationships, ABI
  and native lifetime/disposal order, genuine duplicate/dangling identity
  checks, optional atomic inventory edits for a genuinely all-or-nothing
  multi-item transfer, and understandable failure on malformed current data
  rather than partial success. A delayed action rechecking an actually changed
  target/item at impact is a real gameplay requirement, not ceremony.
- Explicit save capture, previews, tooling, or a stated multiplayer
  requirement may justify stronger machinery at its own boundary. It never
  defines the baseline for ordinary gameplay.

## Not a finding

- Content-admission rejection of missing/duplicate authored references, or
  actionable errors for invalid authored configuration.
- Pure-procgen correctness/decoder checks or offline artifact hashes.
- A concrete safeguard for an actual requirement listed above, kept local
  to the owner that establishes it.
- An Engine decoder or native-safety error the product surfaces and stops on.
- A stronger mechanism the task explicitly requires at a named boundary
  (tooling, explicit save capture).
- A compact structural constant beside the algorithm that owns it. That is
  the ownership lane's call, not this one.
