await browser({op:'press',selector:'button:has-text("Save")',key:'Enter'});await sleep(400);
checkpoint('saved',await capture({label:'m3-saved'}));
await browser({op:'press',selector:'button:text-is("Restart")',key:'Enter'});await sleep(700);
checkpoint('restarted',await capture({label:'m3-restarted'}));
await browser({op:'press',selector:'button:text-is("Load")',key:'Enter'});await sleep(700);
checkpoint('loaded',await capture({label:'m3-loaded'}));
