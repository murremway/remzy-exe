using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yinka;

/// <summary>
/// User-tweakable preferences that survive between sessions. Stored as JSON at
/// %LOCALAPPDATA%\Yinka\settings.json. Saves are best-effort; missing/corrupt files
/// produce defaults so a bad save can never lock the user out.
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("engine")]
    public CaptionEngineKind Engine { get; set; } = CaptionEngineKind.WinRt;

    /// <summary>NAudio device index (-1 = system default).</summary>
    [JsonPropertyName("mic_device_index")]
    public int MicDeviceIndex { get; set; } = -1;

    /// <summary>Friendly name of the chosen mic, used to re-bind across reboots when index shifts.</summary>
    [JsonPropertyName("mic_device_name")]
    public string? MicDeviceName { get; set; }

    [JsonPropertyName("auto_restart_winrt")]
    public bool AutoRestartWinRt { get; set; } = true;

    /// <summary>Initial silence timeout for WinRT in seconds. Default 30 (vs WinRT default ~5).</summary>
    [JsonPropertyName("winrt_initial_silence_seconds")]
    public int WinRtInitialSilenceSeconds { get; set; } = 30;

    /// <summary>End-of-utterance silence in seconds.</summary>
    [JsonPropertyName("winrt_end_silence_seconds")]
    public int WinRtEndSilenceSeconds { get; set; } = 5;

    /// <summary>Babble timeout (continuous noise without recognized words) in seconds.</summary>
    [JsonPropertyName("winrt_babble_seconds")]
    public int WinRtBabbleSeconds { get; set; } = 30;

    [JsonPropertyName("push_to_talk_enabled")]
    public bool PushToTalkEnabled { get; set; }

    /// <summary>Virtual-key code for push-to-talk (default RightControl = 0xA3).</summary>
    [JsonPropertyName("push_to_talk_vk")]
    public int PushToTalkVk { get; set; } = 0xA3;

    /// <summary>Whisper model id; downloaded lazily into %LOCALAPPDATA%\Yinka\models\.</summary>
    [JsonPropertyName("whisper_model")]
    public string WhisperModel { get; set; } = "ggml-small.en.bin";

    public static string SettingsDirectory { get; } = SpeechDiagnostics.LogDirectory;
    public static string SettingsPath { get; } = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            var parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return parsed ?? new AppSettings();
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("AppSettings", "Could not read settings.json, using defaults: " + ex.Message);
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("AppSettings", "Could not write settings.json: " + ex.Message);
        }
    }
}
