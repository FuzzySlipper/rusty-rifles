async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(300);}
await press('button[data-definition="key"]:not([data-owner^="member:"])');await press('[data-owner-destination="member:blade"]');
await press('button[data-definition="weight"]:not([data-owner^="member:"])');await press('[data-owner-destination="member:blade"]');await press('button:text-is("Save")');
checkpoint('pickup',await browser({op:'inspect',selector:'[data-inventory-feedback],button[data-definition="key"],button[data-definition="weight"]'}));
