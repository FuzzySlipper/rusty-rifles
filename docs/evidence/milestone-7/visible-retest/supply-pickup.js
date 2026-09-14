await browser({op:'press',selector:'[data-inventory-item][data-definition="key"]',key:'Enter'});
await browser({op:'press',selector:'[data-owner-destination="member:warden"]',key:'Enter'});await sleep(400);
checkpoint('Picked up key',await capture({label:'picked-key'}));
await browser({op:'press',selector:'[data-inventory-item][data-owner^="combat:flight:"][data-definition="tonic"]',key:'Enter'});
await browser({op:'press',selector:'[data-owner-destination="member:warden"]',key:'Enter'});await sleep(400);
checkpoint('Picked up recovery supply',await capture({label:'picked-supply'}));
await browser({op:'press',selector:'[data-inventory] > summary',key:'Enter'});
