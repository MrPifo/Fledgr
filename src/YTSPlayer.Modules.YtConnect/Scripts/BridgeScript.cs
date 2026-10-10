namespace YTSPlayer.Modules.YtConnect.Scripts;

/// <summary>JavaScript-Brücke: meldet den Player-Status an die App und nimmt Befehle entgegen.</summary>
public static class BridgeScript
{
    /// <param name="songMode">Auf „Song“ statt Video umschalten.</param>
    /// <param name="volume">Gewünschte Lautstärke (0–1). Die Seite setzt sie sofort selbst, ohne Umweg über die App.</param>
    public static string Build(bool songMode, double volume) =>
        $"window.__yspCfg={{song:{(songMode ? "true" : "false")},vol:{Math.Clamp(volume, 0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}}};" + Js;

    const string Js = """
(() => {
  if (window.top !== window || window.__ytsp) return;
  const post = o => { try { window.chrome.webview.postMessage(o); } catch (e) {} };
  const vid = () => document.querySelector('video');
  const click = sel => { const e = document.querySelector(sel); if (e) { e.click(); return true; } return false; };
  // Die Seite meldet ihre Medien-Steuerung (nexttrack, previoustrack …) über die MediaSession an. Wir merken uns die Funktionen
  // und rufen sie selbst auf. Das ist stabiler als auf Knöpfe zu klicken, die in der Empfehlungs-Warteschlange anders heißen oder fehlen.
  const handlers = {};
  try {
    const ms = navigator.mediaSession, orig = ms.setActionHandler.bind(ms);
    ms.setActionHandler = (a, h) => { handlers[a] = h; return orig(a, h); };
  } catch (e) {}
  const act = (name, sel) => {
    const h = handlers[name];
    if (h) { try { h({ action: name }); return true; } catch (e) {} }
    return click(sel) || click(sel.replace('ytmusic-player-bar ', ''));
  };
  // Position und Länge des Titels, wie YouTube Music sie selbst in der Leiste anzeigt (Sekunden). Null, wenn nicht gefunden.
  // Nötig, weil die Zeit des Medien-Elements über das Titelende hinauslaufen kann (nächster Titel wird an dieselbe Zeitachse angehängt).
  const hms = s => { const m = s.match(/(?:(\d+):)?(\d+):(\d+)/); return m ? (+(m[1] || 0)) * 3600 + (+m[2]) * 60 + (+m[3]) : NaN; };
  const barTimes = () => {
    try {
      const pb = document.querySelector('ytmusic-player-bar #progress-bar, #progress-bar');
      if (pb) {
        const mx = parseFloat(pb.getAttribute('aria-valuemax') || pb.max), now = parseFloat(pb.getAttribute('aria-valuenow') || pb.value);
        if (mx > 0 && !isNaN(now)) return { pos: now, dur: mx };
      }
      const ti = document.querySelector('ytmusic-player-bar .time-info');
      const parts = ti ? (ti.textContent || '').split('/') : [];
      if (parts.length === 2) {
        const p = hms(parts[0]), d = hms(parts[1]);
        if (!isNaN(p) && d > 0) return { pos: p, dur: d };
      }
    } catch (e) {}
    return null;
  };
  const playerDuration = () => { const b = barTimes(); return b ? b.dur : 0; };
  // Abstand zwischen der Zeit des Medien-Elements und der Zeit des Titels (meist 0). Beim Spulen wird er dazugerechnet.
  const trackOffset = () => {
    const v = vid(), b = barTimes();
    if (!v || !b) return 0;
    const off = v.currentTime - b.pos;
    return Math.abs(off) > 2 ? off : 0;
  };
  // „Mag ich“ des laufenden Titels. Quelle der Wahrheit ist YouTube selbst (API), nicht der Knopf der versteckten Seite.
  // known: Video-ID → 'LIKE' / 'DISLIKE' / 'INDIFFERENT' für diese Sitzung (eigene Bewertung sofort, sonst mitgelesen).
  let lh = '';
  const known = new Map();
  // Video-ID des laufenden Titels: zuerst vom Player selbst (stimmt auch in Playlists), sonst aus der Adresse.
  const curVid = () => {
    try { const p = document.querySelector('#movie_player'); const d = p && p.getVideoData && p.getVideoData(); if (d && d.video_id) return d.video_id; } catch (e) {}
    try { return new URL(location.href).searchParams.get('v') || ''; } catch (e) { return ''; }
  };
  const likeEl = () => document.querySelector('ytmusic-player-bar ytmusic-like-button-renderer');
  const likeRaw = () => {
    const v = curVid();
    if (known.has(v)) return known.get(v);
    const e = likeEl();
    return e ? String(e.likeStatus || e.getAttribute('like-status') || '?') : 'kein Knopf';
  };
  // InnerTube-Abfrage wie die Seite selbst (Auth-Header, Brand-Konto). Gibt { status, json, note } zurück.
  const ytApi = async (path, body) => {
    const cfg = window.ytcfg;
    if (!cfg || !cfg.get) throw new Error('keine Seitenkonfiguration');
    const headers = { 'content-type': 'application/json', 'x-origin': location.origin, 'x-goog-authuser': String(cfg.get('SESSION_INDEX') || 0) };
    const m = document.cookie.match(/(?:^|;\s*)(?:SAPISID|__Secure-3PAPISID)=([^;]+)/);
    if (m && crypto.subtle) {
      const ts = Math.floor(Date.now() / 1000);
      const buf = await crypto.subtle.digest('SHA-1', new TextEncoder().encode(ts + ' ' + m[1] + ' ' + location.origin));
      headers['authorization'] = 'SAPISIDHASH ' + ts + '_' + [...new Uint8Array(buf)].map(b => b.toString(16).padStart(2, '0')).join('');
    }
    const name = cfg.get('INNERTUBE_CONTEXT_CLIENT_NAME'), ver = cfg.get('INNERTUBE_CONTEXT_CLIENT_VERSION'), key = cfg.get('INNERTUBE_API_KEY');
    if (name) headers['x-youtube-client-name'] = String(name);
    if (ver) headers['x-youtube-client-version'] = String(ver);
    // Brand-Konto / zweiter Kanal: die Seite schickt dann die Kanal-Sitzung mit, sonst landet die Aktion woanders.
    const page = cfg.get('DELEGATED_SESSION_ID');
    if (page) headers['x-goog-pageid'] = String(page);
    const visitor = cfg.get('VISITOR_DATA');
    if (visitor) headers['x-goog-visitor-id'] = String(visitor);
    const r = await fetch('/youtubei/v1/' + path + '?prettyPrint=false' + (key ? '&key=' + key : ''), {
      method: 'POST', credentials: 'include', headers,
      body: JSON.stringify(Object.assign({ context: cfg.get('INNERTUBE_CONTEXT') }, body))
    });
    let json = null;
    try { json = await r.json(); } catch (e) {}
    // Kurzer Befund für die Diagnose (ohne Inhalte).
    let note = (headers['authorization'] ? 'auth' : 'ohne-auth') + (page ? ' page' : '');
    if (json && json.error) note += ' fehler:' + (json.error.status || json.error.code);
    return { status: r.status, json, note };
  };
  const findKey = (o, k, d) => {
    if (!o || typeof o !== 'object' || d > 25) return undefined;
    if (k in o) return o[k];
    for (const x of Object.values(o)) { const f = findKey(x, k, d + 1); if (f !== undefined) return f; }
    return undefined;
  };
  // Bewertung setzen: 'LIKE', 'DISLIKE' oder 'INDIFFERENT' (entfernen). Genau eine Anfrage, wie ein Klick auf der Seite.
  // Gibt den HTTP-Status zurück oder einen Text bei Fehler.
  const apiRate = async (v, target) => {
    if (!v) return 'keine Video-ID';
    const path = target === 'LIKE' ? 'like' : target === 'DISLIKE' ? 'dislike' : 'removelike';
    const r = await ytApi('like/' + path, { target: { videoId: v } });
    lh = 'API ' + path + ' ' + r.status + ' ' + r.note;
    if (r.status === 200) { known.set(v, target); ownAt.set(v, Date.now()); }
    return r.status;
  };
  // Mitlesen statt selbst fragen: Die Seite holt bei jedem Titel selbst „next“. Wir lesen dessen Antwort mit
  // (fetch und XHR), es geht also KEINE zusätzliche Anfrage an YouTube. ls zählt gelesene Status (Diagnose).
  const ownAt = new Map();
  let ls = 0;
  const onNext = (json, reqBody) => {
    try {
      const acts = (((json || {}).playerOverlays || {}).playerOverlayRenderer || {}).actions || [];
      const lb = acts.map(a => a && a.likeButtonRenderer).find(Boolean);
      const status = lb ? lb.likeStatus : findKey(json, 'likeStatus', 0);
      if (typeof status !== 'string') return;
      let v = (lb && lb.target && lb.target.videoId) || findKey(json && json.currentVideoEndpoint, 'videoId', 0) || '';
      if (!v && reqBody) { try { v = JSON.parse(reqBody).videoId || ''; } catch (e) {} }
      if (!v) return;
      // Eigener Klick vor kurzem: die Antwort könnte älter sein als der Klick.
      if (Date.now() - (ownAt.get(v) || 0) < 5000) return;
      known.set(v, status);
      ls++;
      setTimeout(tick, 0);
    } catch (e) {}
  };
  const isNext = u => typeof u === 'string' && u.indexOf('/youtubei/v1/next') >= 0;
  try {
    const of = window.fetch;
    window.fetch = function (input, init) {
      const p = of.apply(this, arguments);
      try {
        const u = typeof input === 'string' ? input : (input && input.url) || '';
        if (isNext(u)) {
          const body = init && typeof init.body === 'string' ? init.body : '';
          p.then(r => r.clone().json()).then(j => onNext(j, body)).catch(() => {});
        }
      } catch (e) {}
      return p;
    };
  } catch (e) {}
  try {
    const oo = XMLHttpRequest.prototype.open, os = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (m, u) { this.__yspNext = isNext(String(u)); return oo.apply(this, arguments); };
    XMLHttpRequest.prototype.send = function (b) {
      if (this.__yspNext) this.addEventListener('load', () => {
        try { onNext(typeof this.response === 'object' && this.response ? this.response : JSON.parse(this.responseText), typeof b === 'string' ? b : ''); } catch (e) {}
      });
      return os.apply(this, arguments);
    };
  } catch (e) {}
  // Kennung des Künstlers des laufenden Titels (UC…): der Link im Namen unter dem Titel in der Player-Leiste. Leer, wenn nicht gefunden.
  const artistId = () => {
    try {
      const a = document.querySelector('ytmusic-player-bar .byline a[href*="channel/"], ytmusic-player-bar a[href*="channel/UC"]');
      const m = a && (a.getAttribute('href') || '').match(/channel\/(UC[\w-]+)/);
      return m ? m[1] : '';
    } catch (e) { return ''; }
  };
  const snap = () => {
    const v = vid(), m = navigator.mediaSession && navigator.mediaSession.metadata;
    const a = m && m.artwork && m.artwork.length ? m.artwork[m.artwork.length - 1].src : '';
    return { t: 'state', title: m ? m.title : '', artist: m ? m.artist : '', album: m ? m.album : '', art: a,
      playing: !!v && !v.paused && !v.ended, pos: barTimes() ? barTimes().pos : (v ? v.currentTime : 0),
      vdur: v && isFinite(v.duration) ? v.duration : 0, pdur: playerDuration(),
      dur: playerDuration() || (v && isFinite(v.duration) ? v.duration : 0),
      aid: artistId(), vol: v ? v.volume : 1, vg, vgLast, mu, sk, wt, ld, sm, rate: likeRaw(), lr: likeRaw() + (lh ? ' (' + lh + ')' : '') + ' [mitgelesen ' + ls + ']', url: location.href };
  };
  let last = '', lastTrack = '';
  const tick = () => {
    const s = snap(), k = [s.title, s.artist, s.art, s.playing, Math.floor(s.pos), Math.floor(s.dur), Math.floor(s.vdur), s.vol, s.vg, s.mu, s.sk, s.wt, s.ld, s.sm, s.rate, s.lr, s.aid].join('|');
    if (k !== last) { last = k; post(s); }
    const tk = s.title + '|' + s.artist;
    if (s.title && tk !== lastTrack) {
      lastTrack = tk;
      if (window.__yspCfg && window.__yspCfg.song) setTimeout(() => { sm++; click('ytmusic-av-toggle .song-button'); }, 1500);
    }
  };
  // Gewünschte Lautstärke: die Seite stellt sie bei jedem neuen Titel selbst um. Wir halten sie hier sofort fest.
  // Zähler für die Diagnose: wie oft die Seite die Lautstärke ändern wollte (vg, zuletzt vgLast) und wie oft sie stumm/laut geschaltet wurde (mu).
  let vg = 0, vgLast = '', mu = 0;
  // Weitere Zähler: fremde Seeks (nicht von uns), Puffer-Pausen, Neuladen des Mediums, Klicks auf „Song“ (vom Song-Modus).
  let sk = 0, wt = 0, ld = 0, sm = 0, ownSeek = false;
  document.addEventListener('seeking', () => { if (ownSeek) ownSeek = false; else sk++; }, true);
  document.addEventListener('waiting', () => { wt++; }, true);
  document.addEventListener('loadstart', () => { ld++; }, true);
  // Wert aus dem Startskript, aber ein später gesetzter Wert (sessionStorage überlebt das Neuladen der Seite) hat Vorrang.
  let want = window.__yspCfg && typeof window.__yspCfg.vol === 'number' ? window.__yspCfg.vol : null;
  try { const s = parseFloat(sessionStorage.getItem('__yspVol')); if (!isNaN(s)) want = s; } catch (e) {}
  // Die Seite darf die Lautstärke nicht mehr ändern: jede Zuweisung an video.volume wird sofort auf unseren Wert gesetzt.
  // Das wirkt schon im selben Moment, nicht erst nach dem volumechange-Ereignis (sonst hört man die Abweichung kurz, z. B. am Songende).
  try {
    const d = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'volume');
    if (d && d.set) Object.defineProperty(HTMLMediaElement.prototype, 'volume', {
      configurable: true, enumerable: d.enumerable,
      get() { return d.get.call(this); },
      set(x) {
        if (want !== null && Math.abs(x - want) > 0.005) { vg++; vgLast = Number(x).toFixed(2); }
        d.set.call(this, want !== null ? want : x);
      }
    });
  } catch (e) {}
  let lastMuted = null;
  document.addEventListener('volumechange', e => {
    const v = e.target;
    if (v && v.tagName === 'VIDEO' && v.muted !== lastMuted) { if (lastMuted !== null) mu++; lastMuted = v.muted; }
  }, true);
  const hold = e => {
    const v = e.target;
    if (want !== null && v && v.tagName === 'VIDEO' && Math.abs(v.volume - want) > 0.005) v.volume = want;
  };
  ['loadstart', 'loadedmetadata', 'durationchange', 'play', 'playing', 'volumechange'].forEach(n => document.addEventListener(n, hold, true));
  ['play', 'pause', 'ended', 'volumechange', 'seeked', 'loadedmetadata'].forEach(n => document.addEventListener(n, tick, true));
  setInterval(tick, 1000);
  window.__ytsp = {
    cmd(c, a) {
      const v = vid();
      switch (c) {
        case 'play': if (v) v.play(); break;
        case 'pause': if (v) v.pause(); break;
        case 'toggle': if (v) { v.paused ? v.play() : v.pause(); } break;
        case 'rate': {
          // a: 0 = Bewertung entfernen, 1 = Like, 2 = Dislike. Zuerst die API (wie die Seite selbst), Klick nur als Ersatz.
          // Beim Klick-Ersatz probieren wir mehrere Arten nacheinander und hören auf, sobald sich der Zustand ändert.
          const root = likeEl();
          const target = a === 1 ? 'LIKE' : a === 2 ? 'DISLIKE' : 'INDIFFERENT', v0 = curVid(), prev = likeRaw();
          const viaClick = () => {
          if (!root) { lh += ' / kein Knopf'; tick(); return; }
          // Entfernen = den gerade aktiven Knopf noch einmal drücken.
          const which = target === 'DISLIKE' || (target === 'INDIFFERENT' && prev === 'DISLIKE') ? 'dislike' : 'like';
          const btn = root.querySelector('#button-shape-' + which + ' button');
          const fill = root.querySelector('#button-shape-' + which + ' .yt-spec-touch-feedback-shape__fill');
          const press = el => {
            if (!el) return;
            const r = el.getBoundingClientRect();
            const o = { bubbles: true, cancelable: true, composed: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, button: 0, pointerId: 1, isPrimary: true };
            for (const t of ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'])
              el.dispatchEvent(t.startsWith('pointer') ? new PointerEvent(t, o) : new MouseEvent(t, o));
          };
          const tries = [() => btn && btn.click(), () => fill && fill.click(), () => press(btn), () => press(fill)];
          const elRaw = () => String(root.likeStatus || root.getAttribute('like-status') || '?');
          const before = elRaw();
          let n = 0;
          const step = () => {
            if (elRaw() !== before) { known.set(v0, elRaw()); lh += ' / Klick Art ' + n; tick(); return; }
            if (n >= tries.length) { lh += ' / kein Klick wirkte'; tick(); return; }
            tries[n++]();
            setTimeout(step, 1500);
          };
          step();
          };
          apiRate(v0, target).then(st => {
            if (typeof st === 'string') lh = 'API ' + st;
            if (st === 200) {
              try { if (root) root.likeStatus = target; } catch (e) {}
              tick();
            } else viaClick();
          }).catch(e => { lh = 'API Fehler ' + e; viaClick(); });
          break;
        }
        case 'next': act('nexttrack', 'ytmusic-player-bar .next-button'); break;
        case 'prev': act('previoustrack', 'ytmusic-player-bar .previous-button'); break;
        case 'seek': if (v) { ownSeek = true; v.currentTime = a + trackOffset(); } break;
        case 'volume':
          want = Math.max(0, Math.min(1, a));
          try { sessionStorage.setItem('__yspVol', String(want)); } catch (e) {}
          if (v) v.volume = want;
          break;
      }
      setTimeout(tick, 50);
    }
  };
})();
""";
}
