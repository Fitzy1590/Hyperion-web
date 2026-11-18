using Microsoft.Web.WebView2.Core;

namespace Hyperion.Core.Media;

/// <summary>Injects the hotkey script into every frame of a WebView2 and relays its messages to <see cref="GlobalSpeedEngine"/>.</summary>
public sealed class VideoInjectionService(GlobalSpeedEngine engine)
{
    const string ConfigPlaceholder = "__CONFIG__";

    // Runs in the page's main world, in all frames. Shortcuts are matched on KeyboardEvent.code.
    const string Script = """
(() => {
  if (window.__hyperionMedia) return;
  const cfg = __CONFIG__;
  const post = m => { try { window.chrome.webview.postMessage(m); } catch (_) {} };

  const deepActive = () => {
    let el = document.activeElement;
    while (el && el.shadowRoot && el.shadowRoot.activeElement) el = el.shadowRoot.activeElement;
    return el;
  };
  const isTyping = el => {
    if (!el) return false;
    const tag = el.tagName;
    if (tag === 'INPUT') {
      const t = (el.type || 'text').toLowerCase();
      return !['button', 'checkbox', 'radio', 'range', 'submit', 'reset', 'image', 'file', 'color'].includes(t);
    }
    if (tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable) return true;
    const role = el.getAttribute && el.getAttribute('role');
    return role === 'textbox' || role === 'searchbox' || role === 'combobox';
  };
  const pickVideo = () => {
    const vs = [...document.querySelectorAll('video')];
    if (!vs.length) return null;
    return vs.find(v => !v.paused) || vs.sort((a, b) => b.clientWidth * b.clientHeight - a.clientWidth * a.clientHeight)[0];
  };
  const seek = (v, d) => {
    if (location.hostname.endsWith('netflix.com')) {
      try {
        const p = netflix.appContext.state.playerApp.getAPI().videoPlayer;
        const s = p.getVideoPlayerBySessionId(p.getAllPlayerSessionIds()[0]);
        s.seek(s.getCurrentTime() + d * 1000);
        return;
      } catch (_) { /* fall through to currentTime */ }
    }
    v.currentTime = Math.max(0, Math.min(v.duration || Infinity, v.currentTime + d));
  };
  const setRate = (v, r) => {
    v.playbackRate = Math.min(16, Math.max(0.1, Math.round(r * 100) / 100));
    post({ type: 'osd', kind: 'speed', value: v.playbackRate });
  };

  const keys = {};
  for (const r of cfg.rules) keys[r.key] = r; // last rule wins on a shared key

  // Outermost ancestor that is still exactly the video's size: the site's player, so its controls survive fullscreen.
  const playerOf = v => {
    let el = v;
    const r = v.getBoundingClientRect();
    while (el.parentElement && el.parentElement !== document.body) {
      const p = el.parentElement.getBoundingClientRect();
      if (Math.abs(p.width - r.width) > 2 || Math.abs(p.height - r.height) > 2) break;
      el = el.parentElement;
    }
    return el;
  };
  const fullProps = {
    position: 'fixed', inset: '0', width: '100vw', height: '100vh', 'max-width': 'none', 'max-height': 'none',
    margin: '0', 'z-index': '2147483647', background: '#000', 'object-fit': 'contain'
  };
  let full = null; // { el, style } while the borderless "full window" mode is on
  const enterFullWindow = v => {
    if (!v) return false;
    if (full) return true;
    full = { el: v, style: v.getAttribute('style') };
    for (const [k, val] of Object.entries(fullProps)) v.style.setProperty(k, val, 'important');
    post({ type: 'fullwindow', value: 1 });
    return true;
  };
  const exitFullWindow = () => {
    if (!full) return;
    const { el, style } = full;
    full = null;
    if (style === null) el.removeAttribute('style'); else el.setAttribute('style', style);
    post({ type: 'fullwindow', value: 0 });
  };
  // Called by the host (toolbar) through ExecuteScriptAsync.
  window.__hyperionMedia = {
    enterFullWindow: () => enterFullWindow(pickVideo()),
    exit: () => { exitFullWindow(); if (document.fullscreenElement) document.exitFullscreen().catch(() => {}); }
  };

  let held = null; // fast forward: { code, video, rate } while its key is down
  const release = () => {
    if (!held) return;
    const { video, rate } = held;
    held = null;
    setRate(video, rate);
  };

  window.addEventListener('keydown', e => {
    if (e.code === 'Escape' && full) {
      exitFullWindow();
      e.preventDefault();
      e.stopImmediatePropagation();
      return;
    }
    if (isTyping(deepActive())) return;
    const combo = (e.ctrlKey ? 'Ctrl+' : '') + (e.altKey ? 'Alt+' : '') + (e.shiftKey ? 'Shift+' : '') + (e.metaKey ? 'Meta+' : '') + e.code;
    const r = keys[combo];
    if (!r) return;
    const v = pickVideo();
    if (!v) return;
    switch (r.action) {
      case 'speed': setRate(v, r.mode === '=' ? r.value : v.playbackRate + r.value); break;
      case 'seek': seek(v, r.value); post({ type: 'osd', kind: 'seek', value: r.value }); break;
      case 'fastforward':
        if (!held) { held = { code: e.code, video: v, rate: v.playbackRate }; setRate(v, r.value); }
        break;
      case 'fullscreen':
        if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
        else playerOf(v).requestFullscreen().catch(() => {});
        break;
      case 'fullwindow': if (full) exitFullWindow(); else enterFullWindow(v); break;
      default: return;
    }
    e.preventDefault();
    e.stopImmediatePropagation();
  }, true);
  window.addEventListener('keyup', e => { if (held && e.code === held.code) release(); }, true);
  window.addEventListener('blur', release);

  const playing = on => () => post({ type: 'playing', value: on });
  document.addEventListener('play', playing(true), true);
  document.addEventListener('pause', playing(false), true);
  document.addEventListener('ended', playing(false), true);
})();
""";

    sealed record Attached(string ScriptId, EventHandler<CoreWebView2WebMessageReceivedEventArgs> Handler);

    readonly Dictionary<CoreWebView2, Attached> _attached = [];
    readonly HashSet<CoreWebView2> _playing = [];

    public bool IsPlaying(CoreWebView2 view) => _playing.Contains(view);

    public async Task AttachAsync(CoreWebView2 view)
    {
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> handler = (_, e) =>
        {
            var m = engine.Handle(e.WebMessageAsJson);
            if (m is { Type: "playing" }) { if (m.Value > 0) _playing.Add(view); else _playing.Remove(view); }
        };
        view.WebMessageReceived += handler;
        var id = await view.AddScriptToExecuteOnDocumentCreatedAsync(BuildScript());
        _attached[view] = new Attached(id, handler);
    }

    /// <summary>Re-registers the script after a settings change. Already-loaded pages pick it up on next navigation.</summary>
    public async Task RefreshAsync()
    {
        foreach (var (view, a) in _attached.ToArray())
        {
            view.RemoveScriptToExecuteOnDocumentCreated(a.ScriptId);
            _attached[view] = a with { ScriptId = await view.AddScriptToExecuteOnDocumentCreatedAsync(BuildScript()) };
        }
    }

    public void Detach(CoreWebView2 view)
    {
        if (!_attached.Remove(view, out var a)) return;
        view.WebMessageReceived -= a.Handler;
        view.RemoveScriptToExecuteOnDocumentCreated(a.ScriptId);
        _playing.Remove(view);
    }

    string BuildScript() => Script.Replace(ConfigPlaceholder, engine.BuildConfigJson());
}
