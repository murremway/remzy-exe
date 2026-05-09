namespace Yinka;

public enum CaptionEngineKind
{
    /// <summary>Windows.Media.SpeechRecognition (WinRT). Cloud or on-device, depends on Windows speech settings.</summary>
    WinRt,

    /// <summary>System.Speech (SAPI 5.4). Pure desktop, no online toggle / language pack required.</summary>
    Sapi,

    /// <summary>WebView2 hosting webkitSpeechRecognition. Requires WebView2 Runtime and internet.</summary>
    WebSpeech,

    /// <summary>Whisper.NET local inference. Fully offline; downloads a model on first use.</summary>
    Whisper,
}

public enum CaptionEngineState
{
    Idle,
    Starting,
    Listening,
    Reconnecting,
    Stopping,
    Error,
}

/// <summary>Engine availability status, used to disable picker entries that won't work on this machine.</summary>
public sealed record EngineAvailability(bool IsAvailable, string? Reason);

/// <summary>
/// Common contract for all caption engines. Each engine raises events on the captured
/// <see cref="System.Windows.Threading.Dispatcher"/> the UI passes in via the constructor.
/// Engines must be safe to Stop -> Start repeatedly. Disposal must be idempotent.
/// </summary>
public interface ICaptionEngine : IDisposable
{
    CaptionEngineKind Kind { get; }
    CaptionEngineState State { get; }
    bool IsRunning { get; }

    /// <summary>Final transcript fragment. Already trimmed; never null/empty.</summary>
    event Action<string>? PhraseCommitted;

    /// <summary>Live partial text. Empty string clears the line.</summary>
    event Action<string>? Hypothesis;

    /// <summary>Human-readable status updates for the StatusText / status dot.</summary>
    event Action<string>? SessionMessage;

    /// <summary>Fires when state changes; UI updates the status dot color from this.</summary>
    event Action<CaptionEngineState>? StateChanged;

    /// <summary>Fired with a structured failure that the UI can turn into a deep-linked dialog.</summary>
    event Action<EngineFailure>? Failed;

    /// <summary>Synchronously check whether the engine can plausibly run on this machine.</summary>
    EngineAvailability Probe();

    Task StartAsync();
    Task StopAsync();
}

/// <summary>
/// Structured engine failure. <see cref="SettingsUri"/> is a `ms-settings:` deep-link
/// the UI can turn into a clickable button so the user can fix the underlying issue.
/// </summary>
public sealed record EngineFailure(
    string Title,
    string Message,
    string? SettingsUri = null,
    string? SettingsButtonLabel = null,
    string? SecondarySettingsUri = null,
    string? SecondarySettingsButtonLabel = null,
    Exception? Inner = null);
