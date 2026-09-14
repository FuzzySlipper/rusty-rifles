await browser({op:'press',selector:'button:text-is("Save")',key:'Enter'});await sleep(300);
checkpoint('Secret saved',await capture({label:'secret-saved'}));
await browser({op:'press',selector:'button:text-is("Restart")',key:'Enter'});await sleep(350);
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(350);
checkpoint('Restart before load',await capture({label:'secret-restart'}));
await browser({op:'press',selector:'button:text-is("Load")',key:'Enter'});await sleep(1000);
checkpoint('Secret restored',await capture({label:'secret-restored'}));
