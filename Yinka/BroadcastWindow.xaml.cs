using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace Yinka;

public partial class BroadcastWindow : Window
{
    private static readonly SolidColorBrush DefaultBg = new(MediaColor.FromRgb(0x0D, 0x11, 0x17));
    private static readonly SolidColorBrush ChromaBg = new(MediaColor.FromRgb(0x00, 0xFF, 0x00));
    private static readonly SolidColorBrush LightFg = new(MediaColor.FromRgb(0xF0, 0xF6, 0xFC));
    private static readonly SolidColorBrush DarkFg = new(MediaColor.FromRgb(0x11, 0x11, 0x11));

    public BroadcastWindow()
    {
        InitializeComponent();
    }

    public void SetVerse(string reference, string body)
    {
        ReferenceBlock.Text = reference;
        VerseBlock.Text = body;
    }

    /// <summary>Fullscreen verse display on a specific monitor.</summary>
    public void MoveToScreen(int screenIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
            return;

        var idx = Math.Clamp(screenIndex, 0, screens.Length - 1);
        var b = screens[idx].Bounds;

        WindowState = WindowState.Normal;
        Left = b.Left;
        Top = b.Top;
        Width = Math.Max(320, b.Width);
        Height = Math.Max(240, b.Height);
        WindowState = WindowState.Maximized;
    }

    /// <summary>Large window centered on the primary display.</summary>
    public void MoveCenteredWindowed()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
            return;

        var b = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? screens[0].WorkingArea;
        WindowState = WindowState.Normal;
        Width = Math.Min(1100, b.Width * 0.92);
        Height = Math.Min(620, b.Height * 0.92);
        Left = b.Left + (b.Width - Width) / 2;
        Top = b.Top + (b.Height - Height) / 2;
    }

    /// <summary>1920×1080 window centered on the chosen display (handy for OBS Window Capture scaling).</summary>
    public void Move1080pCentered(int screenIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
            return;

        var idx = screenIndex < 0 ? 0 : Math.Clamp(screenIndex, 0, screens.Length - 1);
        var b = screens[idx].WorkingArea;

        WindowState = WindowState.Normal;
        Width = 1920;
        Height = 1080;
        Left = b.Left + Math.Max(0, (b.Width - 1920) / 2);
        Top = b.Top + Math.Max(0, (b.Height - 1080) / 2);
    }

    public void ApplyChromaAndTopmost(bool chromaKeyGreen, bool topmost)
    {
        Topmost = topmost;

        if (chromaKeyGreen)
        {
            Background = ChromaBg;
            Foreground = DarkFg;
            ReferenceBlock.Foreground = DarkFg;
            VerseBlock.Foreground = DarkFg;
        }
        else
        {
            Background = DefaultBg;
            Foreground = LightFg;
            ReferenceBlock.Foreground = LightFg;
            VerseBlock.Foreground = LightFg;
        }
    }
}
