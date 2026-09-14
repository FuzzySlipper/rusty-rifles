await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(250);
try {await browser({op:'press',selector:'canvas',key:'Shift'});await keyboard.hold(['Q'],100);await sleep(700);checkpoint('north targets',await browser({op:'inspect',selector:'button[data-target]'}));
const r=await browser({op:'inspect',selector:'button[data-target]'});const t=r.targets.find(t=>!t.disabled);if(t){await browser({op:'press',selector:'button[data-target]:text-is('+JSON.stringify(t.text)+')',key:'Enter'});await sleep(250);await browser({op:'press',selector:'button:text-is("Attack [Space]")',key:'Enter'});await sleep(1400);}}
finally {await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});}
checkpoint('after',await browser({op:'inspect',selector:'[data-inventory-status],button[data-target],[data-combat-log]'}));
checkpoint('north',await capture({label:'north-target'}));
