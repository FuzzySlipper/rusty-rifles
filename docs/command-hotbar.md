# Command hotbar and reload follow-up

Source `8e6688160145a1f6dc86d794a78abfecac437e2e` adds a party Reload order on R,
a fixed-slot command hotbar with readiness and reload countdowns, and direct
Inventory access. Interactable cycling is retained on T. Reload starts eligible
living, idle, unloaded muskets without selecting a soldier or enemy; an ongoing
reload or recovery is left alone. Automatic reload remains in place.

The hotbar exposes per-soldier readiness reasons on hover. A loaded musket can
still be unable to Fire when there is no exposed forward target in its reach.
That is separate from an unloaded weapon or a failed input connection.

## Verification

The full `bash scripts/check.sh` passed at the source revision above, including
UI typecheck, solution build, procgen/game checks, offline self-check and CoreCLR
staging. Log: `/tmp/rifles-hotbar-full-check.log`. The existing isolated broker
successfully launched that revision on port 37301. Browser session `4f66bb00-fc47-492d-b2fa-0b236e4b8133` shows the
[complete hotbar](evidence/command-hotbar/hotbar.png) at 1280×720 and
[Inventory opened from its button](evidence/command-hotbar/inventory-open.png).
Both original captures were inspected; copies are unmodified.

The observer confirmed Inventory open/close and Fire. Root then used the existing
`concentration` debug setup and ordinary native P/Space/R inputs for the timed
reload check. The original sequence is [retained here](evidence/command-hotbar/reload-input-sequence.js)
(supervisor script `822d5f36-be12-4a66-ad93-6cd992281192`). Reload progressed from
[2.4s before R](evidence/command-hotbar/reload-before-r.png) to
[1.7s after two R presses](evidence/command-hotbar/reload-after-r.png). Feedback
confirmed that the reload command was received and found no idle unloaded
musket, leaving the current actions intact. After recovery, the hotbar showed
[Loaded and Fire 3/6 ready](evidence/command-hotbar/reload-loaded.png).

This visibly verifies command delivery and preservation of an automatic reload
in progress. Starting a manual reload from an idle unloaded state is covered by
source admission checks; it was not separately isolated in the browser because
automatic reload normally starts immediately. A first root DOM-input attempt
had a Resume-selector timeout; another sequence did not establish Fire
admission. Neither is counted as a successful combat check. The final native
sequence above supplied the actual combat evidence.

The browser reported no page errors, only Chromium ReadPixels performance
warnings. A mid-run Engine diagnostic read reported zero warnings/errors.
Root restored a fresh paused Guard Hall through ordinary Restart, then stopped
the owned browser; cleanup confirmed `browser_closed: true, released: true`.

## Engine-owned work still required

Cursor unlocking is not implemented in the installed matched pair
`0.1.0-dev.1b208e9b33aa`. Engine Application Host's `focusGameplay()` always
requests pointer lock. Its interface/modal modes release the lock but also
suppress gameplay keys. No product-facing policy keeps gameplay input active
with an unlocked cursor. Den Engine **#8408** owns exposing and packaging that
capability; Rifles must consume the resulting matched pair. No downstream
pointer-lock interception or custom input transport was added.

The [reported log](evidence/command-hotbar/reported-runtime-failure.txt) and
[prior host diagnostics](evidence/command-hotbar/prior-host-diagnostics.ndjson)
show a distinct input-recovery failure. The latest browser attachment loses an
input request (`Failed to fetch`), then closes transport/output because control
replacement did not provide a fresh input binding. Final host telemetry is
still running at about 60 Hz with control revision 2 while the browser's last
baseline was revision 1. Local HTML menus remaining responsive is consistent
with that split. The exact reason the browser failed to establish the new
binding remains unresolved.

The earlier worker scheduler timeout, replacement and invalid rebind-clear
occurred roughly sixteen hours before the final failure. They are retained as
context, not asserted to cause the later failure. The installed pair already
contains the earlier terminal-diagnostic correction. New evidence was attached
to existing Engine **#8257**; a broker restart or short clean playtest does not
prove that intermittent recovery bug fixed.
