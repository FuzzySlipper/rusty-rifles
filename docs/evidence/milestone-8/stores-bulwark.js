async function press(s){await browser({op:'press',selector:s,key:'Enter'});await sleep(230);}
try{
await press('button:text-is("Resume")');await press('button[data-member="warden"]');await press('button:text-is("Reload [T]")');
await press('button[data-member="blade"]');
for(let i=0;i<4;i++){
const v=await browser({op:'inspect',selector:'button[data-target]'});const t=v.targets.filter(t=>!t.disabled).sort((a,b)=>Number(a.text.match(/· (\d+)/)?.[1])-Number(b.text.match(/· (\d+)/)?.[1]))[0];
if(!t)break;await press('button[data-target]:text-is('+JSON.stringify(t.text)+')');await press('button:text-is("Attack [Space]")');await sleep(1100);
if(i===1){await press('button[data-member="warden"]');await press('button:text-is("Attack [Space]")');await sleep(1100);await press('button[data-member="blade"]');}
}
}finally{const v=await browser({op:'inspect',selector:'button'});if(v.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');}
checkpoint('Stores combat',await capture({label:'stores-combat'}));checkpoint('Log',await browser({op:'inspect',selector:'[data-combat-log]'}));checkpoint('Position',await browser({op:'inspect',selector:'[data-inventory-status]'}));checkpoint('Targets',await browser({op:'inspect',selector:'button[data-target]'}));checkpoint('Party',await browser({op:'inspect',selector:'button[data-member]'}));
