# Agent review packet — Rusty Rifles

Standing instructions for review subagents on this repo. Prior rounds
proceeded on task-prompt scope alone; this packet exists so no round has to
improvise a process or stop for lack of one.

## When a review opens

A review opens after `bash scripts/check.sh` is green on the reviewed
commits. The delegating message names the commit(s), files, and angle. That
scope block **is the lane file** — there is no separate per-lane document.

## Angles (pick the ones named in the delegation; never invent others)

- **contract-drift**: does new code consume exactly what the owning producer
  emits (fields, shapes, value sets)? Quote producer file+line vs consumer
  file+line.
- **error/boundary**: null/empty/degenerate inputs, first-frame state,
  transitions (run change, resize, shrink), focus/dispose/listener leaks.
- **rules-equivalence**: for refactors, is observable behavior identical on
  old content? Trace old vs new on the same scenario.
- **save-integrity**: old saves may break loudly (prototype waiver) but must
  never restore corruptly. Every reject path must throw, never accept.
- **geometry**: transforms, sign conventions, tie-breaks, caller coverage.

## Finding format

One section per finding: file+line, trigger condition, consequence, concrete
suggested fix. Severity: blocker (must fix before landing) / finding (should
fix) / low (hardening, fix if cheap) / note (no action). End with an explicit
verdict: **all-clear** or **changes requested**, plus anything verified as
handled (list it — handled paths are evidence too).

## Re-review

Re-review happens in the same session via `send_message`: the parent names
what changed per finding and what to re-check. Prior context carries over;
do not restart the analysis from zero.

## Authority

The task prompt's scope plus this packet is the complete process. A missing
per-lane file is never a reason to stop: if the delegation names a commit
and an angle, that is sufficient authorization to proceed.
