# C04 acceptance recheck during G01

2026-09-13, crew-services CLI profile `rusty-rifles`, headless browser session
`b4196404-68cc-4f7f-b5c8-41281851e421`, slot-1. A prior attempt against a
misidentified profile/retired broker route did not create a session; this
observation used the configured crew-services profile successfully.

The independent playtester used DOM inspection and ordinary click operations.
Blade's restorative tonic stack (2) was selected and transferred to Warden.
Warden changed from 73/160 mass and 18/24 space to 77/160 and 20/24;
Blade changed from 9/160 and 3/24 to 5/160 and 1/24. Inspection located the tonic
stack only under Warden. See [before](inventory-before.png) and
[after](inventory-after.png), copied unchanged from service captures.

The service exposes absolute pointer placement, keyboard and DOM actions, but
no documented held-pointer drag/down/up primitive. No synthetic drag events
were substituted. Native drag success, invalid destination and drag cancellation
remain unverified; #8204 stays open. Escape/outside click left a click-selected
item selected; that does not establish the behavior of a native drag gesture.

The session reported GPU ReadPixels stalls and debug-request HTTP 500 warnings.
This is click-operation evidence, not a performance or recovery acceptance.
Cleanup receipt confirmed browser_closed=true, released=true, slot-1.
