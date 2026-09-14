async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(650);}
checkpoint('restored reward',await browser({op:'inspect',selector:'[data-run-status],[data-member-development],[data-run-completion-problem]'}));
checkpoint('restored reward original',await capture({label:'restored-reward'}));
await press('[data-run-new] summary');await browser({op:'fill',selector:'[data-run-seed]',value:'314159'});await browser({op:'press',selector:'[data-run-difficulty]',key:'End'});await sleep(500);await press('[data-run-new-button]');await sleep(2500);
const b=await browser({op:'inspect',selector:'button'});if(b.targets.some(t=>t.text==='Pause'))await press('button:text-is("Pause")');
checkpoint('hard new run',await browser({op:'inspect',selector:'[data-inventory-status],[data-run-profile],[data-member-development],button[data-member]'}));checkpoint('hard run',await capture({label:'hard-new-seed'}));
await press('[data-run-load]');await sleep(2500);
checkpoint('prior completion preserved',await browser({op:'inspect',selector:'[data-run-status],[data-member-development],[data-run-profile]'}));
checkpoint('prior saved ending',await capture({label:'prior-ending-preserved'}));
