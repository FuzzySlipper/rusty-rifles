await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(300);
await browser({op:'press',selector:'button:text-is("Use feature")',key:'Enter'});await sleep(400);
checkpoint('Corrected use button',await browser({op:'inspect',selector:'p'}));
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});
