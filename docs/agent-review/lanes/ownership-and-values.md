# Lane: Ownership and values

**Optional.** Use when the change crosses an ownership seam, adds tuning, or
touches content definitions.

## One question

Does this change leak policy across its owner seam, or put authored and tunable
values into incidental code?

## Basis required for an actionable finding

Name all three:

1. the actual assumption or value, quoted, with file and line;
2. its current owner and its correct owner, in `AGENTS.md`'s terms (C# owns
   dungeon intent/rules/formation/encounters/progression/timing/save meaning;
   Engine owns lifecycle/admitted time/input/rendering/camera/spatial/resources/
   persistence primitives; TypeScript is a DOM companion only; tuning lives in
   versioned `content/` files loaded into typed records at the owning domain's
   boundary; procgen stays pure);
3. the affected uses — what breaks or becomes wrong when that value changes.

## Not a finding

- A demand for a universal abstraction, a new interface, or a generic factory.
- A constant for every literal. Compact structural constants stay beside the
  algorithm that owns them; only genuinely adjustable or authored values are
  promoted to `content/`.
- A rename that moves vocabulary somewhere without changing who owns
  the decision.
- A demand for validation, verification, or audit machinery as the "correct
  owner" for a value. That is a runtime-trust question, not an ownership one.
