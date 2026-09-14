await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(300);await browser({op:'press',selector:'canvas',key:'Shift'});
for(let i=0;i<5;i++){await keyboard.hold(['W'],100);await sleep(600);}
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(300);
checkpoint('Last passage',await capture({label:'stores-last-passage'}));checkpoint('Position',await browser({op:'inspect',selector:'[data-inventory-status]'}));checkpoint('Targets',await browser({op:'inspect',selector:'button[data-target]'}));
