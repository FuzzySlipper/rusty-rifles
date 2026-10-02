# Generated floor data snapshot

Four seeds (0, 1, 29, 83), three authored floors each. The Game checks export
the data; `scripts/render-floor-plan.py` renders the plans.
This bank is an inspection snapshot, not a byte-comparison fixture or visible
acceptance evidence. Runtime content is authoritative.

Authored definitions/tuning SHA256 (ordered paths, NUL, file bytes): `3279a283515c97512f9ce38a4bffea163193f9fdb990fbc3fe5f88ac7bdd492b`.

Rebuild:

```sh
dotnet run --project tests/Game -c Release -- --export-floors docs/generated/floor-bank
for floor in docs/generated/floor-bank/*.json; do
  case "$floor" in *-inspection.json) continue ;; esac
  python3 scripts/render-floor-plan.py "$floor" "${floor%.json}.svg"
done
```

Graph goals are mostly authored and fixed. Seeds vary physical realization,
room templates, routes, encounters and supplies. Dated validation results live
in Den.
