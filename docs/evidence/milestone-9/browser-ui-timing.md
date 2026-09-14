# Owner browser HUD isolation, 2026-09-14

The owner reported native inventory transfers work, but the demo was sluggish.
Stopping automated clients did not restore responsiveness. Disabling HUD
projection and refreshing did. These are owner observations, not automated
GPU evidence.

With the full HUD restored, browser-local instrumentation measured:

| Sample | DOM callback p50 / p95 / max | Delivery gap p50 / p95 / max | Latest callback age |
| --- | --- | --- | --- |
| First | 0.30 / 0.70 / 12.60 ms | 114.70 / 118.60 / 7739.10 ms | 76956 ms |
| With slow-frame attribution | 0.40 / 0.60 / 0.90 ms | 116.60 / 119.00 / 125.50 ms | 53 ms |

The second sample retained an 8021.50 ms frame, approximately 60 seconds old:
8018.50 ms attributed to `Response.arrayBuffer.then` in the served
`engine/product-browser-host.js`, character offset 1263454; forced layout
4.80 ms and layout/paint tail 0 ms. A following 521.10 ms frame included
multiple EventSource callbacks (5–10 ms each) and a 59.80 ms animation callback.

The exact served bundle maps offset 1263454 to `bD`, renderer resource loading:
`await response.arrayBuffer()` followed by `fD` → `jx` → synchronous SHA-256
`Px`. This identifies a synchronous resource-verification suspect; browser
promise attribution does not prove hashing alone consumed the entire duration.
Offset 1345547 is EventSource output decoding/delivery. Offset 1113392 is the
renderer animation-frame callback.

The recent second-sample HUD delivery is steady. It does not establish that
all gameplay is responsive, nor explain every earlier degraded attachment.
The owner subsequently confirmed movement and clicks are responsive. The
upstream startup resource-admission investigation remains separate. No performance fix is claimed from instrumentation alone.

The timing feature was pushed in aa45111. UI typecheck and CoreCLR staging
passed. The broker required restart to serve changed UI modules; LAN readback
confirmed the new code after restart. No automated clients were opened for
these owner measurements. Renderer metrics shared by multiple clients were
not used to infer this browser's performance.
