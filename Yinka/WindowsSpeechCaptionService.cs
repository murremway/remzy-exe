using Windows.Media.SpeechRecognition;

namespace Yinka;

/// <summary>
/// Windows continuous dictation. May use cloud or on-device recognition depending on Windows language settings
/// and installed speech packs (Settings → Time &amp; language → Speech).
/// </summary>
public sealed class WindowsSpeechCaptionService : IDisposable
{
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private SpeechRecognizer? _recognizer;
    private bool _started;

    public WindowsSpeechCaptionService(System.Windows.Threading.Dispatcher dispatcher) =>
        _dispatcher = dispatcher;

    public event Action<string>? PhraseCommitted;
    public event Action<string>? Hypothesis;
    public event Action<string>? SessionMessage;

    public bool IsRunning => _started;

    public async Task StartAsync()
    {
        if (_started)
            return;

        var recognizer = new SpeechRecognizer();
        recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
        var compile = await recognizer.CompileConstraintsAsync();
        if (compile.Status != SpeechRecognitionResultStatus.Success)
            throw new InvalidOperationException("Speech constraints failed to compile: " + compile.Status);

        recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
        recognizer.ContinuousRecognitionSession.Completed += OnCompleted;
        recognizer.ContinuousRecognitionSession.HypothesisGenerated += OnHypothesisGenerated;

        await recognizer.ContinuousRecognitionSession.StartAsync();

        _recognizer = recognizer;
        _started = true;
    }

    public async Task StopAsync()
    {
        if (!_started || _recognizer is null)
            return;

        var r = _recognizer;
        _recognizer = null;
        _started = false;

        try
        {
            r.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
            r.ContinuousRecognitionSession.Completed -= OnCompleted;
            r.ContinuousRecognitionSession.HypothesisGenerated -= OnHypothesisGenerated;
        }
        catch
        {
            /* ignore */
        }

        try
        {
            await r.ContinuousRecognitionSession.StopAsync();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            r.Dispose();
        }
        catch
        {
            /* ignore */
        }
    }

    private void OnHypothesisGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionHypothesisGeneratedEventArgs args)
    {
        var t = args.Hypothesis?.Text;
        if (string.IsNullOrWhiteSpace(t))
            return;
        var text = t.Trim();
        _dispatcher.BeginInvoke(new Action(() => Hypothesis?.Invoke(text)));
    }

    private void OnResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var t = args.Result?.Text;
        if (string.IsNullOrWhiteSpace(t))
            return;
        var text = t.Trim();
        _dispatcher.BeginInvoke(new Action(() => PhraseCommitted?.Invoke(text)));
    }

    private void OnCompleted(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
    {
        _started = false;
        var msg = args.Status == SpeechRecognitionResultStatus.Success
            ? "Speech session ended."
            : "Speech session ended: " + args.Status;
        _dispatcher.BeginInvoke(new Action(() => SessionMessage?.Invoke(msg)));
    }

    public void Dispose()
    {
        try
        {
            StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }
    }
}
