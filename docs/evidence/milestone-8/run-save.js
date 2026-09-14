await browser({op:'press',selector:'button:text-is("Save")',key:'Enter'});await sleep(500);
checkpoint('Saved run',await browser({op:'inspect',selector:'[data-inventory-feedback]'}));
await browser({op:'press',selector:'button:text-is("Restart")',key:'Enter'});await sleep(1800);
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(300);
await browser({op:'press',selector:'button:text-is("Load")',key:'Enter'});
for(let i=0;i<30;i++){await sleep(300);const r=await browser({op:'inspect',selector:'[data-inventory-feedback]'});if(r.targets.some(t=>t.text==='Expedition restored'))break;}
checkpoint('Multi-floor restored',await capture({label:'multi-floor-restored'}));checkpoint('Load feedback',await browser({op:'inspect',selector:'[data-inventory-feedback]'}));
