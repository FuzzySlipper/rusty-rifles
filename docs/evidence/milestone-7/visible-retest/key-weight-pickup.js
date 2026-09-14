await browser({op:'press',selector:'[data-inventory-item][data-owner^="combat:flight:"][data-definition="key"]',key:'Enter'});
await browser({op:'press',selector:'[data-owner-destination="member:blade"]',key:'Enter'});await sleep(400);
await browser({op:'press',selector:'[data-inventory-item][data-owner^="combat:flight:"][data-definition="weight"]',key:'Enter'});
await browser({op:'press',selector:'[data-owner-destination="member:blade"]',key:'Enter'});await sleep(400);
checkpoint('Puzzle items carried',await capture({label:'puzzle-items-carried'}));
checkpoint('Blade pack',await browser({op:'inspect',selector:'[data-inventory-owner][data-owner="member:blade"]'}));
await browser({op:'press',selector:'button:text-is("Save")',key:'Enter'});await sleep(300);
await browser({op:'press',selector:'[data-inventory] > summary',key:'Enter'});
