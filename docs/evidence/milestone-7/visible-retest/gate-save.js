await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(350);
await browser({op:'press',selector:'canvas',key:'Shift'});
for(let i=0;i<3;i++){await keyboard.hold(['W'],100);await sleep(280);}
await keyboard.hold(['F'],100);await sleep(400);
checkpoint('Latch opened',await capture({label:'latch-opened'}));
for(let i=0;i<2;i++){await keyboard.hold(['W'],100);await sleep(280);}
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(350);
checkpoint('Gate crossed',await capture({label:'gate-crossed'}));
await browser({op:'press',selector:'button:text-is("Save")',key:'Enter'});await sleep(400);
await browser({op:'press',selector:'button:text-is("Restart")',key:'Enter'});await sleep(500);
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(350);
await browser({op:'press',selector:'button:text-is("Load")',key:'Enter'});await sleep(600);
checkpoint('Gate save restored',await capture({label:'gate-save-restored'}));
checkpoint('Position',await browser({op:'inspect',selector:'[data-inventory-status]'}));
