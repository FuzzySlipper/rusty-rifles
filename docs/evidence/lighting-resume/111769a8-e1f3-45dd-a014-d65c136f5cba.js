await input([{kind:'hold',keys:[13],ms:100}]);await sleep(4000);
checkpoint('painted-settled',await capture({label:'painted-settled'}));
await input([{kind:'point',x:1000,y:600,width:1280,height:720},{kind:'click',button:1,ms:100},{kind:'hold',keys:[83],ms:150}]);await sleep(2000);
checkpoint('painted-distance',await capture({label:'painted-distance'}));
