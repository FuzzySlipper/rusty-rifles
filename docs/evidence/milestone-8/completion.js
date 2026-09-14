async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(550);}
await press('button:text-is("Resume")');await press('[data-run-complete]');
await press('summary:text-is("Spells & recovery")');
checkpoint('completed facts',await browser({op:'inspect',selector:'[data-run-status],[data-run-result],[data-member-development],[data-run-completion-problem],[data-inventory-feedback]'}));
checkpoint('completed expedition',await capture({label:'completed-expedition'}));
await press('button:text-is("Save")');await press('[data-run-restart]');await press('[data-run-load]');
checkpoint('restored completion',await browser({op:'inspect',selector:'[data-run-status],[data-run-result],[data-member-development],[data-run-completion-problem],[data-inventory-feedback]'}));
checkpoint('completed reload',await capture({label:'completed-run-reload'}));
