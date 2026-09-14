await keyboard.hold(['P'],60); await sleep(100);
await keyboard.hold(['E'],80); await sleep(300);
checkpoint('Facing raised passage',await capture({label:'bridge-approach'}));
for(let i=0;i<5;i++) {
 await keyboard.hold(['W'],70); await sleep(260);
 checkpoint('Bridge step '+i,await capture({label:'bridge-step-'+i}));
}
await keyboard.hold(['P'],60);await sleep(250);
checkpoint('Bridge end paused',await capture({label:'bridge-end'}));
