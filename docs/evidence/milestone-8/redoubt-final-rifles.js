async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(260);}
await press('button:text-is("Resume")');
try {await press('button[data-member="warden"]');await press('button:text-is("Reload [T]")');await sleep(2800);
for(let i=0;i<4;i++){const r=await browser({op:'inspect',selector:'button[data-target]'});const t=r.targets.find(t=>!t.disabled);if(t){await press('button[data-target]:text-is('+JSON.stringify(t.text)+')');await press('button:text-is("Attack [Space]")');await sleep(1400);break;}await browser({op:'press',selector:'canvas',key:'Shift'});await keyboard.hold(['E'],80);await sleep(800);}
}finally {await press('button:text-is("Pause")');}
checkpoint('last foe',await browser({op:'inspect',selector:'[data-inventory-status],button[data-target],button[data-member],[data-combat-log]'}));checkpoint('last rifle',await capture({label:'last-rifle'}));
