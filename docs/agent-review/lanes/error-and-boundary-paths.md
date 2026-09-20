# Lane: Error and boundary paths

**Optional.** Use when the change adds parsing, input handling, or failure paths.

## One question

Does the change handle the boundary and failure paths its inputs actually reach?

Scope this lane to boundaries the change actually touches. Trusted
first-party runtime paths — admitted content, attached live state, Engine
services, current-schema saves — are not hostile inputs. Do not demand
hashing, revalidation, proposal/acceptance, revision guards, snapshots, or
rollback for ordinary gameplay; that question belongs to the runtime-trust
lane, which rejects it by default. The content-admission and offline
boundaries are where strict checking belongs.

## Basis required for an actionable finding

Name all three:

1. the input, state, or boundary condition;
2. the code path it takes, with file and line;
3. the observed wrong result — a crash, a silent default, a wrong value, a
   swallowed error, or a partial mutation that is not rolled back.

Prefer a case you can execute. A reproducing command is the strongest evidence
this lane can carry, and `bash` is available for it.

## What to probe

- Empty, zero, single, and maximum inputs, and the transition between them.
- Malformed authored content at the admission boundary, and the definition
  cases the loader claims to accept.
- Failure of an Engine call, and whether the product state stays coherent when it
  fails partway.
- Cancellation or interruption between two mutations that were meant to be one
  operation.
- The absent case: a required definition, content reference, or resource that is
  missing rather than wrong.

## Not a finding

- A defensive check for a state the type system or the caller already excludes.
- A request for broad input validation unrelated to what this change reads.
- An upstream Engine failure the product correctly surfaces and stops on.
- A demand for revalidation, hashing, compatibility fingerprints, schema
  versions, proposal/acceptance, or snapshot/rollback on a trusted runtime
  path. Report that as a runtime-trust concern only if the change ADDS such
  machinery; never request it from this lane.
