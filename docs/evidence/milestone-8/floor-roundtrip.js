await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(300);
await browser({op:'press',selector:'button:text-is("Descend to Flooded Stores")',key:'Enter'});
for(let i=0;i<30;i++){await sleep(300);const r=await browser({op:'inspect',selector:'[data-travel-panel]'});if(r.targets[0].text.includes('Return to Supply Approach'))break;}
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(300);
checkpoint('Stores arrival',await capture({label:'stores-arrival'}));checkpoint('Travel feedback',await browser({op:'inspect',selector:'[data-inventory-feedback]'}));
await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(300);
await browser({op:'press',selector:'button:text-is("Return to Supply Approach")',key:'Enter'});
for(let i=0;i<30;i++){await sleep(300);const r=await browser({op:'inspect',selector:'[data-travel-panel]'});if(!r.targets[0].text.includes('Return to Supply Approach'))break;}
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(300);
checkpoint('Arrival restored',await capture({label:'arrival-restored'}));checkpoint('Travel panel',await browser({op:'inspect',selector:'[data-travel-panel]'}));
