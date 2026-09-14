async function press(s){await browser({op:'press',selector:s,key:'Enter'});await sleep(250);}
try{
await press('button:text-is("Resume")');await press('button[data-member="mender"]');await press('[data-spell-select="mend"]');
for(let i=0;i<2;i++){await press('button:text-is("Cast")');await sleep(1500);}
checkpoint('Recovery',await browser({op:'inspect',selector:'button[data-member]'}));
await press('[data-run-travel-choice="supply-descent"]');
for(let i=0;i<20;i++){await sleep(300);const v=await browser({op:'inspect',selector:'[data-run-location]'});if(v.targets.some(t=>t.text.includes('Flooded Stores')))break;}
}finally{const v=await browser({op:'inspect',selector:'button'});if(v.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');}
checkpoint('Stores',await capture({label:'stores-arrival'}));checkpoint('Location',await browser({op:'inspect',selector:'[data-inventory-status]'}));checkpoint('Feedback',await browser({op:'inspect',selector:'[data-run-location]'}));
