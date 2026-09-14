await sleep(6500);
await browser({op:'press',selector:'[data-run-load]',key:'Enter'});await sleep(1800);
const v=await browser({op:'inspect',selector:'button'});if(v.targets.some(t=>t.text==='Pause'))await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});
checkpoint('Reload after host restart',await capture({label:'host-restart-load'}));checkpoint('Location',await browser({op:'inspect',selector:'[data-inventory-status]'}));checkpoint('Feedback',await browser({op:'inspect',selector:'p'}));
