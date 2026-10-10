namespace YTSPlayer.Modules.YtConnect.Scripts;

/// <summary>
/// Skripte, die in der versteckten Seite Daten auslesen. Die Selektoren und Datenpfade können sich bei YouTube ändern
/// und sind noch NICHT mit einem echten Konto geprüft.
/// </summary>
public static class LibraryScripts
{
    public const string Scrape = """
(() => {
  const out = [], seen = new Set();
  document.querySelectorAll('ytmusic-two-row-item-renderer, ytmusic-responsive-list-item-renderer').forEach(el => {
    const a = [...el.querySelectorAll('a[href]')].find(x => /[?&]list=|browse\/VL/.test(x.getAttribute('href') || ''));
    if (!a) return;
    const href = a.getAttribute('href') || '';
    const m = href.match(/[?&]list=([^&#]+)/) || href.match(/browse\/VL([^?&#]+)/);
    if (!m) return;
    const id = decodeURIComponent(m[1]);
    if (/^RD(AMVM|EM|MM|GMEM)/.test(id)) return; // automatische Radio-Mixe der Wiedergabe, keine Bibliotheks-Playlists
    if (seen.has(id)) return;
    seen.add(id);
    const t = el.querySelector('.title');
    const s = el.querySelector('.subtitle');
    const title = ((t && t.textContent) || a.getAttribute('title') || a.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim();
    out.push({ id, title, sub: s ? s.textContent.replace(/\s+/g, ' ').trim() : '' });
  });
  return JSON.stringify(out);
})()
""";

    public const string Scroll = """
(() => {
  [document.scrollingElement, document.querySelector('ytmusic-app-layout'), document.querySelector('#layout'), document.querySelector('ytmusic-browse-response'), document.querySelector('#content')]
    .forEach(e => { if (e) e.scrollTop = e.scrollHeight; });
  window.scrollTo(0, document.body.scrollHeight);
  return 1;
})()
""";

    /// <summary>Liest die Titel einer geöffneten Playlist-Seite (Selektoren ungeprüft, mit Ersatzwegen).</summary>
    public const string Tracks = """
(() => {
  const out = [], seen = new Set();
  const txt = e => e ? (e.textContent || '').replace(/\s+/g, ' ').trim() : '';
  // Nur die eigenen Titel der Playlist. Die Vorschläge am Ende der Seite stehen in anderen Bereichen und werden ausgelassen.
  let rows = [...document.querySelectorAll('ytmusic-playlist-shelf-renderer ytmusic-responsive-list-item-renderer')];
  if (!rows.length)
    rows = [...document.querySelectorAll('ytmusic-responsive-list-item-renderer')]
      .filter(el => !el.closest('ytmusic-playlist-add-suggestions-renderer, ytmusic-carousel-shelf-renderer, ytmusic-shelf-renderer'));
  rows.forEach(el => {
    let id = '';
    const a = [...el.querySelectorAll('a[href]')].find(x => /[?&]v=/.test(x.getAttribute('href') || ''));
    if (a) { const m = (a.getAttribute('href') || '').match(/[?&]v=([^&#]+)/); if (m) id = decodeURIComponent(m[1]); }
    if (!id && el.data && el.data.playlistItemData) id = el.data.playlistItemData.videoId || '';
    if (!id || seen.has(id)) return;
    seen.add(id);
    const cols = [...el.querySelectorAll('.secondary-flex-columns yt-formatted-string, .flex-columns yt-formatted-string')].map(txt).filter(Boolean);
    const im = el.querySelector('img');
    out.push({
      id,
      title: txt(el.querySelector('.title')) || (a ? a.getAttribute('title') || '' : ''),
      artist: cols[0] || '',
      duration: txt(el.querySelector('.fixed-columns yt-formatted-string, .fixed-columns')),
      thumb: im && /^https?:/.test(im.src || '') ? im.src : '',
      // Nötig zum Entfernen aus der Playlist (YouTube braucht diese Eintrags-Id, nicht die Video-Id). Ungeprüft: fehlt sie, holt die App sie beim Entfernen nach.
      setId: (el.data && el.data.playlistItemData && el.data.playlistItemData.playlistSetVideoId) || ''
    });
  });
  return JSON.stringify(out);
})()
""";

    public const string Diagnose = """
JSON.stringify({
  url: location.href,
  title: document.title,
  twoRow: document.querySelectorAll('ytmusic-two-row-item-renderer').length,
  listItem: document.querySelectorAll('ytmusic-responsive-list-item-renderer').length,
  listLinks: document.querySelectorAll('a[href*="list="]').length,
  vlLinks: document.querySelectorAll('a[href*="browse/VL"]').length
})
""";

    /// <summary>
    /// Liest Name, E-Mail und Profilbild des angemeldeten Kontos. Fragt dafür dieselbe Konto-Menü-Abfrage ab,
    /// die die Seite beim Öffnen des Konto-Menüs selbst stellt. Das Ergebnis kommt per
    /// postMessage als { t: 'profile', ... }, weil ExecuteScript keine Promises abwartet.
    /// </summary>
    /// <param name="allowMenu">Darf das Konto-Menü kurz geöffnet werden (nur beim Login, nicht während der Wiedergabe).</param>
    public static string Profile(bool allowMenu) => ProfileScript.Replace("__ALLOW_MENU__", allowMenu ? "true" : "false");

