using NAudio.Wave;

namespace Yinka;

/// <summary>NAudio-based microphone enumeration. Index -1 = "system default" (WaveIn device 0).</summary>
public static class MicEnumerator
{
    public sealed record MicDevice(int Index, string ProductName)
    {
        public override string ToString() => Index < 0 ? "Default capture device" : $"#{Index + 1}  {ProductName}";
    }

    public static IReadOnlyList<MicDevice> List()
    {
        var list = new List<MicDevice> { new(-1, "Default capture device") };
        try
        {
            var count = WaveInEvent.DeviceCount;
            for (var i = 0; i < count; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                list.Add(new MicDevice(i, caps.ProductName));
            }
        }
        catch (Exception ex)
        {
            SpeechDiagnostics.Warn("MicEnumerator", "Failed enumerating capture devices: " + ex.Message);
        }
        return list;
    }

    /// <summary>Best-effort match by friendly name when a saved index has shifted (USB device re-plugged, etc.).</summary>
    public static int ResolveDeviceIndex(int savedIndex, string? savedName)
    {
        var devices = List();
        if (savedIndex >= 0 && savedIndex < devices.Count - 1)
        {
            // devices[0] is Default at index -1, devices[i+1] corresponds to NAudio index i
            return savedIndex;
        }
        if (!string.IsNullOrWhiteSpace(savedName))
        {
            var hit = devices.FirstOrDefault(d => d.Index >= 0 && string.Equals(d.ProductName, savedName, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit.Index;
        }
        return -1;
    }
}
