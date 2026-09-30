// Loaded before styles to apply the saved preference before the first paint.
(() => {
  const storageKey = 'talumedoc-theme';
  const system = window.matchMedia?.('(prefers-color-scheme: dark)');
  const valid = value => value === 'light' || value === 'dark';
  let preference;
  try { preference = localStorage.getItem(storageKey); } catch { /* Storage may be disabled. */ }
  function apply(theme) {
    document.documentElement.dataset.theme = theme;
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.content = theme === 'dark' ? '#0c1514' : '#f6f7f3';
    window.dispatchEvent(new CustomEvent('talumedoc:themechange', { detail: { theme } }));
  }
  function set(theme) {
    if (!valid(theme)) return;
    preference = theme;
    try { localStorage.setItem(storageKey, theme); } catch { /* Keep working for this tab. */ }
    apply(theme);
  }
  apply(valid(preference) ? preference : system?.matches ? 'dark' : 'light');
  system?.addEventListener?.('change', event => { if (!valid(preference)) apply(event.matches ? 'dark' : 'light'); });
  window.addEventListener('storage', event => {
    if (event.key !== storageKey && event.key !== null) return;
    preference = event.newValue;
    apply(valid(preference) ? preference : system?.matches ? 'dark' : 'light');
  });
  window.TalumeDocTheme = Object.freeze({ set, toggle: () => set(document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark') });
})();
