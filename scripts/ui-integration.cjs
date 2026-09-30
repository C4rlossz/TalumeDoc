// DOM-level UI integration against the real local API. Does not replace a visual browser review.
const { JSDOM } = require('jsdom');
const { spawn } = require('node:child_process');
const fs = require('node:fs'); const os = require('node:os'); const path = require('node:path'); const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..'); const data = fs.mkdtempSync(path.join(os.tmpdir(), 'talumedoc-ui-')); const base = 'http://127.0.0.1:5089';
const server = spawn(process.env.DOTNET || 'dotnet', ['run', '--project', 'src/TalumeDoc.Api', '--no-build', '--no-launch-profile'], { cwd: root,
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', StorageMode: 'Local', DataDirectory: data, ASPNETCORE_URLS: base }, stdio: 'ignore' });
(async () => {
 let dom;
 try {
  for (let i=0;i<100;i++) { try { if((await fetch(base+'/health')).ok) break; } catch {} await new Promise(r=>setTimeout(r,100)); }
  dom = new JSDOM(await (await fetch(base)).text(), { url: base, runScripts: 'outside-only', pretendToBeVisual: true });
  const w = dom.window;
  w.fetch = (url, options) => fetch(new URL(url, base), options); w.Headers=Headers; w.FormData=FormData; w.File=File; w.Blob=Blob; w.TextEncoder=TextEncoder;
  w.HTMLDialogElement.prototype.showModal = function(){ this.open=true; }; w.HTMLDialogElement.prototype.close=function(){ this.open=false; };
  w.eval(fs.readFileSync(path.join(root,'src/TalumeDoc.Api/wwwroot/theme.js'),'utf8'));
  await w.eval('(async()=>{'+fs.readFileSync(path.join(root,'src/TalumeDoc.Api/wwwroot/app.js'),'utf8')+'})()');
  const wait = async fn => { for(let i=0;i<100;i++){if(fn()) return; await new Promise(r=>setTimeout(r,100));}throw new Error('UI timeout: '+w.document.getElementById('toast').textContent); };
  w.document.getElementById('sample-button').click();
  await wait(()=>w.document.querySelector('.row-action'));
  assert.equal(w.document.getElementById('stat-total').textContent,'1');
  assert.equal(w.document.getElementById('stat-attention').textContent,'1');
  w.document.querySelector('.row-action').click(); await wait(()=>w.document.getElementById('report-dialog').open);
  assert.match(w.document.getElementById('report-body').textContent,/DUPLICATE/);
  assert.match(w.document.getElementById('report-body').textContent,/QUANTITY/);
  const search=w.document.getElementById('search-input'); search.value='naoexiste';search.dispatchEvent(new w.Event('input'));
  assert.equal(w.document.querySelectorAll('#document-rows tr').length,0);
  console.log('UI integration passed: live API, sample upload, async processing, real counters, report and search.');
 } finally { if(dom)dom.window.close();server.kill();await new Promise(r=>server.once('exit',r));fs.rmSync(data,{recursive:true,force:true}); }
})().catch(e=>{console.error(e);process.exitCode=1;});
