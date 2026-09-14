async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(900);}
await browser({op:'fill',selector:'[data-run-seed]',value:'314159'});await browser({op:'press',selector:'[data-run-difficulty]',key:'Home'});await sleep(500);await press('[data-run-new-button]');await sleep(2500);
const b=await browser({op:'inspect',selector:'button'});if(b.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');
checkpoint('hard profile',await browser({op:'inspect',selector:'[data-inventory-status],[data-run-profile]'}));
await press('button:text-is("Save")');await press('[data-run-restart]');await sleep(2500);await press('[data-run-load]');await sleep(2500);
checkpoint('hard restored',await browser({op:'inspect',selector:'[data-inventory-status],[data-run-profile],[data-member-development],button[data-member],[data-inventory-feedback]'}));
checkpoint('hard restored original',await capture({label:'hard-profile-restored'}));
