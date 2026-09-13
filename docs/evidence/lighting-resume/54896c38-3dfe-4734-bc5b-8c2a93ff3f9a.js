await input([{kind:'hold',keys:[9],ms:70},{kind:'hold',keys:[9],ms:70},{kind:'hold',keys:[9],ms:70},{kind:'hold',keys:[13],ms:100}]);await sleep(700);
checkpoint('gpu-lantern',await capture({label:'gpu-lantern'}));
await input([{kind:'hold',keys:[16,9],ms:80},{kind:'hold',keys:[13],ms:100}]);await sleep(700);
checkpoint('gpu-left',await capture({label:'gpu-left'}));
await input([{kind:'hold',keys:[13],ms:100}]);await sleep(700);
checkpoint('gpu-right',await capture({label:'gpu-right'}));
await input([{kind:'hold',keys:[16,9],ms:80},{kind:'hold',keys:[13],ms:100}]);await sleep(1000);
checkpoint('gpu-painted',await capture({label:'gpu-painted'}));
