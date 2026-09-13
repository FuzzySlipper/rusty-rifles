await browser({op:'press',selector:'button:text-is("Use nearby lever")',key:'Enter'});await sleep(600);
checkpoint('opened',await capture({label:'m3-door-opened'}));
await input([{kind:'point',x:1000,y:600,width:1280,height:720},{kind:'click',button:1,ms:50},{kind:'hold',keys:[87],ms:100},{kind:'wait',ms:500},{kind:'hold',keys:[87],ms:100}]);await sleep(600);
checkpoint('inside-door',await capture({label:'m3-inside-door'}));
await input([{kind:'hold',keys:[83],ms:100}]);await sleep(600);
await browser({op:'press',selector:'button:text-is("Use nearby lever")',key:'Enter'});await sleep(500);
await input([{kind:'point',x:1000,y:600,width:1280,height:720},{kind:'click',button:1,ms:50},{kind:'hold',keys:[87],ms:100}]);await sleep(600);
checkpoint('closed-blocks',await capture({label:'m3-door-closed-blocks'}));
