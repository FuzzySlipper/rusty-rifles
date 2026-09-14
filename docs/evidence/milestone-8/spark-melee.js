async function press(s){await browser({op:'press',selector:s,key:'Enter'});await sleep(230);}
try{
await press('button:text-is("Resume")');
const v=await browser({op:'inspect',selector:'button[data-target]'});const t=v.targets.find(t=>!t.disabled&&t.text.includes('raider'));
if(t){await press('button[data-target]:text-is('+JSON.stringify(t.text)+')');
await press('button[data-member="seeker"]');await press('[data-spell-shortcut="spark"]');await press('button:text-is("Cast")');await sleep(1350);
checkpoint('Spell hit',await browser({op:'inspect',selector:'[data-combat-log]'}));
await press('button[data-member="blade"]');for(let i=0;i<3;i++){await press('button:text-is("Attack [Space]")');await sleep(1100);}}
}finally{const v=await browser({op:'inspect',selector:'button'});if(v.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');}
checkpoint('After raider',await capture({label:'after-raider'}));checkpoint('Log',await browser({op:'inspect',selector:'[data-combat-log]'}));
