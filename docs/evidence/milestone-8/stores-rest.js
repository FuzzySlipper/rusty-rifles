async function press(s){await browser({op:'press',selector:s,key:'Enter'});await sleep(260);}
await press('button[data-member="mender"]');
await press('summary:text-is("Spells & recovery")');
try{
await press('button:text-is("Resume")');await press('button:text-is("Rest")');await sleep(8500);
}finally{const v=await browser({op:'inspect',selector:'button'});if(v.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');}
checkpoint('Rest',await browser({op:'inspect',selector:'button[data-member]'}));checkpoint('Rest log',await browser({op:'inspect',selector:'[data-combat-log]'}));
