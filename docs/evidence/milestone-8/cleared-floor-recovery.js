await browser({op:'press',selector:'[data-run-load]',key:'Enter'});await sleep(1000);
checkpoint('loaded ending checkpoint',await browser({op:'inspect',selector:'[data-inventory-status],button[data-target],[data-run-status]'}));
checkpoint('restored cleared floor',await capture({label:'restored-cleared-floor'}));
