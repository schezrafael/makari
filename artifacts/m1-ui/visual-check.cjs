const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
(async () => {
const browser = await chromium.launch({channel:'chrome', headless:true});
const page = await browser.newPage();
const id='11111111-1111-4111-8111-111111111111';
let mode='normal', posts=[], gets=0, priceChanged=false;
// Isolated transport fixtures only; the application continues to consume its HTTP contracts.
await page.route('**/api/**', async route => {
 const url=route.request().url(), req=route.request().postDataJSON();
 const send=(body,status=200)=>route.fulfill({status,json:body});
 if(mode==='error') return send({detail:'Servicio no disponible'},503);
 if(url.includes('catalog')) return send({products:mode==='empty'?[]:[{id,name:'Bebida de prueba',category:'Catálogo de prueba',variants:[{id,name:'Regular',sizeMl:500,price:60}]}]});
 if(url.includes('/summary')) { gets++; return send({date:'2026-10-28',timeZoneId:'America/Mexico_City',salesCount:2,totalRevenue:110,cashRevenue:50,transferRevenue:60,unitsSold:2,variants:[{productVariantId:id,productName:'Bebida de prueba',variantName:'Regular',quantity:2,revenue:110}]}); }
 if(url.includes('cash-closes')) {gets++; return send({code:'cash_close_not_found'},404);}
 if(url.includes('/quote')) {
 const unit=req.customerType==='Local'?(priceChanged?55:50):60;
 const items=req.items.map(i=>({...i,visitorUnitPrice:60,unitPrice:unit,subtotal:60*i.quantity,discount:(60-unit)*i.quantity,total:unit*i.quantity}));
 return send({...req,items,subtotal:items.reduce((a,i)=>a+i.subtotal,0),discount:items.reduce((a,i)=>a+i.discount,0),total:items.reduce((a,i)=>a+i.total,0)});
 }
 posts.push(req);
 if(mode==='price') {mode='normal';priceChanged=true;return send({code:'price_changed',currentTotal:55,idempotencyKey:req.idempotencyKey},409);}
 if(mode==='offline') return route.abort();
 return send({...req,id,createdAtUtc:'2026-10-28T12:00:00Z',registeredBy:'Prueba',total:req.expectedTotal,items:req.items.map(i=>({...i,id,unitPrice:req.expectedTotal/i.quantity,subtotal:req.expectedTotal}))},201);
});
await page.goto('http://127.0.0.1:5173');
await page.getByRole('button',{name:'Agregar Bebida de prueba, Regular',exact:true}).click();
await page.getByRole('checkbox').check();
await page.getByRole('button',{name:'Registrar venta',exact:true}).waitFor();
await page.waitForFunction(()=>!document.querySelector('.checkout button').disabled);
assert.match(await page.locator('.checkout strong').innerText(),/50.00/);
for(const [name,width,height] of [['desktop',1440,1000],['mobile',390,844]]) {
 await page.setViewportSize({width,height});
 for(const [tab,file] of [['Nueva venta','sale'],['Ventas del día','daily'],['Cierre de caja','close']]) {
  await page.getByRole('button',{name:tab,exact:true}).click();
  await page.waitForTimeout(500);
  if(tab==='Cierre de caja') {
   const before=gets;
   await page.getByRole('textbox',{name:'Fondo inicial en pesos'}).fill('300');
   await page.getByRole('textbox',{name:'Efectivo contado en pesos'}).fill('350');
   assert.equal(gets,before);
  }
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,`${tab} overflow`);
  await page.locator('h1').click();
  await page.evaluate(()=>window.scrollTo(0,0));
  await page.screenshot({path:`./artifacts/m1-ui/${file}-${name}.png`,fullPage:true});
 }
}
await page.getByRole('button',{name:'Nueva venta',exact:true}).click();
await page.getByRole('button',{name:'Registrar venta',exact:true}).click();
mode='price';
await page.getByRole('button',{name:'Confirmar y registrar',exact:true}).click();
await page.waitForTimeout(600);
assert.match(await page.locator('body').innerText(),/El importe cambió/i);
await page.getByRole('button',{name:'Registrar venta',exact:true}).click();
mode='offline';
await page.getByRole('button',{name:'Confirmar y registrar',exact:true}).click();
await page.getByText('Operación pendiente de resolver').waitFor();
await page.reload();
await page.getByRole('button',{name:'Recuperar venta',exact:true}).waitFor();
mode='normal';
await page.getByRole('button',{name:'Recuperar venta',exact:true}).click();
await page.getByText('Venta registrada',{exact:true}).waitFor();
assert.deepEqual(posts[1],posts[2]);
mode='empty'; await page.reload(); await page.getByText('No hay bebidas disponibles.').waitFor();
mode='error'; await page.reload(); await page.getByRole('button',{name:'Reintentar catálogo'}).waitFor();
await page.screenshot({path:'./artifacts/m1-ui/error-mobile.png',fullPage:true});
console.log('PASS: navigation, desktop/mobile overflow, local discount, explicit confirmation, HTTP 409, pending retry with same request, cash inputs without GET, empty/error states.');
await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
