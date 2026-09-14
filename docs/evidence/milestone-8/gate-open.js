await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(300);
await browser({op:'press',selector:'canvas',key:'Shift'});await keyboard.hold(['F'],100);await sleep(400);
checkpoint('Gate keyboard result',await browser({op:'inspect',selector:'p'}));checkpoint('Gate keyboard',await capture({label:'gate-keyboard'}));
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(300);
await browser({op:'press',selector:'button:text-is("Save")',key:'Enter'});await sleep(300);
