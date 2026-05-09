using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yinka;

public partial class MainWindow : Window
{
    private readonly KjvBibleStore _kjv = new();
    private readonly ObservableCollection<ParsedReference> _detections = new();
    private readonly ObservableCollection<VersePayload> _queue = new();
    private readonly DispatcherTimer _scanDebounce;

    private readonly AppSettings _settings;
    private readonly AudioMeter _meter;
    private readonly PushToTalk _ptt;

    private ICaptionEngine _engine = null!;
    private BroadcastWindow? _broadcast;
    private VersePayload? _previewPayload;
    private bool _suppressPersist = true;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _meter = new AudioMeter(Dispatcher);
        _meter.LevelChanged += OnMeterLevelChanged;
        _ptt = new PushToTalk();
        _ptt.Pressed += OnPushToTalkPressed;
        _ptt.Released += OnPushToTalkReleased;

        DetectionsList.ItemsSource = _detections;
        QueueList.ItemsSource = _queue;

        _scanDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _scanDebounce.Tick += (_, _) =>
        {
            _scanDebounce.Stop();
            if (AutoScanRefsCheck.IsChecked == true)
                ApplyDetectionsFromTranscript(silent: true);
        };

        Loaded += MainWindow_Loaded;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        TranscriptBox.TextChanged += (_, _) => RestartScanDebounce();

        ScreenBox.SelectionChanged += (_, _) =>
        {
            if (_broadcast is not null)
                SyncBroadcastLayout();
        };

        BuildEngine(_settings.Engine);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateScreens();
        PopulateEngineBox();
        PopulateMicBox();
        PopulatePushToTalkBox();
        ApplySettingsToUi();
        _suppressPersist = false;

