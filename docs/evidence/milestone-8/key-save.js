async function press(s){await browser({op:'press',selector:s,key:'Enter'});await sleep(350);}
await press('button[data-definition="key"]');await press('[data-owner-destination="member:blade"]');
await press('button[data-definition="weight"]');await press('[data-owner-destination="member:blade"]');
await press('button:text-is("Save")');await sleep(500);
checkpoint('Key and weight carried',await capture({label:'key-and-weight'}));
checkpoint('Blade items',await browser({op:'inspect',selector:'button[data-inventory-item][data-owner="member:blade"]'}));
await press('[data-run-restart]');await sleep(1200);await press('button:text-is("Pause")');
await press('[data-run-load]');await sleep(1800);
checkpoint('Run restored',await capture({label:'stores-save-restored'}));checkpoint('Location',await browser({op:'inspect',selector:'[data-inventory-status]'}));
checkpoint('Blade restored',await browser({op:'inspect',selector:'button[data-inventory-item][data-owner="member:blade"]'}));
