const $ = (id) => document.getElementById(id);
const state = { config: null, documents: [], reportId: null, uploading: false, refreshing: false };
const labels = { Queued: 'Na fila', Processing: 'Processando', Completed: 'Concluído', Rejected: 'Rejeitado', Failed: 'Falha' };
let toastTimer;
function toast(message, error = false) { $('toast').textContent = message; $('toast').className = `toast${error ? ' error' : ''}`; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => $('toast').hidden = true, 6000); }
function session() { try { const s = JSON.parse(sessionStorage.getItem('talumedoc-session')); return s?.expiresAt > Date.now() ? s : null; } catch { return null; } }
function connected() { return state.config?.local || session(); }
async function api(path, options = {}) {
  const headers = new Headers(options.headers);
  if (!state.config.local) {
    const s = session();
    if (!s) throw new Error('Entre na sua conta para continuar.');
    headers.set('Authorization', `Bearer ${s.accessToken}`);
  }
  const response = await fetch(path, { ...options, headers });
  if (!response.ok) {
    if (response.status === 401) { sessionStorage.removeItem('talumedoc-session'); updateAuth(); throw new Error('Sua sessão expirou. Entre novamente.'); }
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || (response.status === 429 ? 'Muitos envios. Aguarde um minuto e tente novamente.' : 'Não foi possível concluir a operação. Tente novamente.'));
  }
  return response;
}
function updateAuth() {
  const local = state.config.local;
  $('connection').textContent = local ? 'Ambiente local' : connected() ? 'Workspace conectado' : 'Conta desconectada';
  $('auth-button').textContent = local ? 'Demonstração local' : connected() ? 'Sair' : 'Entrar na conta';
  $('auth-button').disabled = local;
  if (local) { $('notice').textContent = 'Você está no ambiente local: arquivos salvos neste computador, com um usuário de demonstração. Na AWS, cada conta tem seu próprio workspace.'; $('notice').hidden = false; }
  else if (!connected()) { $('notice').textContent = 'Entre ou crie sua conta para enviar documentos e acompanhar seus relatórios.'; $('notice').hidden = false; }
  else $('notice').hidden = true;
}
const base64url = bytes => btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
async function login() {
  const verifier = base64url(crypto.getRandomValues(new Uint8Array(48)));
  const csrf = base64url(crypto.getRandomValues(new Uint8Array(32)));
  const challenge = base64url(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier))));
  sessionStorage.setItem('talumedoc-oauth', JSON.stringify({ verifier, state: csrf, at: Date.now() }));
  const params = new URLSearchParams({ response_type: 'code', client_id: state.config.clientId, redirect_uri: location.origin + '/',
    scope: 'openid email profile', state: csrf, code_challenge: challenge, code_challenge_method: 'S256' });
  location.assign(`${state.config.domain}/oauth2/authorize?${params}`);
}
async function authCallback() {
  const params = new URLSearchParams(location.search);
  if (!params.has('code') && !params.has('error')) return;
  history.replaceState({}, '', '/');
  const stored = sessionStorage.getItem('talumedoc-oauth');
  sessionStorage.removeItem('talumedoc-oauth');
  const pending = stored ? JSON.parse(stored) : null;
  if (!pending || params.get('state') !== pending.state || Date.now() - pending.at > 600000) throw new Error('Sessão de login inválida ou expirada. Entre novamente.');
  if (params.has('error')) throw new Error('O login não foi concluído. Tente novamente.');
  const response = await fetch(`${state.config.domain}/oauth2/token`, { method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ grant_type: 'authorization_code', code: params.get('code'), client_id: state.config.clientId,
      redirect_uri: location.origin + '/', code_verifier: pending.verifier }) });
  if (!response.ok) throw new Error('Não foi possível concluir o login.');
  const token = await response.json();
  sessionStorage.setItem('talumedoc-session', JSON.stringify({ accessToken: token.access_token, expiresAt: Date.now() + token.expires_in * 1000 - 30000 }));
}
function logout() {
  sessionStorage.removeItem('talumedoc-session');
  const params = new URLSearchParams({ client_id: state.config.clientId, logout_uri: location.origin + '/' });
  location.assign(`${state.config.domain}/logout?${params}`);
}
function element(tag, className, text) { const e = document.createElement(tag); if (className) e.className = className; if (text !== undefined) e.textContent = text; return e; }
function attention(d) { return d.invalidRows > 0 || ['Rejected', 'Failed'].includes(d.status); }
function render() {
  const documents = state.documents;
  $('stat-total').textContent = documents.length;
  $('stat-processing').textContent = documents.filter(d => ['Queued', 'Processing'].includes(d.status)).length;
  $('stat-completed').textContent = documents.filter(d => d.status === 'Completed').length;
  $('stat-attention').textContent = documents.filter(attention).length;
  $('nav-count').textContent = $('document-count').textContent = documents.length;
  const search = $('search-input').value.toLocaleLowerCase('pt-BR');
  const filter = $('status-filter').value;
  const visible = documents.filter(d => d.fileName.toLocaleLowerCase('pt-BR').includes(search) &&
    (filter === 'all' || (filter === 'attention' ? attention(d) : d.status === filter)));
  const rows = $('document-rows'); rows.replaceChildren();
  for (const d of visible) {
    const row = element('tr');
    const first = element('td'); const file = element('div', 'file-cell'); file.append(element('span', 'file-icon', 'CSV'));
    const info = element('div'); const name = element('div', 'file-name', d.fileName); name.title = d.fileName;
    info.append(name, element('small', 'file-size', `${(d.sizeBytes / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} KB`)); file.append(info); first.append(file); row.append(first);
    const status = element('td'); status.append(element('span', `badge ${d.status}`, labels[d.status] || d.status)); row.append(status);
    const count = element('td', '', d.hasReport ? d.totalRows.toLocaleString('pt-BR') : '—');
    if (d.invalidRows > 0) count.append(element('small', 'invalid-count', `${d.invalidRows} para revisar`)); row.append(count);
    row.append(element('td', '', new Date(d.createdAt).toLocaleString('pt-BR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })));
    const action = element('td');
    if (d.hasReport) { const button = element('button', 'row-action', 'Ver relatório ↗'); button.addEventListener('click', () => report(d)); action.append(button); }
    else if (d.status === 'Failed') { const button = element('button', 'row-action', 'Tentar novamente'); button.addEventListener('click', () => retry(d)); action.append(button); }
    else action.append(element('span', '', '—'));
    row.append(action); rows.append(row);
  }
  $('empty-state').hidden = visible.length > 0;
  $('empty-state').querySelector('h3').textContent = documents.length ? 'Nenhum arquivo corresponde à sua busca.' : 'Seu próximo arquivo começa uma história.';
  $('empty-state').querySelector('p').textContent = documents.length ? 'Ajuste o nome ou o filtro de status para encontrar seu documento.' : 'Envie seu primeiro CSV ou experimente um exemplo para conhecer o TalumeDoc.';
  $('empty-sample').hidden = documents.length > 0;
  $('list-summary').textContent = `${visible.length} de ${documents.length} documentos · até 200 envios recentes`;
}
async function refresh(silent = false) {
  if (!connected() || state.refreshing) return;
  state.refreshing = true;
  try { state.documents = await (await api('/api/documents/')).json(); render(); }
  catch (error) { if (!silent) toast(error.message, true); }
  finally { state.refreshing = false; }
}
async function upload(file) {
  if (!file || state.uploading) return;
  if (!connected()) { toast('Entre na sua conta antes de enviar um arquivo.', true); return; }
  if (!file.name.toLowerCase().endsWith('.csv') || file.size === 0 || file.size > state.config.maxBytes) { toast('Selecione um CSV não vazio de até 2 MiB.', true); return; }
  state.uploading = true; $('file-input').disabled = true;
  const heading = $('drop-zone').querySelector('h3'); heading.textContent = 'Enviando seu arquivo…';
  try {
    const form = new FormData(); form.append('file', file);
    await api('/api/documents/', { method: 'POST', body: form });
    toast('Arquivo enviado. A validação começa em instantes.'); await refresh();
  } catch (error) { toast(error.message, true); }
  finally { state.uploading = false; $('file-input').disabled = false; $('file-input').value = ''; heading.textContent = 'Arraste seu arquivo CSV para cá'; }
}
async function sample() {
  try { const response = await fetch('/exemplo-validacao.csv'); if (!response.ok) throw new Error('Não foi possível carregar o exemplo.'); await upload(new File([await response.blob()], 'exemplo-validacao.csv', { type: 'text/csv' })); }
  catch (error) { toast(error.message, true); }
}
async function retry(d) { try { await api(`/api/documents/${d.id}/retry`, { method: 'POST' }); toast('Documento enviado para uma nova tentativa.'); await refresh(); } catch (error) { toast(error.message, true); } }
async function report(d) {
  try {
    const r = await (await api(`/api/documents/${d.id}/report`)).json(); state.reportId = d.id;
    $('report-title').textContent = d.fileName;
    const body = $('report-body'); body.replaceChildren();
    const metrics = element('div', 'report-metrics');
    for (const [label, value] of [['Registros', r.totalRows], ['Válidos', r.validRows], ['Para revisar', r.invalidRows]]) {
      const box = element('div'); box.append(element('strong', '', value), document.createTextNode(label)); metrics.append(box);
    }
    body.append(metrics);
    if (r.rejected) body.append(element('p', '', 'O arquivo foi rejeitado antes de concluir a análise. Corrija o formato e envie um novo arquivo.'));
    if (r.issues.length) {
      const wrap = element('div', 'issues'); const table = element('table'); const head = element('thead'); const hr = element('tr');
      ['REGISTRO', 'CÓDIGO', 'O QUE AJUSTAR'].forEach(x => hr.append(element('th', '', x))); head.append(hr); table.append(head);
      const tbody = element('tbody'); for (const issue of r.issues) { const row = element('tr'); [issue.row, issue.code, issue.message].forEach(x => row.append(element('td', '', x))); tbody.append(row); }
      table.append(tbody); wrap.append(table); body.append(wrap);
      body.append(element('p', 'report-note', `Exibindo ${r.issues.length} de ${r.totalIssues} ocorrências. Baixe o CSV para consultar todas.`));
    } else body.append(element('div', 'success-note', 'Tudo certo! Todos os registros passaram pelas regras de validação.'));
    $('report-dialog').showModal();
  } catch (error) { toast(error.message, true); }
}
async function download() {
  try { const response = await api(`/api/documents/${state.reportId}/report?download=true`); const url = URL.createObjectURL(await response.blob());
    const link = document.createElement('a'); link.href = url; link.download = `talumedoc-${state.reportId}-erros.csv`; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (error) { toast(error.message, true); }
}
function updateThemeButton() {
  const dark = document.documentElement.dataset.theme === 'dark';
  const action = dark ? 'Ativar tema claro' : 'Ativar tema escuro';
  $('theme-toggle').setAttribute('aria-pressed', String(dark));
  $('theme-toggle').setAttribute('aria-label', action);
  $('theme-toggle').title = action;
  $('theme-label').textContent = dark ? 'Tema claro' : 'Tema escuro';
  $('theme-icon').textContent = dark ? '☀' : '☾';
}
updateThemeButton();
window.addEventListener('talumedoc:themechange', updateThemeButton);
$('theme-toggle').addEventListener('click', () => window.TalumeDocTheme.toggle());
$('about-button').addEventListener('click', () => $('about-dialog').showModal());
$('close-about').addEventListener('click', () => $('about-dialog').close());
$('file-input').addEventListener('change', event => upload(event.target.files[0]));
['sample-button', 'sample-side', 'empty-sample'].forEach(id => $(id).addEventListener('click', sample));
$('refresh-button').addEventListener('click', () => refresh());
$('search-input').addEventListener('input', render); $('status-filter').addEventListener('change', render);
$('guide-button').addEventListener('click', () => $('guide-dialog').showModal());
$('close-guide').addEventListener('click', () => $('guide-dialog').close());
$('close-report').addEventListener('click', () => $('report-dialog').close());
$('download-report').addEventListener('click', download);
$('auth-button').addEventListener('click', () => { if (connected()) logout(); else login().catch(e => toast(e.message, true)); });
$('drop-zone').addEventListener('dragover', event => { event.preventDefault(); $('drop-zone').classList.add('dragging'); });
$('drop-zone').addEventListener('dragleave', () => $('drop-zone').classList.remove('dragging'));
$('drop-zone').addEventListener('drop', event => { event.preventDefault(); $('drop-zone').classList.remove('dragging'); if (event.dataTransfer.files.length !== 1) return toast('Envie um arquivo por vez.', true); upload(event.dataTransfer.files[0]); });
try {
  const response = await fetch('/config'); if (!response.ok) throw new Error('O serviço está indisponível. Atualize a página.');
  state.config = await response.json();
  try { await authCallback(); } catch (error) { toast(error.message, true); }
  updateAuth(); render(); await refresh();
  setInterval(() => { if (!document.hidden) refresh(true); }, 5000);
} catch (error) { $('connection').textContent = 'Serviço indisponível'; toast(error.message, true); }
