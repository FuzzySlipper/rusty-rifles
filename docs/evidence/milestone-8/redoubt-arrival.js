async function press(s){await browser({op:'press',selector:s,key:'Enter'});await sleep(260);}
await press('button:text-is("Save")');
try{
await press('button:text-is("Resume")');await press('[data-run-travel-choice="redoubt-descent"]');
for(let i=0;i<20;i++){await sleep(250);const v=await browser({op:'inspect',selector:'[data-run-location]'});if(v.targets.some(t=>t.text.includes('Inner Redoubt')))break;}
}finally{const v=await browser({op:'inspect',selector:'button'});if(v.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');}
checkpoint('Redoubt arrival',await capture({label:'redoubt-arrival'}));checkpoint('Position',await browser({op:'inspect',selector:'[data-inventory-status]'}));checkpoint('Run',await browser({op:'inspect',selector:'[data-run-location]'}));
