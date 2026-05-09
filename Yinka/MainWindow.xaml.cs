using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Yinka;

public partial class MainWindow : Window
{
    private readonly KjvBibleStore _kjv = new();
    private readonly WindowsSpeechCaptionService _speech;
    private readonly ObservableCollection<ParsedReference> _detections = new();
    private readonly ObservableCollection<VersePayload> _queue = new();
    private readonly DispatcherTimer _scanDebounce;

    private BroadcastWindow? _broadcast;
    private VersePayload? _previewPayload;

    public MainWindow()
    {
        InitializeComponent();
        _speech = new WindowsSpeechCaptionService(Dispatcher);
        _speech.PhraseCommitted += OnSpeechPhraseCommitted;
        _speech.Hypothesis += OnSpeechHypothesis;
        _speech.SessionMessage += OnSpeechSessionMessage;

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
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateScreens();
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
            if (_speech.IsRunning)
                _speech.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }
        base.OnClosing(e);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.L)
            return;
        if (Keyboard.FocusedElement is TextBox)
            return;
        GoLive_Click(sender, e);
        e.Handled = true;
    }

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
            SetStatus("Starting Windows speech…");
            await _speech.StartAsync().ConfigureAwait(true);
            StartCaptionBtn.IsEnabled = false;
            StopCaptionBtn.IsEnabled = true;
            SetStatus("Listening (Windows speech).");
        }
        catch (Exception ex)
        {
            SetStatus("Speech error: " + ex.Message);
            MessageBox.Show(
                "Could not start Windows speech recognition. Grant microphone access in Windows Settings → Privacy → Microphone, and ensure an English speech pack is installed.\n\n" + ex.Message,
                "Yinka",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void StopCaption_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _speech.StopAsync().ConfigureAwait(true);
        }
        catch
        {
            /* ignore */
        }
        StartCaptionBtn.IsEnabled = true;
        StopCaptionBtn.IsEnabled = false;
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

    private void OnSpeechSessionMessage(string message) => SetStatus(message);

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
        _speech.Dispose();
        base.OnClosed(e);
    }

    private sealed record ScreenOption(int Index, string Label)
    {
        public override string ToString() => Label;
    }
}
