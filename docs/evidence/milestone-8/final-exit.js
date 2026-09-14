async function press(selector){await browser({op:'press',selector,key:'Enter'});await sleep(280);}
async function pos(){const r=await browser({op:'inspect',selector:'[data-inventory-status]'});const m=r.targets[0].text.match(/\((\d+), (\d+)\)/);return [Number(m[1]),Number(m[2])];}
await press('button:text-is("Resume")');
try {await browser({op:'press',selector:'canvas',key:'Shift'});
const route=[[100, 112], [101, 112], [102, 112], [103, 112], [104, 112], [105, 112], [106, 112], [107, 112], [108, 112], [109, 112], [110, 112], [111, 112], [112, 112], [113, 112], [114, 112], [115, 112], [116, 112], [117, 112], [118, 112], [118, 111], [118, 110], [118, 109], [118, 108], [118, 107], [118, 106], [118, 105], [118, 104], [118, 103], [118, 102], [118, 101], [118, 100], [118, 99], [118, 98], [118, 97], [118, 96], [117, 96], [117, 95], [116, 95], [116, 94], [116, 93], [116, 92], [115, 92], [115, 91], [114, 91], [114, 90], [113, 90], [113, 89], [113, 88], [113, 87], [113, 86], [113, 85]];
for(const n of route){const p=await pos();const dx=n[0]-p[0],dy=n[1]-p[1];if(dx===0&&dy===0)continue;if(Math.abs(dx)+Math.abs(dy)!==1){checkpoint('unexpected position',p);break;}const k=dx===1?'D':dx===-1?'A':dy===1?'S':'W';await keyboard.hold([k],80);await sleep(800);const a=await pos();if(a[0]!==n[0]||a[1]!==n[1]){checkpoint('blocked route',{expected:n,actual:a});break;}}
}finally {await press('button:text-is("Pause")');}
checkpoint('route end',await browser({op:'inspect',selector:'[data-inventory-status],button[data-target],button[data-member]'}));
checkpoint('route view',await capture({label:'redoubt-route'}));
