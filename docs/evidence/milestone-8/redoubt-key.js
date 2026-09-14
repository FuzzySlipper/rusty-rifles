async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(260);}
await press('button:text-is("Resume")');
try {await press('button[data-member="mender"]');await press('[data-spell-select="mend"]');for(let i=0;i<2;i++){await press('button:text-is("Cast")');await sleep(1400);}
await browser({op:'press',selector:'canvas',key:'Shift'});for(let i=0;i<5;i++){await keyboard.hold(['W'],100);await sleep(600);}
}finally{await press('button:text-is("Pause")');}
checkpoint('key cache',await browser({op:'inspect',selector:'[data-inventory-status],button[data-definition="key"],button[data-definition="weight"],button[data-member]'}));
checkpoint('cache',await capture({label:'redoubt-key-cache'}));
