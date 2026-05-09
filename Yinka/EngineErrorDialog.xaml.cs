using System.Diagnostics;
using System.Windows;

namespace Yinka;

public partial class EngineErrorDialog : Window
{
    private string? _primaryUri;
    private string? _secondaryUri;

    public EngineErrorDialog(EngineFailure failure)
    {
        InitializeComponent();
        TitleText.Text = failure.Title;
        MessageText.Text = failure.Message;

        if (!string.IsNullOrWhiteSpace(failure.SettingsUri))
        {
            _primaryUri = failure.SettingsUri;
            PrimaryBtn.Content = failure.SettingsButtonLabel ?? "Open Windows Settings";
            PrimaryBtn.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(failure.SecondarySettingsUri))
        {
            _secondaryUri = failure.SecondarySettingsUri;
            SecondaryBtn.Content = failure.SecondarySettingsButtonLabel ?? "Open Windows Settings";
            SecondaryBtn.Visibility = Visibility.Visible;
        }
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_primaryUri))
            return;

        if (_primaryUri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            SettingsLinks.Open(_primaryUri);
        }
        else
        {
            try { Process.Start(new ProcessStartInfo(_primaryUri) { UseShellExecute = true }); } catch { /* ignore */ }
        }
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_secondaryUri))
            return;

        if (_secondaryUri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            SettingsLinks.Open(_secondaryUri);
        }
        else
        {
            try { Process.Start(new ProcessStartInfo(_secondaryUri) { UseShellExecute = true }); } catch { /* ignore */ }
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e) => SpeechDiagnostics.OpenLog();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
