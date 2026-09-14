async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(300);}
await press('button:text-is("Resume")');
try {await browser({op:'press',selector:'canvas',key:'Shift'});await keyboard.hold(['E'],100);await sleep(800);await press('button:text-is("Use feature")');await sleep(600);}
finally{await press('button:text-is("Pause")');}
await press('button:text-is("Save")');
checkpoint('gate',await browser({op:'inspect',selector:'[data-inventory-status],[data-inventory-feedback]'}));
checkpoint('final gate',await capture({label:'redoubt-open-gate'}));
