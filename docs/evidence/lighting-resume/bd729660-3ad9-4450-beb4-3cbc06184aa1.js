await browser({op:'press',selector:'summary:text-is("Art comparison")',key:'Enter'});
await browser({op:'press',selector:'button:text-is("Toggle room lights")',key:'Enter'});await sleep(600);
checkpoint('lantern-only',await capture({label:'lantern-only'}));
await browser({op:'press',selector:'button:text-is("Move light")',key:'Enter'});await sleep(600);
checkpoint('light-left',await capture({label:'light-left'}));
await browser({op:'press',selector:'button:text-is("Move light")',key:'Enter'});await sleep(600);
checkpoint('light-right',await capture({label:'light-right'}));
await browser({op:'press',selector:'button:text-is("Switch treatment")',key:'Enter'});await sleep(1000);
checkpoint('painted',await capture({label:'painted'}));
