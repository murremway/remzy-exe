using System.Diagnostics;
using System.IO;

namespace Yinka;

/// <summary>
/// Append-only diagnostics log used by every caption engine and the audio meter.
/// Path: %LOCALAPPDATA%\Yinka\speech.log. Failures here are intentionally swallowed
/// (we never want logging to break the app), and concurrent writes are serialized
/// via a static lock since engines may live on different threads.
/// </summary>
public static class SpeechDiagnostics
{
    private static readonly object Gate = new();

    public static string LogDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yinka");

    public static string LogPath { get; } = Path.Combine(LogDirectory, "speech.log");

    public static void Info(string source, string message) => Write("INFO ", source, message);
    public static void Warn(string source, string message) => Write("WARN ", source, message);
    public static void Error(string source, string message) => Write("ERROR", source, message);

    public static void Error(string source, Exception ex) =>
        Write("ERROR", source, ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);

    private static void Write(string level, string source, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {level} [{source}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            /* logging must never throw */
        }
    }

    /// <summary>Open the speech.log in the user's default text editor (Notepad on Windows).</summary>
    public static void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            if (!File.Exists(LogPath))
                File.WriteAllText(LogPath, "(empty)" + Environment.NewLine);
            Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error("OpenLog", ex);
        }
    }
}
