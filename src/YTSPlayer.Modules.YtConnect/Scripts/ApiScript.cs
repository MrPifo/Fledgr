namespace YTSPlayer.Modules.YtConnect.Scripts;

/// <summary>
/// Schickt eine Anfrage an die InnerTube-Schnittstelle von YouTube Music, wie es die Seite selbst tut (gleiche Anmelde-Kopfzeilen).
/// Das Ergebnis kommt per postMessage als { t: 'api', rid, status, body | error }, weil ExecuteScript keine Promises abwartet.
/// </summary>
public static class ApiScript
{
    /// <param name="requestId">Kennung, mit der die Antwort der Anfrage zugeordnet wird.</param>
    /// <param name="path">Pfad hinter /youtubei/v1/, z. B. browse/edit_playlist (nur Konstanten, nie Nutzereingaben).</param>
    /// <param name="bodyJson">Der Anfrage-Inhalt als JSON-Objekt. Der Seiten-Kontext wird im Skript ergänzt.</param>
    public static string Build(string requestId, string path, string bodyJson) =>
        Js.Replace("__RID__", requestId).Replace("__PATH__", path).Replace("__BODY__", bodyJson);

    const string Js = """
(() => {
  const send = o => { try { window.chrome.webview.postMessage(Object.assign({ t: 'api', rid: '__RID__' }, o)); } catch (e) {} };
  (async () => {
    try {
      for (let i = 0; i < 30 && !(window.ytcfg && window.ytcfg.get && window.ytcfg.get('INNERTUBE_CONTEXT')); i++) await new Promise(r => setTimeout(r, 500));
      const cfg = window.ytcfg;
      if (!cfg || !cfg.get || !cfg.get('INNERTUBE_CONTEXT')) { send({ status: 0, error: 'Die Seite ist noch nicht bereit.' }); return; }
      if (!cfg.get('LOGGED_IN')) { send({ status: 0, error: 'Nicht angemeldet.' }); return; }
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
      const body = __BODY__;
      body.context = cfg.get('INNERTUBE_CONTEXT');
      const r = await fetch('/youtubei/v1/__PATH__?prettyPrint=false' + (key ? '&key=' + key : ''), {
        method: 'POST', credentials: 'include', headers, body: JSON.stringify(body)
      });
      const text = await r.text();
      send({ status: r.status, body: text.length > 3000000 ? '' : text });
    } catch (e) { send({ status: 0, error: String(e) }); }
  })();
  return 1;
})()
""";
}
