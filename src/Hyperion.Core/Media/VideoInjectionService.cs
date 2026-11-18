using Microsoft.Web.WebView2.Core;

namespace Hyperion.Core.Media;

/// <summary>Injects the hotkey script into every frame of a WebView2 and relays its messages to <see cref="GlobalSpeedEngine"/>.</summary>
public sealed class VideoInjectionService(GlobalSpeedEngine engine)
{
    const string ConfigPlaceholder = "__CONFIG__";

    // Runs in the page's main world, in all frames. Hotkeys are matched on KeyboardEvent.code.
    const string Script = """
(() => {
  if (window.__hyperionMedia) return;
  window.__hyperionMedia = true;
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

  window.addEventListener('keydown', e => {
    if (isTyping(deepActive())) return;
    const combo = (e.ctrlKey ? 'Ctrl+' : '') + (e.altKey ? 'Alt+' : '') + (e.shiftKey ? 'Shift+' : '') + (e.metaKey ? 'Meta+' : '') + e.code;
    const action = cfg.keys[combo];
    if (!action) return;
    const v = pickVideo();
    if (!v) return;
    const [kind, a, b] = action.split('.');
    if (action === 'speed.up') setRate(v, v.playbackRate + cfg.step);
    else if (action === 'speed.down') setRate(v, v.playbackRate - cfg.step);
    else if (kind === 'preset') setRate(v, cfg.presets[Number(a) - 1]);
    else if (kind === 'seek') {
      const s = cfg.seeks[Number(b) - 1] * (a === 'back' ? -1 : 1);
      seek(v, s);
      post({ type: 'osd', kind: 'seek', value: s });
    } else return;
    e.preventDefault();
    e.stopImmediatePropagation();
  }, true);

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
