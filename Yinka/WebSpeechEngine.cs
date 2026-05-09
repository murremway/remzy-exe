using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Yinka;

/// <summary>
/// Caption engine backed by Chromium's webkitSpeechRecognition, hosted in a hidden
/// WebView2. This bypasses every Windows speech-stack pitfall (language packs, online
/// speech privacy gating, SAPI engines) at the cost of needing the WebView2 Runtime
/// on the user's machine and an internet connection (Chromium uses Google's cloud).
///
/// The engine owns a 1×1 hidden Window so it doesn't intrude on the dashboard layout.
/// Inter-process bridge: chrome.webview.postMessage(json) on the JS side becomes a
/// WebMessageReceived event we route to the engine's typed events.
/// </summary>
public sealed class WebSpeechEngine : ICaptionEngine
{
    private readonly Dispatcher _dispatcher;
    private Window? _hostWindow;
    private WebView2? _web;
    private bool _started;

    /// <summary>
    /// Synthetic host name that <see cref="CoreWebView2.SetVirtualHostNameToFolderMapping"/>
    /// resolves to our local webroot. Must be a hostname-shaped string; Chromium treats
    /// the resulting https:// origin as secure, which is what unlocks getUserMedia.
    /// </summary>
    private const string VirtualHost = "yinka.local";

    public CaptionEngineKind Kind => CaptionEngineKind.WebSpeech;
    public CaptionEngineState State { get; private set; } = CaptionEngineState.Idle;
    public bool IsRunning => _started;

    public event Action<string>? PhraseCommitted;
    public event Action<string>? Hypothesis;
    public event Action<string>? SessionMessage;
    public event Action<CaptionEngineState>? StateChanged;
    public event Action<EngineFailure>? Failed;

