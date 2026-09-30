const { chromium } = require('playwright');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const data = fs.mkdtempSync(path.join(os.tmpdir(), 'talumedoc-browser-'));
const port = process.env.TEST_PORT || '5087';
const base = `http://127.0.0.1:${port}`;
const server = spawn(process.env.DOTNET || 'dotnet', ['run', '--project', 'src/TalumeDoc.Api', '--no-build', '--no-launch-profile'], {
  cwd: root, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', StorageMode: 'Local', DataDirectory: data, ASPNETCORE_URLS: base }, stdio: ['ignore', 'pipe', 'pipe']
});
let log = ''; server.stdout.on('data', x => log += x); server.stderr.on('data', x => log += x);
(async () => {
  let browser;
  try {
    for (let i = 0; i < 100; i++) { try { if ((await fetch(base + '/health')).ok) break; } catch {} await new Promise(r => setTimeout(r, 100)); }
    browser = await chromium.launch({ headless: true, ...(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {}) });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1120 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    await page.goto(base); await page.locator('#connection').filter({ hasText: 'Ambiente local' }).waitFor();
    await page.locator('#sample-button').click();
    await page.locator('.row-action').filter({ hasText: 'Ver relatório' }).waitFor({ timeout: 15000 });
    assert.equal(await page.locator('#stat-total').textContent(), '1');
    assert.equal(await page.locator('#stat-attention').textContent(), '1');
    fs.mkdirSync(path.join(root, 'docs/images'), { recursive: true });
    await page.screenshot({ path: path.join(root, 'docs/images/dashboard.png'), fullPage: true });
    await page.locator('.row-action').click(); await page.locator('#report-dialog').waitFor();
    assert.match(await page.locator('#report-body').textContent(), /DUPLICATE/);
    const download = page.waitForEvent('download'); await page.locator('#download-report').click(); assert.match((await download).suggestedFilename(), /erros\.csv$/);
    await page.screenshot({ path: path.join(root, 'docs/images/report.png'), fullPage: true });
    await page.locator('#close-report').click();
    await page.locator('#search-input').fill('inexistente'); assert.equal(await page.locator('#document-rows tr').count(), 0);
    await page.locator('#search-input').fill('');
    await page.locator('#file-input').setInputFiles(path.join(root, 'samples/produtos-validos.csv'));
    await page.waitForFunction(() => document.getElementById('stat-total').textContent === '2');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: path.join(root, 'docs/images/mobile.png'), fullPage: true });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, 'Mobile layout overflows');
    assert.deepEqual(errors, []);
    console.log('Browser smoke passed: upload, processing, report, download, search and mobile layout.');
  } finally {
    if (browser) await browser.close();
    server.kill(); await new Promise(r => server.once('exit', r)); fs.rmSync(data, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); console.error(log); process.exitCode = 1; });
