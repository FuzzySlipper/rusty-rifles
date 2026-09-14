await browser({op:'press',selector:'button:text-is("Resume")',key:'Enter'});await sleep(500);
try{await browser({op:'press',selector:'canvas',key:'Shift'});for(const k of ['S','S','D','D','W','D','W','D','D','D']){await keyboard.hold([k],60);await sleep(800);}}
finally{await browser({op:'press',selector:'button:text-is("Pause")',key:'Enter'});await sleep(400);}
checkpoint('fresh route',await browser({op:'inspect',selector:'[data-inventory-status],[data-run-profile],button[data-target]'}));checkpoint('fresh route view',await capture({label:'fresh-route'}));