    public WebSpeechEngine(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public EngineAvailability Probe()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrEmpty(version)
                ? new EngineAvailability(false, "WebView2 Runtime is not installed.")
                : new EngineAvailability(true, "WebView2 " + version);
        }
        catch (Exception ex)
        {
            return new EngineAvailability(false, "WebView2 Runtime is not installed: " + ex.Message);
        }
    }

    public async Task StartAsync()
    {
        if (_started)
            return;

        SetState(CaptionEngineState.Starting);
        Status("Starting Web Speech engine…");
        SpeechDiagnostics.Info("Web", "StartAsync");

        if (!Probe().IsAvailable)
        {
            Fail(new EngineFailure(
                "Microsoft Edge WebView2 Runtime is not installed.",
                "Download the Evergreen WebView2 Runtime from Microsoft (free) and reopen Yinka.",
                "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                "Open Microsoft download page"));
            return;
        }

        try
        {
            // The host Window must stay visible for WebView2 to keep processing audio
            // (Chromium suspends rendering / capture on hidden windows). We keep it 1x1,
            // fully transparent, and parked far off-screen so the user never sees it.
            _hostWindow = new Window
            {
                Width = 1,
                Height = 1,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Topmost = false,
                ShowActivated = false,
                Opacity = 0,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Title = "Yinka Web Speech (hidden)",
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
            };
            _web = new WebView2();
            _hostWindow.Content = _web;
            _hostWindow.Show();

            var userData = Path.Combine(SpeechDiagnostics.LogDirectory, "webview2");
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);

            _web.CoreWebView2.PermissionRequested += OnPermissionRequested;
            _web.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // CRITICAL: webkitSpeechRecognition calls getUserMedia under the hood, which
            // requires a secure context. NavigateToString hosts at about:blank (null
            // origin) and silently fails. Map a virtual https://yinka.local/ to a
            // per-user folder we control, write the SPA there, and navigate to it.
            var webRoot = Path.Combine(SpeechDiagnostics.LogDirectory, "webroot");
            Directory.CreateDirectory(webRoot);
            var htmlPath = Path.Combine(webRoot, "speech.html");
            File.WriteAllText(htmlPath, BuildHtml());

            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                webRoot,
                CoreWebView2HostResourceAccessKind.Allow);
            _web.CoreWebView2.Navigate($"https://{VirtualHost}/speech.html");

            _started = true;
            SetState(CaptionEngineState.Listening);
            Status("Listening (Web Speech API).");
        }
        catch (Exception ex)
        {
            await StopAsync().ConfigureAwait(true);
            Fail(new EngineFailure(
                "Could not start the Web Speech engine.",
                ex.Message + "\n\nIf you don't have internet, switch to the SAPI or Whisper engine for offline use.",
                "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                "Open Microsoft download page",
                Inner: ex));
        }
    }

    public Task StopAsync()
    {
        if (!_started && _hostWindow is null)
        {
            SetState(CaptionEngineState.Idle);
            return Task.CompletedTask;
        }

        SetState(CaptionEngineState.Stopping);
        SpeechDiagnostics.Info("Web", "StopAsync");

        try
        {
            if (_web?.CoreWebView2 is { } core)
            {
                core.PermissionRequested -= OnPermissionRequested;
                core.WebMessageReceived -= OnWebMessageReceived;
                try { core.PostWebMessageAsString("stop"); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("Web", "Stop posting: " + ex.Message);
        }

        try { _web?.Dispose(); } catch { /* ignore */ }
        _web = null;

        try { _hostWindow?.Close(); } catch { /* ignore */ }
        _hostWindow = null;

        _started = false;
        SetState(CaptionEngineState.Idle);
        Status("Speech caption stopped.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
    }

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind == CoreWebView2PermissionKind.Microphone)
        {
            e.State = CoreWebView2PermissionState.Allow;
            SpeechDiagnostics.Info("Web", "Granted microphone permission to WebView2.");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            switch (type)
            {
                case "hypothesis":
                {
                    var text = root.GetProperty("text").GetString() ?? "";
                    _dispatcher.BeginInvoke(new Action(() => Hypothesis?.Invoke(text.Trim())));
                    break;
                }
                case "result":
                {
                    var text = (root.GetProperty("text").GetString() ?? "").Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        SpeechDiagnostics.Info("Web", "Result: " + text);
                        _dispatcher.BeginInvoke(new Action(() => PhraseCommitted?.Invoke(text)));
                    }
                    break;
                }
                case "status":
                {
                    var text = root.GetProperty("text").GetString() ?? "";
                    SpeechDiagnostics.Info("Web", "Status: " + text);
                    Status(text);
                    break;
                }
                case "error":
                {
                    var msg = root.GetProperty("text").GetString() ?? "(unknown)";
                    SpeechDiagnostics.Error("Web", msg);
                    Fail(new EngineFailure(
                        "Web Speech API reported an error.",
                        msg + "\n\nWeb Speech needs internet, mic permission, and works in WebView2 (Chromium).",
                        SettingsLinks.MicrophonePrivacy,
                        "Open Microphone privacy"));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("Web", "Bad WebMessage: " + ex.Message);
        }
    }

    private static string BuildHtml() => """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Yinka Web Speech</title></head>
        <body>
        <script>
        (function () {
          const post = (obj) => { try { chrome.webview.postMessage(obj); } catch (e) {} };
          const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
          if (!SR) {
            post({ type: "error", text: "webkitSpeechRecognition is not available in this WebView2." });
            return;
          }
          const r = new SR();
          r.continuous = true;
          r.interimResults = true;
          r.lang = navigator.language || "en-US";

          let stopped = false;
          let restartBackoff = 250;

          r.onresult = (ev) => {
            let interim = "";
            let final = "";
            for (let i = ev.resultIndex; i < ev.results.length; i++) {
              const res = ev.results[i];
              if (res.isFinal) final += res[0].transcript + " "; else interim += res[0].transcript + " ";
            }
            if (final.trim().length) post({ type: "result", text: final.trim() });
            if (interim.trim().length) post({ type: "hypothesis", text: interim.trim() });
            else if (final.trim().length) post({ type: "hypothesis", text: "" });
          };
          r.onerror = (ev) => {
            post({ type: "error", text: "speech error: " + (ev.error || "unknown") });
          };
          r.onstart = () => { post({ type: "status", text: "Listening (Web Speech API)." }); restartBackoff = 250; };
          r.onend = () => {
            if (stopped) { post({ type: "status", text: "Web Speech ended." }); return; }
            post({ type: "status", text: "Reconnecting Web Speech…" });
            setTimeout(() => { try { r.start(); } catch (e) {} }, restartBackoff);
            restartBackoff = Math.min(restartBackoff * 2, 4000);
          };

          window.chrome.webview.addEventListener("message", (e) => {
            if (e.data === "stop") { stopped = true; try { r.stop(); } catch (_) {} }
          });

          try { r.start(); } catch (e) {
            post({ type: "error", text: "could not start: " + (e && e.message || e) });
          }
        })();
        </script>
        </body></html>
        """;

    private void SetState(CaptionEngineState s)
    {
        State = s;
        _dispatcher.BeginInvoke(new Action(() => StateChanged?.Invoke(s)));
    }

    private void Status(string msg) =>
        _dispatcher.BeginInvoke(new Action(() => SessionMessage?.Invoke(msg)));

    private void Fail(EngineFailure f)
    {
        if (f.Inner is not null)
            SpeechDiagnostics.Error("Web", f.Inner);
        else
            SpeechDiagnostics.Error("Web", f.Title + " — " + f.Message);
        SetState(CaptionEngineState.Error);
        Status(f.Title);
        _dispatcher.BeginInvoke(new Action(() => Failed?.Invoke(f)));
    }
}
