# Brave presentation recovery investigation

Observed on Rifles `60f727d`, paired Engine `03ac310b95c2`, runtime
`1478498/1/1`, using the user's accelerated NVIDIA RTX 4080 SUPER / ANGLE D3D11
browser. This investigation did not restart the user's session or change game
behavior.

## Evidence

The screenshot reports 9.8 Hz submission, 166.6 ms last interval, 0.60 ms
synchronous submission and 3.05 ms GPU time. The simulation advances at about
60/s; C# callback p50 is 1.327 ms, p95 1.909 ms. There is no queued input in the
provided snapshot; zero input admission latency does not prove successful
keyboard delivery when no input is reaching the queue.

[User diagnostics](evidence/brave-recovery/user-diagnostics.txt) show one initial
ready/baseline-established state, then degraded state and attachment replacement
roughly every 6.2 seconds. All subsequent listed baselines remain unestablished.
This reproduces the recovery pattern seen before the previous Engine update;
the update did not resolve it on this browser.

The live read-only `engine.renderer.detail` response was collected through the
existing Engine debug route, without launching another browser. Its canvas
(2067 x 1997) and accelerated renderer match the user's screenshot. The
[raw response](evidence/brave-recovery/renderer-detail.json) reports:

- A recent callback interval of 5816.4 ms; callback work p95 only 2.5 ms.
- Five callback attempts, three admitted and two backend-blocked in that retained
  window. This short window is not a steady-state benchmark.
- Received/applied presentation frames arriving in bursts (recent received rate
  372/s, applied rate 716/s), not smooth display cadence. These rates use separate
  windows and must not be compared as sustainable throughput.
- Forty realized texture definitions: 69,981,589 encoded bytes and 251,790,720
  decoded bytes. Repeated logical definitions share resource identities; this
  number alone does not prove repeated network downloads or GPU exhaustion.

A separate six-second loopback SSE read received 5,051,306 bytes, one complete
baseline and ongoing incremental batches without a lag event. It was stopped at
its explicit six-second limit. This establishes local delivery, not remote
network/browser health; it does not reproduce Brave.

## Owning path and next investigation

Engine's `render/packages/product-browser-host/src/local-transport.ts` replaces
an established output subscription after stream failure/lag. The corresponding
`product-browser-host.ts` begins projection recovery, invalidates queued renderer
work, enters degraded state, replaces the retained frame and resets camera
motion. Recovery gates remain until the replacement and trailing renderer work
settle. These mechanisms explain why repeated recovery can interrupt input and
flash/reset presentation even with a fast GPU and healthy C# simulation.

The initiating cause is still unknown: the available host-status log does not
identify whether the repeated replacements began with output lag, a socket
failure, decode failure, renderer invalidation or some other recovery trigger.
Do not claim a proven bandwidth, Brave-specific, GPU, or Rifles gameplay fault.

The next Engine-side diagnostic/fix should preserve the exact recovery trigger
and output cursor/epoch context for every attachment replacement, then exercise
fresh-baseline installation with realistic remote delivery and delayed browser
consumption. Correlate subscriber writes/close reason, baseline arrival and
installation duration, pending renderer work, and input gate release. Confirm
that one recovery actually settles instead of repeatedly replacing its baseline.
The browser callback gap and backend admission history should be assessed in
that same trace, rather than reducing image resolution or changing game timing
without evidence.

Acceptance is the user's failure case: sustained healthy attachment, prompt
keyboard and UI response, and no periodic camera jump under the same remote
accelerated browser. A local build or software-browser control test is not that
acceptance. No downstream transport, renderer scheduling, or recovery shim was
added.
