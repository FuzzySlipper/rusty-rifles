const b=await browser({op:'inspect',selector:'button'});if(b.targets.some(t=>t.text==='Pause'))await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});
checkpoint('State',await capture({label:'combat-state'}));checkpoint('Log',await browser({op:'inspect',selector:'[data-combat-log]'}));checkpoint('Party',await browser({op:'inspect',selector:'button[data-member]'}));