    const string ProfileScript = """
(() => {
  const send = o => { try { window.chrome.webview.postMessage(Object.assign({ t: 'profile' }, o)); } catch (e) {} };
  (async () => {
    try {
      const allowMenu = __ALLOW_MENU__;
      const cfg = window.ytcfg && window.ytcfg.get ? window.ytcfg : null;
      if (!cfg) { send({ ok: false, reason: 'no-ytcfg' }); return; }
      if (!cfg.get('LOGGED_IN')) { send({ ok: true, loggedIn: false }); return; }
      const txt = x => !x ? '' : (x.simpleText || (x.runs ? x.runs.map(a => a.text).join('') : ''));
      const find = (o, k, d) => {
        if (!o || typeof o !== 'object' || d > 14) return null;
        if (o[k]) return o[k];
        for (const v of Object.values(o)) { const f = find(v, k, d + 1); if (f) return f; }
        return null;
      };
      const best = th => (th && th.length) ? th.reduce((a, b) => ((b.width || 0) >= (a.width || 0) ? b : a)).url || '' : '';
      const isPic = u => typeof u === 'string' && /^(https?:)?\/\//.test(u) && /ggpht\.com|googleusercontent\.com|ytimg\.com/.test(u);
      // Angemeldete InnerTube-Abfragen brauchen denselben Auth-Header, den die Seite selbst baut (SAPISIDHASH).
      // Ohne ihn antwortet YouTube wie bei einem Gast, also ohne Konto-Kopf.
      const authHeader = async () => {
        const m = document.cookie.match(/(?:^|;\s*)(?:SAPISID|__Secure-3PAPISID)=([^;]+)/);
        if (!m || !crypto.subtle) return '';
        const ts = Math.floor(Date.now() / 1000);
        const buf = await crypto.subtle.digest('SHA-1', new TextEncoder().encode(ts + ' ' + m[1] + ' ' + location.origin));
        const hex = [...new Uint8Array(buf)].map(b => b.toString(16).padStart(2, '0')).join('');
        return 'SAPISIDHASH ' + ts + '_' + hex;
      };
      let name = '', email = '', photo = '', how = 'api', dbg = '';
      try {
        const key = cfg.get('INNERTUBE_API_KEY');
        const headers = { 'content-type': 'application/json', 'x-origin': location.origin, 'x-goog-authuser': String(cfg.get('SESSION_INDEX') || 0) };
        const auth = await authHeader();
        if (auth) headers['authorization'] = auth;
        const ctx = cfg.get('INNERTUBE_CONTEXT_CLIENT_NAME'), ver = cfg.get('INNERTUBE_CONTEXT_CLIENT_VERSION');
        if (ctx) headers['x-youtube-client-name'] = String(ctx);
        if (ver) headers['x-youtube-client-version'] = String(ver);
        const r = await fetch('/youtubei/v1/account/account_menu?prettyPrint=false' + (key ? '&key=' + key : ''), {
          method: 'POST', credentials: 'include', headers,
          body: JSON.stringify({ context: cfg.get('INNERTUBE_CONTEXT') })
        });
        const h = find(await r.json(), 'activeAccountHeaderRenderer', 0);
        dbg = 'api ' + r.status + (auth ? ' auth' : ' ohne-auth') + (h ? ' header' : ' kein-header');
        if (h) {
          name = txt(h.accountName); email = txt(h.email) || txt(h.channelHandle);
          photo = best(h.accountPhoto && h.accountPhoto.thumbnails);
        }
      } catch (e) { dbg = 'api-fehler ' + e; }
      if (!name || !photo) {
        // Ersatz: Daten der Kopfleiste. Das <img> ist oft noch leer (lädt erst beim Sichtbarwerden),
        // die Bild-Adresse steht aber schon in den Polymer-Daten des Elements.
        how = 'dom';
        const btn = document.querySelector('ytmusic-settings-button');
        if (btn && !photo) {
          const shadow = btn.querySelector('yt-img-shadow');
          const tn = shadow && (shadow.thumbnail || (shadow.__data && shadow.__data.thumbnail));
          photo = best(tn && tn.thumbnails);
          if (!photo) { const f = find(btn.data || btn.__data, 'thumbnails', 0); photo = best(f); }
          if (!photo) { const img = btn.querySelector('img'); if (img && isPic(img.src)) photo = img.src; }
        }
        if (btn && !name && allowMenu) {
          const clickable = btn.querySelector('#button, button, tp-yt-paper-icon-button') || btn;
          clickable.click();
          for (let i = 0; i < 10 && !name; i++) {
            await new Promise(res => setTimeout(res, 250));
            const hd = [...document.querySelectorAll('*')].find(e => e.localName.endsWith('active-account-header-renderer'));
            if (hd) {
              const q = s => { const e = hd.querySelector(s); return e ? (e.textContent || '').trim() : ''; };
              name = q('#account-name') || q('.account-name');
              email = email || q('#email') || q('.email');
              if (!photo) { const d = hd.data || hd.__data; photo = best(d && d.accountPhoto && d.accountPhoto.thumbnails); }
              if (!photo) { const im = hd.querySelector('img'); if (im && isPic(im.src)) photo = im.src; }
            }
          }
          document.body.click();
          document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        }
      }
      send({ ok: true, loggedIn: true, name, email, photo, how, dbg });
    } catch (e) { send({ ok: false, reason: String(e) }); }
  })();
  return 1;
})()
""";
}
