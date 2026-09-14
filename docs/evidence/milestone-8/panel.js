await sleep(7000);
await browser({op:'press',selector:'[data-run-restart]',key:'Enter'});await sleep(1800);
await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(350);
await browser({op:'press',selector:'[data-run-map] summary',key:'Enter'});
checkpoint('Start',await capture({label:'m8-final-start'}));
checkpoint('UI',await browser({op:'inspect',selector:'aside[aria-label="Expedition run"]'}));
