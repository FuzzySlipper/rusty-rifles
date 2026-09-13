# Runtime debug tools

The top-right **Debug** toolbar hosts the packaged Engine UI. Press Escape to
release the game cursor, then use **Open debug console** or **Show metrics**.
The buttons are also keyboard accessible with Tab and Enter. **Hide metrics**
and **Close debug console** remove the readouts when finished.

The console provides Engine's generated command catalog, completion, response
transcript with Copy, diagnostics, and product/runtime lane telemetry. Useful
commands for delayed controls or camera jumps:

- `engine.renderer`: compact renderer timing, pacing, canvas and resources.
- `engine.renderer.detail`: renderer admission, callback and cadence detail.
- `engine.renderer.presentation`: submitted presentation revisions and camera.
- `engine.renderer.status`: metrics visibility and the latest renderer facts.

Compare renderer submission/pacing with the console's **Product/runtime lane**
input admission latency, input queue age, worker phases and in-flight operation.
The diagnostics log can reveal browser degradation or attachment recovery.
Smooth-looking animation alone does not establish timely input admission.
Unavailable GPU facts remain unavailable; software-browser measurements should
not be interpreted as measurements of the player's GPU.

Rifles only mounts `mountLiveDebugPanel` and `mountRendererMetricsWidget` and
uses the packaged `createLiveDebugHttpTransport`. Engine owns command semantics,
diagnostic collection, polling and HTTP routes. Closing/remounting the UI
releases its requests and widgets. The console is separate from the expedition
panel's gameplay-key guard, so typing commands cannot trigger game actions.
Renderer visibility follows Engine state, including console show/hide commands.

This adds investigation tools; it does not establish the cause or resolution of
the reported Brave input delays and transient camera jumps.

## Verification

`bash scripts/check.sh` passed (UI typecheck, solution build, game/procgen
checks, artifact self-check and CoreCLR staging); the final layout adjustment
also passed UI typechecking. In the served browser, DOM click/keyboard actions
opened the Engine console, executed `engine.renderer`, and showed renderer
metrics alongside its response. Original evidence:
[console and metrics](evidence/debug-tools/console-and-metrics.png).
The test used the crew-services headless Chromium backend. Initial raw pointer
clicks did not activate the toolbar; successful DOM-targeted activation is
recorded separately and is not proof that the reported Brave input fault is
fixed.

Closing/reopening the console and hiding metrics were also verified; a second
`engine.renderer.status` command succeeded after remount. See
[remounted console](evidence/debug-tools/remounted-console.png). Both owned
playtest sessions were stopped afterward.
