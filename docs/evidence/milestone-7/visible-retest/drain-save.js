await browser({op:'press',selector:'button:text-is("Restart")',key:'Enter'});await sleep(800);
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(350);
await browser({op:'press',selector:'button:text-is("Load")',key:'Enter'});
let restored=false;
for(let i=0;i<20;i++){await sleep(300);const r=await browser({op:'inspect',selector:'[data-inventory-feedback]'});if(r.targets.some(t=>t.text==='Expedition restored')){restored=true;break;}}
if(!restored)throw Error('Restore not observed');
checkpoint('Restored hazard run',await capture({label:'restored-hazard-run'}));checkpoint('Position',await browser({op:'inspect',selector:'[data-inventory-status]'}));
