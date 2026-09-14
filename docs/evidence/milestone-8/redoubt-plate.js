async function press(s){await browser({op:'press',selector:s,key:'Enter'});await sleep(300);}
await press('button[data-member="blade"]');
await press('button[data-definition="weight"][data-owner="member:blade"]');
try{
await press('button:text-is("Resume")');await press('button:text-is("Toss onto plate")');await sleep(1800);
}finally{const v=await browser({op:'inspect',selector:'button'});if(v.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');}
checkpoint('Weight on plate',await capture({label:'weight-on-plate'}));checkpoint('Log',await browser({op:'inspect',selector:'[data-combat-log]'}));checkpoint('Position',await browser({op:'inspect',selector:'[data-inventory-status]'}));
