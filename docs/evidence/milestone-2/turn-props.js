await input([{kind:'point',x:800,y:450,width:1280,height:720},{kind:'click',button:1,ms:80}]);
await keyboard.hold(['E'],100);
await sleep(300);
checkpoint('east-grounded',await capture({label:'east-grounded'}));
await keyboard.hold(['E'],100);
await sleep(300);
checkpoint('south-props',await capture({label:'south-props'}));
