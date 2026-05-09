using System.Diagnostics;

namespace Yinka;

/// <summary>
/// Helpers for opening Windows Settings pages relevant to speech / mic permissions.
/// Each method launches a `ms-settings:` URI via the shell. Callers should still
/// surface plain-text fallback steps if the launch fails (older Windows builds, no UI).
/// </summary>
public static class SettingsLinks
{
    public const string SpeechPage = "ms-settings:speech";
    public const string OnlineSpeechPrivacy = "ms-settings:privacy-speech";
    public const string MicrophonePrivacy = "ms-settings:privacy-microphone";
    public const string SoundDevices = "ms-settings:sound";

    public static bool Open(string msSettingsUri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(msSettingsUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("SettingsLinks", $"Failed to open {msSettingsUri}: {ex.Message}");
            return false;
        }
    }
}
