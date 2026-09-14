async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(260);}
await press('button:text-is("Resume")');
try {await press('button[data-member="blade"]');
for(let i=0;i<6;i++){const r=await browser({op:'inspect',selector:'button[data-target]'});const t=r.targets.find(t=>!t.disabled&&t.text.includes('bulwark'));if(!t)break;await press('button[data-target]:text-is('+JSON.stringify(t.text)+')');await press('button:text-is("Attack [Space]")');await sleep(1100);}
}finally {await press('button:text-is("Pause")');}
checkpoint('fight',await browser({op:'inspect',selector:'button[data-target],button[data-member],[data-combat-log]'}));
checkpoint('bulwark',await capture({label:'redoubt-bulwark'}));