        StartMeterIfPossible();

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "en_kjv.json");
        SetStatus("Loading bundled KJV…");
        await Task.Run(() => _kjv.LoadFromFile(path)).ConfigureAwait(true);
        if (!_kjv.IsLoaded)
        {
            SetStatus(_kjv.LoadError ?? "KJV failed to load.");
            MessageBox.Show(
                _kjv.LoadError ?? "Could not load bundled KJV. Ensure Data/en_kjv.json is next to the app.",
                "Yinka",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetStatus("KJV ready (fully offline).");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        try
        {
            if (_engine.IsRunning)
                _engine.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch { /* ignore */ }
        base.OnClosing(e);
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.L)
            return;
        if (Keyboard.FocusedElement is TextBox)
            return;
        GoLive_Click(sender, e);
        e.Handled = true;
    }

    // ---------- Engine plumbing ----------

    private void BuildEngine(CaptionEngineKind kind)
    {
        DisposeEngine();
        _engine = kind switch
        {
            CaptionEngineKind.Sapi => new SapiSpeechEngine(Dispatcher),
            CaptionEngineKind.WebSpeech => new WebSpeechEngine(Dispatcher),
            CaptionEngineKind.Whisper => new WhisperEngine(Dispatcher, _settings, () => GetSelectedMicIndex()),
            _ => new WinRtSpeechEngine(Dispatcher, _settings),
        };
        _engine.PhraseCommitted += OnSpeechPhraseCommitted;
        _engine.Hypothesis += OnSpeechHypothesis;
        _engine.SessionMessage += SetStatus;
        _engine.StateChanged += OnEngineStateChanged;
        _engine.Failed += OnEngineFailed;

        var probe = _engine.Probe();
        if (!probe.IsAvailable)
        {
            SetStatus($"{kind} unavailable: {probe.Reason}");
            SpeechDiagnostics.Warn("MainWindow", $"Engine {kind} unavailable: {probe.Reason}");
        }
    }

    private void DisposeEngine()
    {
        if (_engine is null)
            return;
        try
        {
            _engine.PhraseCommitted -= OnSpeechPhraseCommitted;
            _engine.Hypothesis -= OnSpeechHypothesis;
            _engine.SessionMessage -= SetStatus;
            _engine.StateChanged -= OnEngineStateChanged;
            _engine.Failed -= OnEngineFailed;
            _engine.Dispose();
        }
        catch { /* ignore */ }
    }

    private void OnEngineStateChanged(CaptionEngineState s)
    {
        var color = s switch
        {
            CaptionEngineState.Listening => "#5CDB95",
            CaptionEngineState.Starting => "#F2C14E",
            CaptionEngineState.Reconnecting => "#F2C14E",
            CaptionEngineState.Stopping => "#F2C14E",
            CaptionEngineState.Error => "#E76F51",
            _ => "#7A7A7A",
        };
        StatusDot.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom(color)!;

        StartCaptionBtn.IsEnabled = s is CaptionEngineState.Idle or CaptionEngineState.Error;
        StopCaptionBtn.IsEnabled = s is CaptionEngineState.Listening or CaptionEngineState.Reconnecting or CaptionEngineState.Starting;
    }

    private void OnEngineFailed(EngineFailure failure)
    {
        var dlg = new EngineErrorDialog(failure) { Owner = this };
        dlg.ShowDialog();
    }

    // ---------- Engine + mic UI ----------

    private void PopulateEngineBox()
    {
        EngineBox.Items.Clear();
        EngineBox.Items.Add(new EngineOption(CaptionEngineKind.WinRt, "WinRT (Windows.Media.SpeechRecognition)"));
        EngineBox.Items.Add(new EngineOption(CaptionEngineKind.Sapi, "SAPI (legacy desktop, most reliable)"));
        EngineBox.Items.Add(new EngineOption(CaptionEngineKind.WebSpeech, "Web Speech (WebView2 + Chromium, online)"));
        EngineBox.Items.Add(new EngineOption(CaptionEngineKind.Whisper, "Whisper (offline neural, ~470 MB model)"));

        var match = EngineBox.Items.Cast<EngineOption>().FirstOrDefault(o => o.Kind == _settings.Engine);
        EngineBox.SelectedItem = match ?? EngineBox.Items[0];
    }

    private void PopulateMicBox()
    {
        MicBox.Items.Clear();
        foreach (var d in MicEnumerator.List())
            MicBox.Items.Add(d);

        var idx = MicEnumerator.ResolveDeviceIndex(_settings.MicDeviceIndex, _settings.MicDeviceName);
        var match = MicBox.Items.Cast<MicEnumerator.MicDevice>().FirstOrDefault(d => d.Index == idx)
                    ?? MicBox.Items.Cast<MicEnumerator.MicDevice>().First();
        MicBox.SelectedItem = match;
    }

    private void PopulatePushToTalkBox()
    {
        PushToTalkKeyBox.Items.Clear();
        foreach (var (vk, label) in PushToTalk.CommonKeys)
            PushToTalkKeyBox.Items.Add(new PushToTalkOption(vk, label));

        var match = PushToTalkKeyBox.Items.Cast<PushToTalkOption>().FirstOrDefault(o => o.Vk == _settings.PushToTalkVk)
                    ?? (PushToTalkOption)PushToTalkKeyBox.Items[0]!;
        PushToTalkKeyBox.SelectedItem = match;
    }

    private void ApplySettingsToUi()
    {
        PushToTalkCheck.IsChecked = _settings.PushToTalkEnabled;
        if (_settings.PushToTalkEnabled)
            EnablePushToTalk();
    }

    private int GetSelectedMicIndex() =>
        MicBox.SelectedItem is MicEnumerator.MicDevice d ? d.Index : -1;

    private void StartMeterIfPossible() => _meter.Start(GetSelectedMicIndex());

    private void OnMeterLevelChanged(float level)
    {
        var maxWidth = (MeterFill.Parent is FrameworkElement fe ? fe.ActualWidth : 240) - 2;
        if (maxWidth < 4) maxWidth = 4;
        var w = Math.Max(0, Math.Min(maxWidth, level * maxWidth * 1.2));
        MeterFill.Width = w;

        // Color escalates with level.
        var color = level switch
        {
            < 0.25f => "#2D6A4F",
            < 0.7f => "#F2C14E",
            _ => "#E76F51",
        };
        MeterFill.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(color)!;
    }

    // ---------- Settings event handlers ----------

    private async void EngineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPersist) return;
        if (EngineBox.SelectedItem is not EngineOption opt) return;
        if (_engine?.Kind == opt.Kind) return;

        if (_engine?.IsRunning == true)
        {
            try { await _engine.StopAsync(); } catch { /* ignore */ }
        }

        _settings.Engine = opt.Kind;
        _settings.Save();
        BuildEngine(opt.Kind);
        SetStatus($"Switched to {opt.Label}.");
    }

    private void MicBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPersist) return;
        if (MicBox.SelectedItem is not MicEnumerator.MicDevice d) return;

        _settings.MicDeviceIndex = d.Index;
        _settings.MicDeviceName = d.Index < 0 ? null : d.ProductName;
        _settings.Save();

        StartMeterIfPossible();
        SetStatus($"Mic set to: {d.ProductName}");
    }

    private void PushToTalk_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPersist) return;
        _settings.PushToTalkEnabled = PushToTalkCheck.IsChecked == true;
        _settings.Save();
        if (_settings.PushToTalkEnabled)
            EnablePushToTalk();
        else
            _ptt.Stop();
    }

    private void PushToTalkKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPersist) return;
        if (PushToTalkKeyBox.SelectedItem is not PushToTalkOption opt) return;
        _settings.PushToTalkVk = opt.Vk;
        _settings.Save();
        if (_settings.PushToTalkEnabled)
            EnablePushToTalk();
    }

    private void EnablePushToTalk()
    {
        _ptt.Stop();
        if (!_ptt.Start(_settings.PushToTalkVk))
            SetStatus("Push-to-talk could not install the keyboard hook.");
    }

    private async void OnPushToTalkPressed()
    {
        if (_engine.IsRunning) return;
        try { await _engine.StartAsync(); } catch (Exception ex) { SpeechDiagnostics.Error("MainWindow", ex); }
    }

    private async void OnPushToTalkReleased()
    {
        if (!_engine.IsRunning) return;
        try { await _engine.StopAsync(); } catch (Exception ex) { SpeechDiagnostics.Error("MainWindow", ex); }
    }

    private void OpenSpeechLog_Click(object sender, RoutedEventArgs e) => SpeechDiagnostics.OpenLog();

    // ---------- Existing dashboard plumbing (unchanged below) ----------

    private void PopulateScreens()
    {
        ScreenBox.Items.Clear();
        ScreenBox.Items.Add(new ScreenOption(-1, "Primary display — windowed (centered)"));
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            var primary = s.Primary ? " (primary)" : "";
            ScreenBox.Items.Add(new ScreenOption(i, $"Screen {i + 1}: {s.DeviceName}{primary}"));
        }
        ScreenBox.SelectedIndex = 0;
    }

    private int GetSelectedScreenIndex()
    {
        if (ScreenBox.SelectedItem is ScreenOption opt)
            return opt.Index;
        return -1;
    }

    private void ObsOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (_broadcast is not null)
            SyncBroadcastLayout();
    }

    private void SyncBroadcastLayout()
    {
        if (_broadcast is null)
            return;

        var idx = GetSelectedScreenIndex();
        var chroma = ChromaObsCheck.IsChecked == true;
        var top = TopmostObsCheck.IsChecked == true;
        var obs1080 = Obs1080Check.IsChecked == true;

        _broadcast.ApplyChromaAndTopmost(chroma, top);

        if (obs1080)
            _broadcast.Move1080pCentered(idx < 0 ? 0 : idx);
        else if (idx < 0)
            _broadcast.MoveCenteredWindowed();
        else
            _broadcast.MoveToScreen(idx);
    }

    private void OpenBroadcast_Click(object sender, RoutedEventArgs e)
    {
        _broadcast?.Close();
        _broadcast = new BroadcastWindow();
        _broadcast.Closed += (_, _) => _broadcast = null;
        _broadcast.Show();
        SyncBroadcastLayout();
        SyncBroadcastFromLive();
        SetStatus("Broadcast window opened for OBS Window Capture.");
    }

    private void RestartScanDebounce()
    {
        if (AutoScanRefsCheck.IsChecked != true)
            return;
        _scanDebounce.Stop();
        _scanDebounce.Start();
    }

    private async void StartCaption_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Starting speech engine…");
            await _engine.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus("Speech error: " + ex.Message);
            SpeechDiagnostics.Error("MainWindow", ex);
            new EngineErrorDialog(new EngineFailure(
                "Could not start the speech engine.",
                ex.Message,
                Inner: ex)) { Owner = this }.ShowDialog();
        }
    }

    private async void StopCaption_Click(object sender, RoutedEventArgs e)
    {
        try { await _engine.StopAsync().ConfigureAwait(true); } catch { /* ignore */ }
        HearingLine.Text = "";
        SetStatus("Speech caption stopped.");
    }

    private void OnSpeechPhraseCommitted(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return;

        var t = TranscriptBox.Text;
        if (t.Length > 0 && !char.IsWhiteSpace(t[^1]))
            TranscriptBox.AppendText(" ");
        TranscriptBox.AppendText(phrase);
        TranscriptBox.CaretIndex = TranscriptBox.Text.Length;
        RestartScanDebounce();
    }

    private void OnSpeechHypothesis(string hypothesis)
    {
        HearingLine.Text = string.IsNullOrWhiteSpace(hypothesis) ? "" : "Listening… " + hypothesis;
    }

    private void Scan_Click(object sender, RoutedEventArgs e) =>
        ApplyDetectionsFromTranscript(silent: false);

    private void ApplyDetectionsFromTranscript(bool silent)
    {
        var text = TranscriptBox.Text ?? "";
        var found = BibleReferenceParser.FindReferences(text);
        _detections.Clear();
        foreach (var f in found)
            _detections.Add(f);
        if (!silent)
            SetStatus(found.Count == 0 ? "No references found." : $"Found {found.Count} reference(s).");
    }

    private void Sample_Click(object sender, RoutedEventArgs e)
    {
        const string sample = """
            Good morning. Please turn to John chapter 3 verse 16.
            We'll also read Romans 12:1 before we close in Philippians 4:6-7.
            """;
        TranscriptBox.AppendText(TranscriptBox.Text.Length > 0 ? Environment.NewLine + sample : sample);
        TranscriptBox.CaretIndex = TranscriptBox.Text.Length;
        Scan_Click(sender, e);
    }

    private void ClearTranscript_Click(object sender, RoutedEventArgs e)
    {
        TranscriptBox.Clear();
        _detections.Clear();
        HearingLine.Text = "";
        SetStatus("Transcript cleared.");
    }

    private void SaveTranscript_Click(object sender, RoutedEventArgs e)
    {
        var text = TranscriptBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Transcript is empty—nothing to save.");
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save transcript",
            Filter = "Text files (*.txt)|*.txt|Markdown (*.md)|*.md|All files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = $"yinka-transcript-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            var header = $"# Yinka transcript{Environment.NewLine}# Saved {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}{Environment.NewLine}";
            File.WriteAllText(dlg.FileName, header + text);
            SetStatus($"Transcript saved to {dlg.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus("Save failed: " + ex.Message);
            SpeechDiagnostics.Error("MainWindow", ex);
            MessageBox.Show(this, "Could not save transcript:\n\n" + ex.Message, "Yinka", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadKjv_Click(object sender, RoutedEventArgs e)
    {
        if (DetectionsList.SelectedItem is not ParsedReference r)
        {
            SetStatus("Select a detection first.");
            return;
        }
        LoadPreviewForReference(r, notify: true);
    }

    private void LoadPreview_Click(object sender, RoutedEventArgs e)
    {
        if (DetectionsList.SelectedItem is not ParsedReference r)
        {
            SetStatus("Select a detection.");
            return;
        }
        LoadPreviewForReference(r, notify: true);
    }

    private void LoadPreviewForReference(ParsedReference r, bool notify)
    {
        if (!_kjv.IsLoaded)
        {
            SetStatus(_kjv.LoadError ?? "KJV not loaded.");
            return;
        }

        var payload = _kjv.GetPassage(r);
        if (payload is null)
        {
            if (notify)
                SetStatus("Could not load that passage from bundled KJV.");
            return;
        }

        _previewPayload = payload;
        PreviewRef.Text = payload.Reference + " · " + payload.TranslationName;
        PreviewBody.Text = payload.Text;
        if (notify)
            SetStatus("Preview updated (offline KJV).");
    }

    private void Queue_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPayload is null)
        {
            SetStatus("Preview is empty—load KJV first.");
            return;
        }
        if (_queue.Any(q => q.Reference == _previewPayload.Reference && q.Text == _previewPayload.Text))
        {
            SetStatus("Already in queue.");
            return;
        }
        _queue.Add(_previewPayload);
        SetStatus("Added to queue.");
    }

    private void PresentFromQueue_Click(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not VersePayload p)
        {
            SetStatus("Select a queue item.");
            return;
        }
        ApplyPayloadToPreview(p);
        GoLive_Click(sender, e);
    }

    private void RemoveQueue_Click(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not VersePayload p)
            return;
        _queue.Remove(p);
    }

    private void ApplyPayloadToPreview(VersePayload p)
    {
        _previewPayload = p;
        PreviewRef.Text = p.Reference + " · " + p.TranslationName;
        PreviewBody.Text = p.Text;
    }

    private void GoLive_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPayload is null)
        {
            SetStatus("Nothing in preview.");
            return;
        }
        LiveRef.Text = _previewPayload.Reference + " · " + _previewPayload.TranslationName;
        LiveBody.Text = _previewPayload.Text;
        SyncBroadcastFromLive();
        SetStatus("Live output updated.");
    }

    private void SyncBroadcastFromLive()
    {
        if (_broadcast is null || string.IsNullOrWhiteSpace(LiveBody.Text))
            return;
        _broadcast.SetVerse(LiveRef.Text, LiveBody.Text);
    }

    private void ManualLookup_Click(object sender, RoutedEventArgs e)
    {
        var q = (ManualQueryBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(q))
        {
            SetStatus("Enter a reference.");
            return;
        }

        var refs = BibleReferenceParser.FindReferences(q);
        if (refs.Count == 0)
        {
            SetStatus("Could not parse reference. Try e.g. John 3:16");
            return;
        }

        LoadPreviewForReference(refs[0], notify: true);
    }

    private void DetectionsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DetectionsList.SelectedItem is ParsedReference r)
            LoadPreviewForReference(r, notify: true);
    }

    private void QueueList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (QueueList.SelectedItem is VersePayload p)
            ApplyPayloadToPreview(p);
    }

    private void SetStatus(string message) => StatusText.Text = message;

    protected override void OnClosed(EventArgs e)
    {
        _broadcast?.Close();
        _ptt.Dispose();
        _meter.Dispose();
        DisposeEngine();
        base.OnClosed(e);
    }

    private sealed record ScreenOption(int Index, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record EngineOption(CaptionEngineKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record PushToTalkOption(int Vk, string Label)
    {
        public override string ToString() => Label;
    }
}
