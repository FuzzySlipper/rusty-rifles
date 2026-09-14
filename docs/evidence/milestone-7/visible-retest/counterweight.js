await browser({op:'press',selector:'[data-inventory] > summary',key:'Enter'});
await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(300);
await browser({op:'press',selector:'canvas',key:'Shift'});
for(let i=0;i<3;i++){await keyboard.hold(['W'],100);await sleep(280);}
await keyboard.hold(['F'],100);await sleep(400);
checkpoint('Gate requires counterweight',await capture({label:'gate-requires-weight'}));
for(let i=0;i<3;i++){await keyboard.hold(['S'],100);await sleep(280);}
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(400);
await browser({op:'press',selector:'[data-inventory] > summary',key:'Enter'});await sleep(200);
await browser({op:'press',selector:'[data-inventory-item][data-owner="member:blade"][data-definition="weight"]',key:'Enter'});
await browser({op:'press',selector:'button[data-owner-destination]:text-matches("^Counterweight plate")',key:'Enter'});await sleep(400);
checkpoint('Weight placed',await capture({label:'weight-placed'}));
checkpoint('Plate contents',await browser({op:'inspect',selector:'[data-inventory-body] button'}));
await browser({op:'press',selector:'[data-inventory] > summary',key:'Enter'});
