async function press(s){await browser({op:'press',selector:s,key:'Enter'});await sleep(220);}
try{
await press('button:text-is("Resume")');await press('canvas');await keyboard.hold(['E'],100);await sleep(300);
const v=await browser({op:'inspect',selector:'button[data-target]'});checkpoint('Targets',v);const t=v.targets.find(t=>!t.disabled&&t.text.includes('runner'))??v.targets.find(t=>!t.disabled);
if(t){
await press('button[data-target]:text-is('+JSON.stringify(t.text)+')');await press('button[data-member="warden"]');await press('button:text-is("Attack [Space]")');await sleep(1400);
await press('button[data-member="seeker"]');await press('[data-spell-shortcut="spark"]');await press('button:text-is("Cast")');await sleep(1400);
await press('button[data-member="blade"]');await press('button:text-is("Attack [Space]")');await sleep(1300);
}
}finally{const v=await browser({op:'inspect',selector:'button'});if(v.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');}
checkpoint('Combat',await capture({label:'guard-combat'}));checkpoint('Log',await browser({op:'inspect',selector:'[data-combat-log]'}));checkpoint('Party',await browser({op:'inspect',selector:'button[data-member]'}));
