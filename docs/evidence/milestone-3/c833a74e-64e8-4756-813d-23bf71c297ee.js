await browser({op:'press',selector:'[data-member="blade"]',key:'Enter'});await sleep(400);
await browser({op:'press',selector:'[data-inventory] > summary',key:'Enter'});
await browser({op:'press',selector:'[data-inventory-item][data-owner="member:seeker"][data-definition="rifle"]',key:'Enter'});await sleep(350);
await browser({op:'press',selector:'button:text-is("Equip main-hand")',key:'Enter'});await sleep(500);
checkpoint('equipped',await capture({label:'m3-equipped'}));
