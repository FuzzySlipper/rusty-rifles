await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(250);await browser({op:'press',selector:'canvas',key:'Shift'});
await keyboard.hold(['D'],100);await sleep(280);await keyboard.hold(['W'],100);await sleep(280);
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(300);
checkpoint('Lowered pit',await capture({label:'lowered-pit'}));checkpoint('Pit position',await browser({op:'inspect',selector:'[data-inventory-status]'}));
await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(250);await browser({op:'press',selector:'canvas',key:'Shift'});
await keyboard.hold(['S'],100);await sleep(280);
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(300);
checkpoint('Climbed out',await capture({label:'climbed-out'}));checkpoint('Climb position',await browser({op:'inspect',selector:'[data-inventory-status]'}));
