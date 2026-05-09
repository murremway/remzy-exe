using System.Runtime.InteropServices;
using System.Windows;

namespace Yinka;

/// <summary>
/// Global push-to-talk: when the configured key is held anywhere in Windows the engine
/// runs; when released it stops. Implemented via Win32 RegisterHotKey + KeyboardLLHook
/// (the hotkey alone gives us press; we use the LL hook for the release event).
///
/// Falls back gracefully if either Win32 call fails.
/// </summary>
public sealed class PushToTalk : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private int _vkCode;
    private bool _down;

    public event Action? Pressed;
    public event Action? Released;

    public PushToTalk()
    {
        _proc = HookCallback;
    }

    public bool Start(int virtualKey)
    {
        Stop();
        _vkCode = virtualKey;
        _down = false;
        _hookId = SetHook(_proc);
        if (_hookId == IntPtr.Zero)
        {
            SpeechDiagnostics.Warn("PTT", "SetWindowsHookEx returned 0; push-to-talk disabled.");
            return false;
        }
        SpeechDiagnostics.Info("PTT", $"Hook installed for vkCode=0x{virtualKey:X}.");
        return true;
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero)
            return;
        try { _ = UnhookWindowsHookEx(_hookId); } catch { /* ignore */ }
        _hookId = IntPtr.Zero;
        _down = false;
    }

    public void Dispose() => Stop();

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vk = Marshal.ReadInt32(lParam);
            if (vk == _vkCode)
            {
                var msg = (int)wParam;
                if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && !_down)
                {
                    _down = true;
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Pressed?.Invoke()));
                }
                else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _down)
                {
                    _down = false;
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Released?.Invoke()));
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    /// <summary>Common VK codes the user might pick from. Names mirror System.Windows.Input.Key roughly.</summary>
    public static readonly (int Vk, string Label)[] CommonKeys =
    {
        (0xA3, "Right Ctrl"),
        (0xA2, "Left Ctrl"),
        (0xA0, "Left Shift"),
        (0xA1, "Right Shift"),
        (0x14, "Caps Lock"),
        (0x73, "F4"),
        (0x74, "F5"),
        (0x76, "F7"),
        (0x78, "F9"),
        (0x7A, "F11"),
    };
}
